using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Purchasing;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services;

/// <summary>
/// Purchase orders: create/list/edit a supplier order, mark it sent, and RECEIVE it — which
/// atomically adds stock, updates each product's last cost, and (optionally) records a payable
/// to the supplier. Additive: nothing else depends on it, and receive is the only path that
/// mutates existing product stock (increment-only).
/// </summary>
public class PurchaseOrderService : IPurchaseOrderService
{
    private readonly AppDbContext _db;

    public PurchaseOrderService(AppDbContext db) => _db = db;

    public async Task<PurchaseOrderDto> CreateAsync(Guid businessId, CreatePurchaseOrderRequest request, Guid? userId, string? userName)
    {
        if (request.Items == null || request.Items.Count == 0)
            throw new ArgumentException("A purchase order needs at least one item.");

        var business = await _db.Businesses.FindAsync(businessId)
            ?? throw new KeyNotFoundException("Business not found.");

        var supplierName = request.SupplierName;
        if (request.SupplierId.HasValue)
        {
            var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == request.SupplierId.Value && c.BusinessId == businessId)
                ?? throw new KeyNotFoundException("Supplier contact not found.");
            supplierName ??= contact.Name;
        }

        var count = await _db.PurchaseOrders.CountAsync(p => p.BusinessId == businessId);

        var po = new PurchaseOrder
        {
            BusinessId = businessId,
            SupplierId = request.SupplierId,
            SupplierName = supplierName,
            PoNumber = $"PO-{count + 1:D4}",
            Status = PurchaseOrderStatus.Draft,
            Currency = business.Currency ?? "NGN",
            Notes = request.Notes,
            ExpectedAtUtc = request.ExpectedAtUtc,
            RecordedByUserId = userId,
            RecordedByName = userName,
        };
        po.Items = request.Items.Select(BuildItem).ToList();
        po.TotalAmount = po.Items.Sum(i => i.LineTotal);

        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();
        return ToDto(po);
    }

    public async Task<PaginatedResult<PurchaseOrderDto>> ListAsync(Guid businessId, string? status, int page, int pageSize)
    {
        var query = _db.PurchaseOrders.Include(p => p.Items).Where(p => p.BusinessId == businessId);

        if (!string.IsNullOrWhiteSpace(status) && status != "all"
            && Enum.TryParse<PurchaseOrderStatus>(status, ignoreCase: true, out var st))
            query = query.Where(p => p.Status == st);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(p => p.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PaginatedResult<PurchaseOrderDto>
        {
            Items = items.Select(ToDto).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<PurchaseOrderDto> GetByIdAsync(Guid businessId, Guid id)
        => ToDto(await LoadAsync(businessId, id));

    public async Task<PurchaseOrderDto> UpdateAsync(Guid businessId, Guid id, UpdatePurchaseOrderRequest request)
    {
        var po = await LoadAsync(businessId, id);
        if (po.Status != PurchaseOrderStatus.Draft)
            throw new InvalidOperationException("Only a draft purchase order can be edited. Cancel and recreate instead.");

        if (request.SupplierId.HasValue || request.SupplierName != null)
        {
            po.SupplierId = request.SupplierId;
            po.SupplierName = request.SupplierName;
            if (request.SupplierId.HasValue)
            {
                var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == request.SupplierId.Value && c.BusinessId == businessId)
                    ?? throw new KeyNotFoundException("Supplier contact not found.");
                po.SupplierName ??= contact.Name;
            }
        }
        if (request.Notes != null) po.Notes = request.Notes;
        if (request.ExpectedAtUtc.HasValue) po.ExpectedAtUtc = request.ExpectedAtUtc;

        if (request.Items != null)
        {
            if (request.Items.Count == 0)
                throw new ArgumentException("A purchase order needs at least one item.");
            _db.PurchaseOrderItems.RemoveRange(po.Items);
            po.Items = request.Items.Select(BuildItem).ToList();
            po.TotalAmount = po.Items.Sum(i => i.LineTotal);
        }

        await _db.SaveChangesAsync();
        return ToDto(po);
    }

    public async Task<PurchaseOrderDto> MarkSentAsync(Guid businessId, Guid id)
    {
        var po = await LoadAsync(businessId, id);
        if (po.Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.Cancelled)
            throw new InvalidOperationException($"A {po.Status.ToString().ToLowerInvariant()} purchase order can't be marked as sent.");
        po.Status = PurchaseOrderStatus.Sent;
        po.SentAtUtc ??= DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(po);
    }

    public async Task<PurchaseOrderDto> ReceiveAsync(Guid businessId, Guid id, ReceivePurchaseOrderRequest request, Guid? userId, string? userName)
    {
        var po = await LoadAsync(businessId, id);
        if (po.Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.Cancelled)
            throw new InvalidOperationException($"This purchase order is already {po.Status.ToString().ToLowerInvariant()}.");

        var lines = (request.Lines ?? new List<ReceivePurchaseOrderItemInput>())
            .Where(l => l.QuantityReceived > 0)
            .ToDictionary(l => l.ItemId, l => l.QuantityReceived);
        if (lines.Count == 0)
            throw new ArgumentException("Nothing to receive — enter a received quantity for at least one line.");

        var now = DateTime.UtcNow;
        decimal receivedValue = 0;

        // One transaction: stock + cost + payable all commit together, or none do.
        await using var tx = await _db.Database.BeginTransactionAsync();

        foreach (var item in po.Items)
        {
            if (!lines.TryGetValue(item.Id, out var qty)) continue;

            var remaining = item.QuantityOrdered - item.QuantityReceived;
            if (remaining <= 0) continue;
            if (qty > remaining) qty = remaining; // clamp; never receive more than ordered

            // Move stock only when the line maps to a real catalog product.
            if (item.ProductId.HasValue)
            {
                var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId.Value && p.BusinessId == businessId);
                if (product != null)
                {
                    product.CurrentStock += qty;
                    if (item.UnitCost > 0) product.CostPrice = item.UnitCost; // keep last cost current
                    if (product.SupplierId == null && po.SupplierId.HasValue) product.SupplierId = po.SupplierId; // learn supplier, non-destructive

                    _db.InventoryTransactions.Add(new InventoryTransaction
                    {
                        BusinessId = businessId,
                        ProductId = product.Id,
                        Type = InventoryTransactionType.StockIn,
                        Quantity = qty,
                        UnitCost = item.UnitCost > 0 ? item.UnitCost : null,
                        Notes = $"Received {po.PoNumber}",
                        RecordedByUserId = userId,
                        RecordedByName = userName,
                        CreatedAtUtc = now,
                    });
                }
            }

            item.QuantityReceived += qty;
            receivedValue += qty * item.UnitCost;
        }

        // Recompute status from received-vs-ordered across all lines.
        var allReceived = po.Items.All(i => i.QuantityReceived >= i.QuantityOrdered);
        var anyReceived = po.Items.Any(i => i.QuantityReceived > 0);
        po.Status = allReceived ? PurchaseOrderStatus.Received
                  : anyReceived ? PurchaseOrderStatus.PartiallyReceived
                  : po.Status;
        if (allReceived) po.ReceivedAtUtc ??= now;

        // Record what we owe the supplier for this receipt (best-effort; only with a linked supplier).
        if (request.CreatePayable && po.SupplierId.HasValue && receivedValue > 0)
        {
            var supplierExists = await _db.Contacts.AnyAsync(c => c.Id == po.SupplierId.Value && c.BusinessId == businessId);
            if (supplierExists)
            {
                var payable = new LedgerEntry
                {
                    BusinessId = businessId,
                    ContactId = po.SupplierId.Value,
                    EntryType = LedgerEntryType.Payable,
                    Amount = receivedValue,
                    Notes = $"Stock received — {po.PoNumber}",
                    Source = "PurchaseOrder",
                    RecordedByUserId = userId,
                    RecordedByName = userName,
                };
                _db.LedgerEntries.Add(payable);
                po.PayableLedgerEntryId = payable.Id;
            }
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return ToDto(po);
    }

    public async Task<PurchaseOrderDto> CancelAsync(Guid businessId, Guid id)
    {
        var po = await LoadAsync(businessId, id);
        if (po.Status == PurchaseOrderStatus.Received)
            throw new InvalidOperationException("A fully received purchase order can't be cancelled.");
        if (po.Status == PurchaseOrderStatus.Cancelled)
            return ToDto(po);
        po.Status = PurchaseOrderStatus.Cancelled;
        po.CancelledAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(po);
    }

    // ── helpers ────────────────────────────────────────────────────────────────
    private async Task<PurchaseOrder> LoadAsync(Guid businessId, Guid id)
        => await _db.PurchaseOrders.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id && p.BusinessId == businessId)
           ?? throw new KeyNotFoundException("Purchase order not found.");

    private static PurchaseOrderItem BuildItem(PurchaseOrderItemInput i) => new()
    {
        ProductId = i.ProductId,
        ProductName = i.ProductName.Trim(),
        Unit = string.IsNullOrWhiteSpace(i.Unit) ? "unit" : i.Unit!,
        QuantityOrdered = i.QuantityOrdered,
        UnitCost = i.UnitCost,
        LineTotal = Math.Round(i.QuantityOrdered * i.UnitCost, 2),
    };

    private static PurchaseOrderDto ToDto(PurchaseOrder p) => new()
    {
        Id = p.Id,
        PoNumber = p.PoNumber,
        SupplierId = p.SupplierId,
        SupplierName = p.SupplierName,
        Status = p.Status.ToString(),
        Currency = p.Currency,
        TotalAmount = p.TotalAmount,
        Notes = p.Notes,
        ExpectedAtUtc = p.ExpectedAtUtc,
        RecordedByName = p.RecordedByName,
        CreatedAtUtc = p.CreatedAtUtc,
        SentAtUtc = p.SentAtUtc,
        ReceivedAtUtc = p.ReceivedAtUtc,
        CancelledAtUtc = p.CancelledAtUtc,
        Items = p.Items
            .OrderBy(i => i.ProductName)
            .Select(i => new PurchaseOrderItemDto
            {
                Id = i.Id,
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                Unit = i.Unit,
                QuantityOrdered = i.QuantityOrdered,
                QuantityReceived = i.QuantityReceived,
                UnitCost = i.UnitCost,
                LineTotal = i.LineTotal,
            }).ToList(),
    };
}

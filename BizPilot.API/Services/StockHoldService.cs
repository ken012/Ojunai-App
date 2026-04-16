using BizPilot.API.Data;
using BizPilot.API.DTOs.Inventory;
using BizPilot.API.DTOs.Sales;
using BizPilot.API.Models;
using BizPilot.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BizPilot.API.Services;

public class StockHoldService : IStockHoldService
{
    private readonly AppDbContext _db;
    private readonly ISalesService _sales;

    public StockHoldService(AppDbContext db, ISalesService sales)
    {
        _db = db;
        _sales = sales;
    }

    public async Task<StockHoldDto> CreateHoldAsync(Guid businessId, Guid productId, string contactName, decimal quantity, string? notes = null, string source = "Manual")
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == productId && p.BusinessId == businessId && p.IsActive)
            ?? throw new KeyNotFoundException("Product not found.");

        var heldQty = await GetHeldQuantityAsync(businessId, productId);
        var available = product.CurrentStock - heldQty;

        if (quantity > available)
            throw new InvalidOperationException($"Only {available:0.##} {product.Unit} of {product.Name} available (total: {product.CurrentStock:0.##}, on hold: {heldQty:0.##}).");

        var hold = new StockHold
        {
            BusinessId = businessId,
            ProductId = productId,
            ContactName = contactName.Trim(),
            Quantity = quantity,
            Notes = notes,
            Source = source,
            Status = HoldStatus.Active
        };
        _db.Set<StockHold>().Add(hold);
        await _db.SaveChangesAsync();

        return ToDto(hold, product);
    }

    public async Task<StockHoldDto> ReleaseHoldAsync(Guid businessId, Guid holdId)
    {
        var hold = await _db.Set<StockHold>()
            .Include(h => h.Product)
            .FirstOrDefaultAsync(h => h.Id == holdId && h.BusinessId == businessId && h.Status == HoldStatus.Active)
            ?? throw new KeyNotFoundException("Active hold not found.");

        hold.Status = HoldStatus.Released;
        hold.ReleasedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ToDto(hold, hold.Product);
    }

    public async Task<SaleDto> ConvertToSaleAsync(Guid businessId, Guid holdId)
    {
        // We can't wrap this in a single DB transaction because SalesService.CreateAsync opens its own
        // transaction, and Npgsql doesn't support nested transactions on the same connection. Instead we
        // use a compensating-action pattern: claim the hold (atomically, via optimistic concurrency), then
        // try to create the sale. If sale creation fails, un-claim the hold so it's available again.
        var hold = await _db.Set<StockHold>()
            .Include(h => h.Product)
            .FirstOrDefaultAsync(h => h.Id == holdId && h.BusinessId == businessId && h.Status == HoldStatus.Active)
            ?? throw new KeyNotFoundException("Active hold not found.");

        // Step 1: claim the hold. The Version row-version token prevents two concurrent conversions
        // from both succeeding — whichever SaveChanges lands second throws DbUpdateConcurrencyException.
        hold.Status = HoldStatus.Converted;
        hold.ReleasedAtUtc = DateTime.UtcNow;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("This hold is already being processed. Please refresh and try again.");
        }

        var product = hold.Product;
        var unitPrice = product.SellingPrice ?? 0;

        var contact = await _db.Contacts.FirstOrDefaultAsync(c =>
            c.BusinessId == businessId && c.Name.ToLower() == hold.ContactName.ToLower());
        Guid? contactId = contact?.Id;

        // Step 2: create the sale. If anything goes wrong, compensate by un-claiming the hold.
        try
        {
            var sale = await _sales.CreateAsync(businessId, new CreateSaleRequest
            {
                Items = new List<SaleItemRequest>
                {
                    new() { ProductId = product.Id, Quantity = hold.Quantity, UnitPrice = unitPrice }
                },
                ContactId = contactId,
                PaymentStatus = PaymentStatus.Paid
            }, hold.Source);

            return sale;
        }
        catch
        {
            // Best-effort rollback of the hold claim. If this save also fails, the hold stays in Converted
            // state and has to be cleaned up manually — but that's better than silently losing the sale.
            hold.Status = HoldStatus.Active;
            hold.ReleasedAtUtc = null;
            try { await _db.SaveChangesAsync(); }
            catch { /* swallow — we're already in a failure path and the primary error matters more */ }
            throw;
        }
    }

    public async Task<List<StockHoldDto>> GetActiveHoldsAsync(Guid businessId)
    {
        return await _db.Set<StockHold>()
            .Include(h => h.Product)
            .Where(h => h.BusinessId == businessId && h.Status == HoldStatus.Active)
            .OrderByDescending(h => h.CreatedAtUtc)
            .Select(h => new StockHoldDto
            {
                Id = h.Id,
                ProductId = h.ProductId,
                ProductName = h.Product.Name,
                Unit = h.Product.Unit,
                ContactName = h.ContactName,
                Quantity = h.Quantity,
                Notes = h.Notes,
                Status = h.Status.ToString(),
                Source = h.Source,
                CreatedAtUtc = h.CreatedAtUtc
            })
            .ToListAsync();
    }

    public async Task<decimal> GetHeldQuantityAsync(Guid businessId, Guid productId)
    {
        return await _db.Set<StockHold>()
            .Where(h => h.BusinessId == businessId && h.ProductId == productId && h.Status == HoldStatus.Active)
            .SumAsync(h => h.Quantity);
    }

    private static StockHoldDto ToDto(StockHold h, Product p) => new()
    {
        Id = h.Id,
        ProductId = h.ProductId,
        ProductName = p.Name,
        Unit = p.Unit,
        ContactName = h.ContactName,
        Quantity = h.Quantity,
        Notes = h.Notes,
        Status = h.Status.ToString(),
        Source = h.Source,
        CreatedAtUtc = h.CreatedAtUtc
    };
}

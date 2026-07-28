using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Inventory;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services;

/// <summary>
/// Stock transfers between a business's locations (multi-location Phase 3). A transfer is an instant move:
/// source ProductLocationStock down, destination up, <see cref="Product.CurrentStock"/> (the business-wide
/// roll-up) UNCHANGED — so SUM(per-location) == Product.CurrentStock still holds. The dual-write mirror
/// no-ops (it only reacts to Product.CurrentStock changes), and the central attribution stamp leaves the
/// TransferOut/TransferIn rows alone since they carry an explicit LocationId.
/// </summary>
public class StockTransferService : IStockTransferService
{
    private readonly AppDbContext _db;
    private readonly PlanGuard _planGuard;

    public StockTransferService(AppDbContext db, PlanGuard planGuard)
    {
        _db = db;
        _planGuard = planGuard;
    }

    public async Task<StockTransferDto> TransferAsync(Guid businessId, CreateStockTransferRequest request, Guid? userId = null, string? userName = null)
    {
        if (!await _planGuard.CanUseMultiLocationAsync(businessId))
            throw new InvalidOperationException("Multiple locations aren't enabled for this business.");
        if (request.Quantity <= 0)
            throw new InvalidOperationException("Transfer quantity must be greater than zero.");
        if (request.FromLocationId == request.ToLocationId)
            throw new InvalidOperationException("Choose two different locations to transfer between.");

        // The Serializable move can legitimately abort under contention — serialization failure (40001),
        // deadlock (40P01) — or race a concurrent first-time destination-row insert (unique violation 23505).
        // Those are the signal to RETRY, on a CLEARED change tracker so the failed attempt's added/edited rows
        // aren't re-applied. This serializes transfer-vs-transfer AND transfer-vs-SALE (SalesService now runs its
        // stock write Serializable too, so Postgres SSI aborts one of the pair). RESIDUAL: the OTHER stock
        // writers — InventoryService stock-in/out/adjust/damaged/wastage and StocktakeService — still run Read
        // Committed, so a rare transfer-vs-(stock-op/stocktake) race can lose an update = reconcilable
        // ProductLocationStock drift, within the documented multi-location worst case, repaired by backfill.
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ExecuteTransferAsync(businessId, request, userId, userName);
            }
            catch (Exception ex) when (attempt < maxAttempts && IsRetryable(ex))
            {
                _db.ChangeTracker.Clear();
                await Task.Delay(20 * attempt);
            }
        }
    }

    /// <summary>A Postgres transient that the transfer should retry: serialization failure, deadlock, or a
    /// unique-violation from two first-time destination rows racing (the loser retries and finds the row).</summary>
    private static bool IsRetryable(Exception ex)
    {
        for (var e = (Exception?)ex; e != null; e = e.InnerException)
            if (e is Npgsql.PostgresException pg && pg.SqlState is "40001" or "40P01" or "23505")
                return true;
        return false;
    }

    private async Task<StockTransferDto> ExecuteTransferAsync(Guid businessId, CreateStockTransferRequest request, Guid? userId, string? userName)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId && p.BusinessId == businessId && p.IsActive)
            ?? throw new KeyNotFoundException("Product not found.");
        if (product.IsBundle)
            throw new InvalidOperationException("Bundles are assembled from components — transfer the components instead.");

        var locs = await _db.Locations
            .Where(l => l.BusinessId == businessId && l.IsActive && (l.Id == request.FromLocationId || l.Id == request.ToLocationId))
            .ToListAsync();
        var from = locs.FirstOrDefault(l => l.Id == request.FromLocationId)
            ?? throw new InvalidOperationException("The source location is invalid or inactive.");
        var to = locs.FirstOrDefault(l => l.Id == request.ToLocationId)
            ?? throw new InvalidOperationException("The destination location is invalid or inactive.");

        // Serializable so two concurrent transfers touching the same per-location stock can't both commit (one
        // aborts and is retried by the caller); the ProductLocationStock CK (CurrentStock >= 0) is the final backstop.
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        var sourcePls = await _db.ProductLocationStocks.FirstOrDefaultAsync(x => x.ProductId == product.Id && x.LocationId == from.Id);
        var sourceStock = sourcePls?.CurrentStock ?? 0m;
        if (sourceStock < request.Quantity)
            throw new InvalidOperationException(
                $"Only {sourceStock:0.####} {UnitFormat.Plural(sourceStock, product.Unit)} of {product.Name} at {from.Name} — can't transfer {request.Quantity:0.####}.");

        sourcePls!.CurrentStock -= request.Quantity;

        var destPls = await _db.ProductLocationStocks.FirstOrDefaultAsync(x => x.ProductId == product.Id && x.LocationId == to.Id);
        if (destPls == null)
        {
            destPls = new ProductLocationStock { BusinessId = businessId, ProductId = product.Id, LocationId = to.Id, CurrentStock = 0m };
            _db.ProductLocationStocks.Add(destPls);
        }
        destPls.CurrentStock += request.Quantity;

        // Product.CurrentStock is intentionally NOT touched — a transfer moves stock, it doesn't change the total.

        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        var transfer = new StockTransfer
        {
            BusinessId = businessId, ProductId = product.Id,
            FromLocationId = from.Id, ToLocationId = to.Id, Quantity = request.Quantity,
            Notes = notes, RecordedByUserId = userId, RecordedByName = userName,
        };
        _db.StockTransfers.Add(transfer);

        var noteSuffix = notes != null ? $" — {notes}" : "";
        _db.InventoryTransactions.Add(new InventoryTransaction
        {
            BusinessId = businessId, ProductId = product.Id, LocationId = from.Id,
            Type = InventoryTransactionType.TransferOut, Quantity = request.Quantity,
            Notes = $"Transfer to {to.Name}{noteSuffix}", RecordedByUserId = userId, RecordedByName = userName,
        });
        _db.InventoryTransactions.Add(new InventoryTransaction
        {
            BusinessId = businessId, ProductId = product.Id, LocationId = to.Id,
            Type = InventoryTransactionType.TransferIn, Quantity = request.Quantity,
            Notes = $"Transfer from {from.Name}{noteSuffix}", RecordedByUserId = userId, RecordedByName = userName,
        });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return new StockTransferDto
        {
            Id = transfer.Id, ProductId = product.Id, ProductName = product.Name, Unit = product.Unit,
            FromLocationId = from.Id, FromLocationName = from.Name,
            ToLocationId = to.Id, ToLocationName = to.Name,
            Quantity = transfer.Quantity, Notes = transfer.Notes,
            RecordedByName = transfer.RecordedByName, CreatedAtUtc = transfer.CreatedAtUtc,
        };
    }

    public async Task<PaginatedResult<StockTransferDto>> GetAllAsync(Guid businessId, int page, int pageSize, Guid? productId = null, Guid? locationId = null)
    {
        var query = _db.StockTransfers.Where(t => t.BusinessId == businessId);
        if (productId.HasValue) query = query.Where(t => t.ProductId == productId.Value);
        if (locationId.HasValue) query = query.Where(t => t.FromLocationId == locationId.Value || t.ToLocationId == locationId.Value);

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(t => t.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        // Batch-resolve names (StockTransfer holds plain Guid FKs, no navigations).
        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.Unit }).ToDictionaryAsync(p => p.Id);
        var locIds = rows.SelectMany(r => new[] { r.FromLocationId, r.ToLocationId }).Distinct().ToList();
        var locNames = await _db.Locations.Where(l => locIds.Contains(l.Id))
            .Select(l => new { l.Id, l.Name }).ToDictionaryAsync(l => l.Id, l => l.Name);

        var items = rows.Select(r => new StockTransferDto
        {
            Id = r.Id, ProductId = r.ProductId,
            ProductName = products.TryGetValue(r.ProductId, out var p) ? p.Name : "(deleted product)",
            Unit = products.TryGetValue(r.ProductId, out var pu) ? pu.Unit : "",
            FromLocationId = r.FromLocationId, FromLocationName = locNames.GetValueOrDefault(r.FromLocationId, "(unknown)"),
            ToLocationId = r.ToLocationId, ToLocationName = locNames.GetValueOrDefault(r.ToLocationId, "(unknown)"),
            Quantity = r.Quantity, Notes = r.Notes, RecordedByName = r.RecordedByName, CreatedAtUtc = r.CreatedAtUtc,
        }).ToList();

        return new PaginatedResult<StockTransferDto> { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
    }
}

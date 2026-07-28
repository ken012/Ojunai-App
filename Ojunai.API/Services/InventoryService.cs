using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Inventory;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services;

public class InventoryService : IInventoryService
{
    private readonly AppDbContext _db;
    private readonly LocationStockService _locStock;

    public InventoryService(AppDbContext db, LocationStockService locStock)
    {
        _db = db;
        _locStock = locStock;
    }

    public async Task<InventoryTransactionDto> StockInAsync(Guid businessId, StockInRequest request, Guid? recordedByUserId = null, string? recordedByName = null, DateTime? createdAtUtc = null)
        => await DbRetry.SerializableAsync(_db, async () =>
    {
        var product = await GetProductAsync(businessId, request.ProductId);

        var txn = new InventoryTransaction
        {
            BusinessId = businessId,
            ProductId = request.ProductId,
            Type = InventoryTransactionType.StockIn,
            Quantity = request.Quantity,
            UnitCost = request.UnitCost,
            Notes = request.Notes,
            RecordedByUserId = recordedByUserId,
            RecordedByName = recordedByName,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };

        product.CurrentStock += request.Quantity;
        if (request.UnitCost.HasValue) product.CostPrice = request.UnitCost;

        // Batch/expiry: record a lot for batch-tracked products (additive; no-op otherwise).
        if (product.TracksBatches)
        {
            _db.ProductBatches.Add(new ProductBatch
            {
                BusinessId = businessId,
                ProductId = product.Id,
                Quantity = request.Quantity,
                ExpiryDate = request.ExpiryDate,
                LotNumber = string.IsNullOrWhiteSpace(request.LotNumber) ? null : request.LotNumber.Trim(),
                CostPrice = request.UnitCost,
                ReceivedAtUtc = createdAtUtc ?? DateTime.UtcNow,
            });
        }

        _db.InventoryTransactions.Add(txn);
        await _db.SaveChangesAsync();
        return ToDto(txn, product.Name, product.Unit);
    });

    public async Task<InventoryTransactionDto> StockOutAsync(Guid businessId, StockOutRequest request, Guid? recordedByUserId = null, string? recordedByName = null)
        => await DbRetry.SerializableAsync(_db, async () =>
    {
        var product = await GetProductAsync(businessId, request.ProductId);

        var outLoc = await _locStock.SelectedLocationForAsync(businessId);
        var outAvailable = outLoc is { } ol ? await _locStock.StockAtAsync(request.ProductId, ol) : product.CurrentStock;
        if (outAvailable < request.Quantity)
            throw new InvalidOperationException($"Insufficient stock{(outLoc != null ? " at this location" : "")}. Available: {outAvailable} {UnitFormat.Plural(outAvailable, product.Unit)}.");

        var txn = new InventoryTransaction
        {
            BusinessId = businessId,
            ProductId = request.ProductId,
            Type = InventoryTransactionType.StockOut,
            Quantity = request.Quantity,
            Notes = request.Notes,
            RecordedByUserId = recordedByUserId,
            RecordedByName = recordedByName
        };

        product.CurrentStock -= request.Quantity;
        _db.InventoryTransactions.Add(txn);
        await _db.SaveChangesAsync();
        return ToDto(txn, product.Name, product.Unit);
    });

    public async Task<InventoryTransactionDto> AdjustAsync(Guid businessId, AdjustmentRequest request, Guid? recordedByUserId = null, string? recordedByName = null)
        => await DbRetry.SerializableAsync(_db, async () =>
    {
        var product = await GetProductAsync(businessId, request.ProductId);
        // Absolute set. For a specific location, "set stock to N" means set THAT location's stock; otherwise
        // it's the whole product (single-location / All locations = unchanged behaviour).
        var adjLoc = await _locStock.SelectedLocationForAsync(businessId);
        var effectiveCurrent = adjLoc is { } al ? await _locStock.StockAtAsync(request.ProductId, al) : product.CurrentStock;
        var diff = request.NewQuantity - effectiveCurrent;

        var txn = new InventoryTransaction
        {
            BusinessId = businessId,
            ProductId = request.ProductId,
            Type = InventoryTransactionType.Adjustment,
            Quantity = Math.Abs(diff),
            Notes = request.Notes ?? $"Adjusted from {effectiveCurrent} to {request.NewQuantity}",
            RecordedByUserId = recordedByUserId,
            RecordedByName = recordedByName
        };

        // Set the business-wide roll-up. For a specific location, move only that location's slice by `diff`
        // (the dual-write mirror then lands PLS(location) = NewQuantity); otherwise set the product total.
        product.CurrentStock = adjLoc != null ? product.CurrentStock + diff : request.NewQuantity;
        _db.InventoryTransactions.Add(txn);
        await _db.SaveChangesAsync();
        return ToDto(txn, product.Name, product.Unit);
    });

    public async Task<InventoryTransactionDto> MarkDamagedAsync(Guid businessId, DamagedRequest request, Guid? recordedByUserId = null, string? recordedByName = null)
        => await DbRetry.SerializableAsync(_db, async () =>
    {
        var product = await GetProductAsync(businessId, request.ProductId);

        var outLoc = await _locStock.SelectedLocationForAsync(businessId);
        var outAvailable = outLoc is { } ol ? await _locStock.StockAtAsync(request.ProductId, ol) : product.CurrentStock;
        if (outAvailable < request.Quantity)
            throw new InvalidOperationException($"Insufficient stock{(outLoc != null ? " at this location" : "")}. Available: {outAvailable} {UnitFormat.Plural(outAvailable, product.Unit)}.");

        var txn = new InventoryTransaction
        {
            BusinessId = businessId,
            ProductId = request.ProductId,
            Type = InventoryTransactionType.Damaged,
            Quantity = request.Quantity,
            Notes = request.Notes,
            RecordedByUserId = recordedByUserId,
            RecordedByName = recordedByName
        };

        product.CurrentStock -= request.Quantity;
        _db.InventoryTransactions.Add(txn);
        await _db.SaveChangesAsync();
        return ToDto(txn, product.Name, product.Unit);
    });

    public async Task<InventoryTransactionDto> MarkWastageAsync(Guid businessId, DamagedRequest request, Guid? recordedByUserId = null, string? recordedByName = null)
        => await DbRetry.SerializableAsync(_db, async () =>
    {
        var product = await GetProductAsync(businessId, request.ProductId);

        var outLoc = await _locStock.SelectedLocationForAsync(businessId);
        var outAvailable = outLoc is { } ol ? await _locStock.StockAtAsync(request.ProductId, ol) : product.CurrentStock;
        if (outAvailable < request.Quantity)
            throw new InvalidOperationException($"Insufficient stock{(outLoc != null ? " at this location" : "")}. Available: {outAvailable} {UnitFormat.Plural(outAvailable, product.Unit)}.");

        var txn = new InventoryTransaction
        {
            BusinessId = businessId,
            ProductId = request.ProductId,
            Type = InventoryTransactionType.Wastage,
            Quantity = request.Quantity,
            Notes = request.Notes,
            RecordedByUserId = recordedByUserId,
            RecordedByName = recordedByName
        };

        product.CurrentStock -= request.Quantity;
        _db.InventoryTransactions.Add(txn);
        await _db.SaveChangesAsync();
        return ToDto(txn, product.Name, product.Unit);
    });

    public async Task<PaginatedResult<InventoryTransactionDto>> GetTransactionsAsync(
        Guid businessId, Guid? productId, int page, int pageSize)
    {
        var query = _db.InventoryTransactions
            .Include(t => t.Product)
            .Where(t => t.BusinessId == businessId);

        // When a location is selected (multi-location), show only that location's stock movements. Pre-existing
        // movements have a null LocationId and surface only under "All locations".
        if (await _locStock.SelectedLocationForAsync(businessId) is { } locId)
            query = query.Where(t => t.LocationId == locId);

        if (productId.HasValue)
            query = query.Where(t => t.ProductId == productId.Value);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(t => t.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => ToDto(t, t.Product.Name, t.Product.Unit))
            .ToListAsync();

        return new PaginatedResult<InventoryTransactionDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    private async Task<Product> GetProductAsync(Guid businessId, Guid productId)
    {
        return await _db.Products
            .FirstOrDefaultAsync(p => p.Id == productId && p.BusinessId == businessId && p.IsActive)
            ?? throw new KeyNotFoundException("Product not found.");
    }

    private static InventoryTransactionDto ToDto(InventoryTransaction t, string productName, string unit) => new()
    {
        Id = t.Id,
        ProductId = t.ProductId,
        ProductName = productName,
        Type = t.Type.ToString(),
        Quantity = t.Quantity,
        UnitCost = t.UnitCost,
        Notes = t.Notes,
        CreatedAtUtc = t.CreatedAtUtc
    };
}

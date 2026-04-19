using BizPilot.API.Common;
using BizPilot.API.Data;
using BizPilot.API.DTOs.Inventory;
using BizPilot.API.Models;
using BizPilot.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BizPilot.API.Services;

public class InventoryService : IInventoryService
{
    private readonly AppDbContext _db;

    public InventoryService(AppDbContext db) => _db = db;

    public async Task<InventoryTransactionDto> StockInAsync(Guid businessId, StockInRequest request, Guid? recordedByUserId = null, string? recordedByName = null)
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
            RecordedByName = recordedByName
        };

        product.CurrentStock += request.Quantity;
        if (request.UnitCost.HasValue) product.CostPrice = request.UnitCost;

        _db.InventoryTransactions.Add(txn);
        await _db.SaveChangesAsync();
        return ToDto(txn, product.Name, product.Unit);
    }

    public async Task<InventoryTransactionDto> StockOutAsync(Guid businessId, StockOutRequest request, Guid? recordedByUserId = null, string? recordedByName = null)
    {
        var product = await GetProductAsync(businessId, request.ProductId);

        if (product.CurrentStock < request.Quantity)
            throw new InvalidOperationException($"Insufficient stock. Available: {product.CurrentStock} {product.Unit}.");

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
    }

    public async Task<InventoryTransactionDto> AdjustAsync(Guid businessId, AdjustmentRequest request, Guid? recordedByUserId = null, string? recordedByName = null)
    {
        var product = await GetProductAsync(businessId, request.ProductId);
        var diff = request.NewQuantity - product.CurrentStock;

        var txn = new InventoryTransaction
        {
            BusinessId = businessId,
            ProductId = request.ProductId,
            Type = InventoryTransactionType.Adjustment,
            Quantity = Math.Abs(diff),
            Notes = request.Notes ?? $"Adjusted from {product.CurrentStock} to {request.NewQuantity}",
            RecordedByUserId = recordedByUserId,
            RecordedByName = recordedByName
        };

        product.CurrentStock = request.NewQuantity;
        _db.InventoryTransactions.Add(txn);
        await _db.SaveChangesAsync();
        return ToDto(txn, product.Name, product.Unit);
    }

    public async Task<InventoryTransactionDto> MarkDamagedAsync(Guid businessId, DamagedRequest request, Guid? recordedByUserId = null, string? recordedByName = null)
    {
        var product = await GetProductAsync(businessId, request.ProductId);

        if (product.CurrentStock < request.Quantity)
            throw new InvalidOperationException($"Insufficient stock. Available: {product.CurrentStock} {product.Unit}.");

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
    }

    public async Task<InventoryTransactionDto> MarkWastageAsync(Guid businessId, DamagedRequest request, Guid? recordedByUserId = null, string? recordedByName = null)
    {
        var product = await GetProductAsync(businessId, request.ProductId);

        if (product.CurrentStock < request.Quantity)
            throw new InvalidOperationException($"Insufficient stock. Available: {product.CurrentStock} {product.Unit}.");

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
    }

    public async Task<PaginatedResult<InventoryTransactionDto>> GetTransactionsAsync(
        Guid businessId, Guid? productId, int page, int pageSize)
    {
        var query = _db.InventoryTransactions
            .Include(t => t.Product)
            .Where(t => t.BusinessId == businessId);

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

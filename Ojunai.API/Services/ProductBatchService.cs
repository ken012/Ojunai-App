using Ojunai.API.Data;
using Ojunai.API.DTOs.Inventory;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services;

/// <summary>
/// Batch/expiry lot register. Lots are created on stock-in (see InventoryService) for batch-tracked
/// products. This service lists them, surfaces expiring/expired ones, and writes them off (which
/// records a wastage that reduces stock via the existing InventoryService path). Additive — no
/// existing flow depends on it.
/// </summary>
public class ProductBatchService : IProductBatchService
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IActivityLogger _activity;

    public ProductBatchService(AppDbContext db, IInventoryService inventory, IActivityLogger activity)
    {
        _db = db;
        _inventory = inventory;
        _activity = activity;
    }

    public async Task<List<ProductBatchDto>> ListAsync(Guid businessId, Guid productId)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId && p.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Product not found.");
        var batches = await _db.ProductBatches
            .Where(b => b.BusinessId == businessId && b.ProductId == productId && b.WrittenOffAtUtc == null && b.Quantity > 0)
            .OrderBy(b => b.ExpiryDate == null).ThenBy(b => b.ExpiryDate)
            .ToListAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return batches.Select(b => ToDto(b, product.Name, product.Unit, today)).ToList();
    }

    public async Task<List<ProductBatchDto>> WriteOffAsync(Guid businessId, Guid productId, Guid batchId, WriteOffBatchRequest request, Guid? userId, string? userName)
    {
        var batch = await _db.ProductBatches
            .FirstOrDefaultAsync(b => b.Id == batchId && b.ProductId == productId && b.BusinessId == businessId && b.WrittenOffAtUtc == null)
            ?? throw new KeyNotFoundException("Batch not found.");
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId && p.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Product not found.");

        var requested = request.Quantity is > 0 ? request.Quantity!.Value : batch.Quantity;
        var amount = Math.Min(requested, batch.Quantity);

        // Record the discard as a wastage so it hits the loss reports + reduces stock — but never try to
        // remove more than is actually in stock (guards against drift on fast-movers in this V1 register).
        var wastable = Math.Min(amount, product.CurrentStock);
        if (wastable > 0)
        {
            await _inventory.MarkWastageAsync(businessId, new DamagedRequest
            {
                ProductId = productId,
                Quantity = wastable,
                Notes = $"Expired/discarded lot{(string.IsNullOrEmpty(batch.LotNumber) ? "" : $" {batch.LotNumber}")}"
                        + (batch.ExpiryDate.HasValue ? $" (exp {batch.ExpiryDate:yyyy-MM-dd})" : ""),
            }, userId, userName);
        }

        batch.Quantity -= amount;
        if (batch.Quantity <= 0) batch.WrittenOffAtUtc = DateTime.UtcNow;
        await _activity.LogAsync(businessId, "batch.written_off", "ProductBatch", batchId, product.Name,
            $"wrote off {amount:0.##} of \"{product.Name}\" (expired/damaged)");
        await _db.SaveChangesAsync();

        return await ListAsync(businessId, productId);
    }

    public async Task<List<ProductBatchDto>> ExpiringAsync(Guid businessId, int days)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cutoff = today.AddDays(Math.Clamp(days, 0, 3650));

        var rows = await _db.ProductBatches
            .Where(b => b.BusinessId == businessId && b.WrittenOffAtUtc == null && b.Quantity > 0
                        && b.ExpiryDate != null && b.ExpiryDate <= cutoff)
            .Join(_db.Products, b => b.ProductId, p => p.Id, (b, p) => new { b, p.Name, p.Unit, p.IsActive })
            .Where(x => x.IsActive)
            .OrderBy(x => x.b.ExpiryDate)
            .ToListAsync();

        return rows.Select(x => ToDto(x.b, x.Name, x.Unit, today)).ToList();
    }

    private static ProductBatchDto ToDto(ProductBatch b, string productName, string unit, DateOnly today) => new()
    {
        Id = b.Id,
        ProductId = b.ProductId,
        ProductName = productName,
        Unit = unit,
        Quantity = b.Quantity,
        ExpiryDate = b.ExpiryDate,
        LotNumber = b.LotNumber,
        DaysToExpiry = b.ExpiryDate.HasValue ? b.ExpiryDate.Value.DayNumber - today.DayNumber : null,
        IsExpired = b.ExpiryDate.HasValue && b.ExpiryDate.Value < today,
        ReceivedAtUtc = b.ReceivedAtUtc,
    };
}

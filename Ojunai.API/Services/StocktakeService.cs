using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Stocktaking;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services;

/// <summary>
/// Physical stock counts. Create snapshots current system stock per product; the user enters counted
/// quantities; Complete reconciles each counted product to its count via an Adjustment inventory
/// transaction (identical mechanic to <see cref="InventoryService.AdjustAsync"/>, just batched and
/// transactional). Nothing mutates stock until Complete.
/// </summary>
public class StocktakeService : IStocktakeService
{
    private readonly AppDbContext _db;

    public StocktakeService(AppDbContext db) => _db = db;

    public async Task<StocktakeDto> CreateAsync(Guid businessId, CreateStocktakeRequest request, Guid? userId, string? userName)
    {
        var category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();

        var products = await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive && (category == null || p.Category == category))
            .OrderBy(p => p.Name)
            .ToListAsync();
        if (products.Count == 0)
            throw new InvalidOperationException(category == null
                ? "No active products to count."
                : $"No active products in category '{category}'.");

        var count = await _db.Stocktakes.CountAsync(s => s.BusinessId == businessId);

        var stocktake = new Stocktake
        {
            BusinessId = businessId,
            Reference = $"SC-{count + 1:D4}",
            Status = StocktakeStatus.Draft,
            Scope = category == null ? "All products" : $"Category: {category}",
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            RecordedByUserId = userId,
            RecordedByName = userName,
            Items = products.Select(p => new StocktakeItem
            {
                ProductId = p.Id,
                ProductName = p.Name,
                Unit = p.Unit,
                SystemQuantity = p.CurrentStock,
                CountedQuantity = null,
                UnitCost = p.CostPrice ?? 0m,
            }).ToList(),
        };

        _db.Stocktakes.Add(stocktake);
        await _db.SaveChangesAsync();
        return ToDto(stocktake);
    }

    public async Task<PaginatedResult<StocktakeDto>> ListAsync(Guid businessId, string? status, int page, int pageSize)
    {
        var query = _db.Stocktakes.Include(s => s.Items).Where(s => s.BusinessId == businessId);

        if (!string.IsNullOrWhiteSpace(status) && status != "all"
            && Enum.TryParse<StocktakeStatus>(status, ignoreCase: true, out var st))
            query = query.Where(s => s.Status == st);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(s => s.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PaginatedResult<StocktakeDto>
        {
            Items = items.Select(ToDto).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<StocktakeDto> GetByIdAsync(Guid businessId, Guid id)
        => ToDto(await LoadAsync(businessId, id));

    public async Task<StocktakeDto> SaveCountsAsync(Guid businessId, Guid id, SaveCountsRequest request)
    {
        var st = await LoadAsync(businessId, id);
        if (st.Status != StocktakeStatus.Draft)
            throw new InvalidOperationException("This stock count is already finished — start a new one.");

        var byId = st.Items.ToDictionary(i => i.Id);
        foreach (var c in request.Counts ?? new List<CountInput>())
        {
            if (byId.TryGetValue(c.ItemId, out var item))
                item.CountedQuantity = c.CountedQuantity.HasValue ? Math.Max(0, c.CountedQuantity.Value) : null;
        }

        await _db.SaveChangesAsync();
        return ToDto(st);
    }

    public async Task<StocktakeDto> CompleteAsync(Guid businessId, Guid id, Guid? userId, string? userName)
    {
        var st = await LoadAsync(businessId, id);
        if (st.Status != StocktakeStatus.Draft)
            throw new InvalidOperationException($"This stock count is already {st.Status.ToString().ToLowerInvariant()}.");

        var counted = st.Items.Where(i => i.CountedQuantity.HasValue).ToList();
        if (counted.Count == 0)
            throw new InvalidOperationException("Enter at least one counted quantity before completing.");

        var now = DateTime.UtcNow;

        // One transaction: all adjustments + status commit together, or none do.
        await using var tx = await _db.Database.BeginTransactionAsync();

        foreach (var item in counted)
        {
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId && p.BusinessId == businessId);
            if (product == null) continue; // product deleted since snapshot — skip, don't fail the batch

            var target = item.CountedQuantity!.Value;
            // Diff against CURRENT stock at commit time (not the snapshot) so interim sales/receipts
            // are accounted for and the transaction log is accurate. Matches AdjustAsync semantics.
            var diff = target - product.CurrentStock;
            if (diff == 0) continue;

            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                BusinessId = businessId,
                ProductId = product.Id,
                Type = InventoryTransactionType.Adjustment,
                Quantity = Math.Abs(diff),
                UnitCost = item.UnitCost > 0 ? item.UnitCost : null,
                Notes = $"Stock count {st.Reference}: {product.CurrentStock:0.##} → {target:0.##}",
                RecordedByUserId = userId,
                RecordedByName = userName,
                CreatedAtUtc = now,
            });
            product.CurrentStock = target;
        }

        st.Status = StocktakeStatus.Completed;
        st.CompletedAtUtc = now;

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return ToDto(st);
    }

    public async Task<StocktakeDto> CancelAsync(Guid businessId, Guid id)
    {
        var st = await LoadAsync(businessId, id);
        if (st.Status == StocktakeStatus.Completed)
            throw new InvalidOperationException("A completed stock count can't be cancelled.");
        if (st.Status == StocktakeStatus.Cancelled)
            return ToDto(st);
        st.Status = StocktakeStatus.Cancelled;
        st.CancelledAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(st);
    }

    // ── helpers ────────────────────────────────────────────────────────────────
    private async Task<Stocktake> LoadAsync(Guid businessId, Guid id)
        => await _db.Stocktakes.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == id && s.BusinessId == businessId)
           ?? throw new KeyNotFoundException("Stock count not found.");

    private static StocktakeDto ToDto(Stocktake s)
    {
        var items = s.Items
            .OrderBy(i => i.ProductName)
            .Select(i =>
            {
                decimal? variance = i.CountedQuantity.HasValue ? i.CountedQuantity.Value - i.SystemQuantity : null;
                return new StocktakeItemDto
                {
                    Id = i.Id,
                    ProductId = i.ProductId,
                    ProductName = i.ProductName,
                    Unit = i.Unit,
                    SystemQuantity = i.SystemQuantity,
                    CountedQuantity = i.CountedQuantity,
                    UnitCost = i.UnitCost,
                    Variance = variance,
                    VarianceValue = variance.HasValue ? variance.Value * i.UnitCost : null,
                };
            })
            .ToList();

        return new StocktakeDto
        {
            Id = s.Id,
            Reference = s.Reference,
            Status = s.Status.ToString(),
            Scope = s.Scope,
            Notes = s.Notes,
            RecordedByName = s.RecordedByName,
            TotalItems = items.Count,
            CountedItems = items.Count(i => i.CountedQuantity.HasValue),
            VarianceItems = items.Count(i => i.Variance.HasValue && i.Variance.Value != 0),
            NetVarianceValue = items.Where(i => i.VarianceValue.HasValue).Sum(i => i.VarianceValue!.Value),
            CreatedAtUtc = s.CreatedAtUtc,
            CompletedAtUtc = s.CompletedAtUtc,
            CancelledAtUtc = s.CancelledAtUtc,
            Items = items,
        };
    }
}

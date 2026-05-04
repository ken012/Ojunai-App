using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Products;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services;

public class ProductService : IProductService
{
    private readonly AppDbContext _db;

    public ProductService(AppDbContext db) => _db = db;

    public async Task<PaginatedResult<ProductDto>> GetAllAsync(Guid businessId, int page, int pageSize, string? search)
    {
        var query = _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(p => EF.Functions.ILike(p.Name, pattern) || (p.SKU != null && EF.Functions.ILike(p.SKU, pattern)));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => ToDto(p))
            .ToListAsync();

        return new PaginatedResult<ProductDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<ProductDto> GetByIdAsync(Guid businessId, Guid productId)
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == productId && p.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Product not found.");
        return ToDto(product);
    }

    public async Task<ProductDto> CreateAsync(Guid businessId, CreateProductRequest request, Guid? recordedByUserId = null, string? recordedByName = null, DateTime? createdAtUtc = null)
    {
        var exists = await _db.Products.AnyAsync(p =>
            p.BusinessId == businessId && p.Name == request.Name && p.IsActive);
        if (exists)
            throw new InvalidOperationException($"Product '{request.Name}' already exists.");

        // Auto-infer unit if left as default
        var unit = request.Unit;
        if (string.IsNullOrWhiteSpace(unit) || unit == "unit" || unit == "bag")
            unit = Common.UnitInferrer.Infer(request.Name);

        // Auto-infer category if not provided
        var category = request.Category;
        var subcategory = request.Subcategory;
        if (string.IsNullOrWhiteSpace(category))
        {
            var (inferredCat, inferredSub) = Common.CategoryInferrer.Infer(request.Name);
            category = inferredCat;
            subcategory = subcategory ?? inferredSub;
        }

        var effectiveDate = createdAtUtc ?? DateTime.UtcNow;

        var product = new Product
        {
            BusinessId = businessId,
            Name = request.Name,
            SKU = request.SKU,
            Unit = unit,
            CostPrice = request.CostPrice,
            SellingPrice = request.SellingPrice,
            CurrentStock = request.InitialStock,
            LowStockThreshold = request.LowStockThreshold,
            Category = category,
            Subcategory = subcategory,
            RecordedByUserId = recordedByUserId,
            RecordedByName = recordedByName,
            CreatedAtUtc = effectiveDate
        };
        _db.Products.Add(product);

        if (request.InitialStock > 0)
        {
            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                BusinessId = businessId,
                ProductId = product.Id,
                Type = InventoryTransactionType.StockIn,
                Quantity = request.InitialStock,
                Notes = "Initial stock",
                RecordedByUserId = recordedByUserId,
                RecordedByName = recordedByName,
                CreatedAtUtc = effectiveDate
            });
        }

        await _db.SaveChangesAsync();
        return ToDto(product);
    }

    public async Task<ProductDto> UpdateAsync(Guid businessId, Guid productId, UpdateProductRequest request, Guid? recordedByUserId = null, string? recordedByName = null)
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == productId && p.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Product not found.");

        if (request.Name != null) product.Name = request.Name;
        if (request.Unit != null) product.Unit = request.Unit;
        if (request.CostPrice.HasValue) product.CostPrice = request.CostPrice;
        if (request.SellingPrice.HasValue) product.SellingPrice = request.SellingPrice;
        if (request.LowStockThreshold.HasValue) product.LowStockThreshold = request.LowStockThreshold.Value;
        if (request.Category != null) product.Category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();
        if (request.Subcategory != null) product.Subcategory = string.IsNullOrWhiteSpace(request.Subcategory) ? null : request.Subcategory.Trim();
        if (request.IsActive.HasValue) product.IsActive = request.IsActive.Value;
        if (request.Aliases != null) product.Aliases = request.Aliases.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(request.Aliases) : null;
        if (request.VoiceDescription != null) product.VoiceDescription = string.IsNullOrWhiteSpace(request.VoiceDescription) ? null : request.VoiceDescription.Trim();
        if (recordedByUserId.HasValue) { product.RecordedByUserId = recordedByUserId; product.RecordedByName = recordedByName; }

        await _db.SaveChangesAsync();
        return ToDto(product);
    }

    public async Task<ProductDto> UpdatePriceAsync(Guid businessId, Guid productId, UpdatePriceRequest request)
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == productId && p.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Product not found.");

        if (request.SellingPrice.HasValue) product.SellingPrice = request.SellingPrice;
        if (request.CostPrice.HasValue) product.CostPrice = request.CostPrice;

        await _db.SaveChangesAsync();
        return ToDto(product);
    }

    public async Task DeleteAsync(Guid businessId, Guid productId)
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == productId && p.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Product not found.");

        product.IsActive = false;
        await _db.SaveChangesAsync();
    }

    public async Task<List<ProductDto>> GetLowStockAsync(Guid businessId)
    {
        return await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive && p.CurrentStock <= p.LowStockThreshold)
            .OrderBy(p => p.CurrentStock)
            .Select(p => ToDto(p))
            .ToListAsync();
    }

    private static ProductDto ToDto(Product p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        SKU = p.SKU,
        Unit = p.Unit,
        CostPrice = p.CostPrice,
        SellingPrice = p.SellingPrice,
        CurrentStock = p.CurrentStock,
        LowStockThreshold = p.LowStockThreshold,
        IsLowStock = p.CurrentStock <= p.LowStockThreshold,
        IsActive = p.IsActive,
        Category = p.Category,
        Subcategory = p.Subcategory,
        Source = p.Source,
        RecordedByName = p.RecordedByName,
        Aliases = string.IsNullOrEmpty(p.Aliases) ? null : System.Text.Json.JsonSerializer.Deserialize<List<string>>(p.Aliases),
        VoiceDescription = p.VoiceDescription,
        CreatedAtUtc = p.CreatedAtUtc
    };
}

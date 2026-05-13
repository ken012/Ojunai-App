using Ojunai.API.Common;
using Ojunai.API.DTOs.Products;

namespace Ojunai.API.Services.Interfaces;

public interface IProductService
{
    Task<PaginatedResult<ProductDto>> GetAllAsync(
        Guid businessId, int page, int pageSize,
        string? search, string? category = null, string? stockLevel = null);
    Task<ProductDto> GetByIdAsync(Guid businessId, Guid productId);
    Task<ProductDto> CreateAsync(Guid businessId, CreateProductRequest request, Guid? recordedByUserId = null, string? recordedByName = null, DateTime? createdAtUtc = null);
    Task<ProductDto> UpdateAsync(Guid businessId, Guid productId, UpdateProductRequest request, Guid? recordedByUserId = null, string? recordedByName = null);
    Task<ProductDto> UpdatePriceAsync(Guid businessId, Guid productId, UpdatePriceRequest request);
    Task DeleteAsync(Guid businessId, Guid productId);
    Task<List<ProductDto>> GetLowStockAsync(Guid businessId);

    /// <summary>
    /// Per-stock-level counts honoring the same search + category filters as <see cref="GetAllAsync"/>.
    /// Drives the Inventory page's "All / In stock / Low / Out" filter chips without forcing the
    /// client to load every product.
    /// </summary>
    Task<ProductStockStatsDto> GetStockStatsAsync(Guid businessId, string? search, string? category);
}

public class ProductStockStatsDto
{
    public int Total { get; set; }
    public int OutOfStock { get; set; }
    public int Low { get; set; }
    public int Sufficient { get; set; }
}

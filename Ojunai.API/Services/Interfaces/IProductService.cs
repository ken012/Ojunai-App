using Ojunai.API.Common;
using Ojunai.API.DTOs.Products;

namespace Ojunai.API.Services.Interfaces;

public interface IProductService
{
    Task<PaginatedResult<ProductDto>> GetAllAsync(
        Guid businessId, int page, int pageSize,
        string? search, string? category = null, string? stockLevel = null, bool excludeVariants = false);
    Task<ProductDto> GetByIdAsync(Guid businessId, Guid productId);
    /// <summary>Scan-to-lookup: the active product with this barcode, or null. Most recent wins if shared.</summary>
    Task<ProductDto?> GetByBarcodeAsync(Guid businessId, string barcode);
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

    /// <summary>Get a product's bundle definition (its components), if any.</summary>
    Task<BundleDto> GetBundleAsync(Guid businessId, Guid productId);
    /// <summary>Set (or clear) a product's bundle components. Marks IsBundle accordingly.</summary>
    Task<BundleDto> SetBundleAsync(Guid businessId, Guid productId, SetBundleRequest request);
}

public class ProductStockStatsDto
{
    public int Total { get; set; }
    public int OutOfStock { get; set; }
    public int Low { get; set; }
    public int Sufficient { get; set; }
}

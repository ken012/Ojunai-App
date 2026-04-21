using BizPilot.API.Common;
using BizPilot.API.DTOs.Products;

namespace BizPilot.API.Services.Interfaces;

public interface IProductService
{
    Task<PaginatedResult<ProductDto>> GetAllAsync(Guid businessId, int page, int pageSize, string? search);
    Task<ProductDto> GetByIdAsync(Guid businessId, Guid productId);
    Task<ProductDto> CreateAsync(Guid businessId, CreateProductRequest request, Guid? recordedByUserId = null, string? recordedByName = null, DateTime? createdAtUtc = null);
    Task<ProductDto> UpdateAsync(Guid businessId, Guid productId, UpdateProductRequest request, Guid? recordedByUserId = null, string? recordedByName = null);
    Task<ProductDto> UpdatePriceAsync(Guid businessId, Guid productId, UpdatePriceRequest request);
    Task DeleteAsync(Guid businessId, Guid productId);
    Task<List<ProductDto>> GetLowStockAsync(Guid businessId);
}

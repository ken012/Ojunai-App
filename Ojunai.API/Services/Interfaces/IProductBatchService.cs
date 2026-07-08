using Ojunai.API.DTOs.Inventory;

namespace Ojunai.API.Services.Interfaces;

public interface IProductBatchService
{
    /// <summary>Active (not written-off) lots for a product, soonest expiry first.</summary>
    Task<List<ProductBatchDto>> ListAsync(Guid businessId, Guid productId);

    /// <summary>Write off (discard) some/all of a lot — records a wastage that reduces stock.</summary>
    Task<List<ProductBatchDto>> WriteOffAsync(Guid businessId, Guid productId, Guid batchId, WriteOffBatchRequest request, Guid? userId, string? userName);

    /// <summary>Lots expiring within <paramref name="days"/> (includes already-expired), across all products.</summary>
    Task<List<ProductBatchDto>> ExpiringAsync(Guid businessId, int days);
}

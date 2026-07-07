using Ojunai.API.Common;
using Ojunai.API.DTOs.Purchasing;

namespace Ojunai.API.Services.Interfaces;

public interface IPurchaseOrderService
{
    Task<PurchaseOrderDto> CreateAsync(Guid businessId, CreatePurchaseOrderRequest request, Guid? userId, string? userName);
    Task<PaginatedResult<PurchaseOrderDto>> ListAsync(Guid businessId, string? status, int page, int pageSize);
    Task<PurchaseOrderDto> GetByIdAsync(Guid businessId, Guid id);
    Task<PurchaseOrderDto> UpdateAsync(Guid businessId, Guid id, UpdatePurchaseOrderRequest request);
    Task<PurchaseOrderDto> MarkSentAsync(Guid businessId, Guid id);
    Task<PurchaseOrderDto> ReceiveAsync(Guid businessId, Guid id, ReceivePurchaseOrderRequest request, Guid? userId, string? userName);
    Task<PurchaseOrderDto> CancelAsync(Guid businessId, Guid id);
}

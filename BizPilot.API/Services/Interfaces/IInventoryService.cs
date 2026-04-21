using BizPilot.API.Common;
using BizPilot.API.DTOs.Inventory;

namespace BizPilot.API.Services.Interfaces;

public interface IInventoryService
{
    Task<InventoryTransactionDto> StockInAsync(Guid businessId, StockInRequest request, Guid? recordedByUserId = null, string? recordedByName = null, DateTime? createdAtUtc = null);
    Task<InventoryTransactionDto> StockOutAsync(Guid businessId, StockOutRequest request, Guid? recordedByUserId = null, string? recordedByName = null);
    Task<InventoryTransactionDto> AdjustAsync(Guid businessId, AdjustmentRequest request, Guid? recordedByUserId = null, string? recordedByName = null);
    Task<InventoryTransactionDto> MarkDamagedAsync(Guid businessId, DamagedRequest request, Guid? recordedByUserId = null, string? recordedByName = null);
    Task<InventoryTransactionDto> MarkWastageAsync(Guid businessId, DamagedRequest request, Guid? recordedByUserId = null, string? recordedByName = null);
    Task<PaginatedResult<InventoryTransactionDto>> GetTransactionsAsync(Guid businessId, Guid? productId, int page, int pageSize);
}

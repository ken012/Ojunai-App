using BizPilot.API.DTOs.Inventory;

namespace BizPilot.API.Services.Interfaces;

public interface IStockHoldService
{
    Task<StockHoldDto> CreateHoldAsync(Guid businessId, Guid productId, string contactName, decimal quantity, string? notes = null, string source = "Manual");
    Task<StockHoldDto> ReleaseHoldAsync(Guid businessId, Guid holdId);
    Task<DTOs.Sales.SaleDto> ConvertToSaleAsync(Guid businessId, Guid holdId);
    Task<List<StockHoldDto>> GetActiveHoldsAsync(Guid businessId);
    Task<List<StockHoldDto>> GetAllHoldsAsync(Guid businessId, string? status = null);
    Task<decimal> GetHeldQuantityAsync(Guid businessId, Guid productId);
}

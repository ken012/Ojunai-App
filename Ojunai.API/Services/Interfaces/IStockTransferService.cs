using Ojunai.API.Common;
using Ojunai.API.DTOs.Inventory;

namespace Ojunai.API.Services.Interfaces;

public interface IStockTransferService
{
    /// <summary>Move <paramref name="request"/>.Quantity of a product from one location to another (instant).
    /// Decrements the source ProductLocationStock, increments the destination's, leaves Product.CurrentStock
    /// unchanged, and records the transfer + a TransferOut/TransferIn movement row. Throws for a disabled
    /// business, invalid/inactive/same locations, a bundle, or insufficient source stock.</summary>
    Task<StockTransferDto> TransferAsync(Guid businessId, CreateStockTransferRequest request, Guid? userId = null, string? userName = null);

    /// <summary>The transfer history, newest first. Optional product / location (from OR to) filters.</summary>
    Task<PaginatedResult<StockTransferDto>> GetAllAsync(Guid businessId, int page, int pageSize, Guid? productId = null, Guid? locationId = null);
}

using BizPilot.API.Common;
using BizPilot.API.DTOs.Sales;

namespace BizPilot.API.Services.Interfaces;

public interface ISalesService
{
    Task<SaleDto> CreateAsync(Guid businessId, CreateSaleRequest request, string source = "Manual", Guid? recordedByUserId = null, string? recordedByName = null);
    Task<PaginatedResult<SaleSummaryDto>> GetAllAsync(Guid businessId, int page, int pageSize, DateTime? from, DateTime? to);
    Task<SaleDto> GetByIdAsync(Guid businessId, Guid saleId);
    Task VoidAsync(Guid businessId, Guid saleId, Guid? voidedByUserId = null, string? voidedByName = null);
    Task<PaginatedResult<SaleSummaryDto>> GetVoidedAsync(Guid businessId, int page, int pageSize);
}

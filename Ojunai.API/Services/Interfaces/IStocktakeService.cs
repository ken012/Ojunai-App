using Ojunai.API.Common;
using Ojunai.API.DTOs.Stocktaking;

namespace Ojunai.API.Services.Interfaces;

public interface IStocktakeService
{
    Task<StocktakeDto> CreateAsync(Guid businessId, CreateStocktakeRequest request, Guid? userId, string? userName);
    Task<PaginatedResult<StocktakeDto>> ListAsync(Guid businessId, string? status, int page, int pageSize);
    Task<StocktakeDto> GetByIdAsync(Guid businessId, Guid id);
    Task<StocktakeDto> SaveCountsAsync(Guid businessId, Guid id, SaveCountsRequest request);
    Task<StocktakeDto> CompleteAsync(Guid businessId, Guid id, Guid? userId, string? userName);
    Task<StocktakeDto> CancelAsync(Guid businessId, Guid id);
}

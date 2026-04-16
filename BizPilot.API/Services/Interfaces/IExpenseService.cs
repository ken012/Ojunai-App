using BizPilot.API.Common;
using BizPilot.API.DTOs.Expenses;

namespace BizPilot.API.Services.Interfaces;

public interface IExpenseService
{
    Task<ExpenseDto> CreateAsync(Guid businessId, CreateExpenseRequest request, string source = "Manual", Guid? recordedByUserId = null, string? recordedByName = null);
    Task<PaginatedResult<ExpenseDto>> GetAllAsync(Guid businessId, int page, int pageSize, DateTime? from, DateTime? to);
    Task<ExpenseDto> UpdateAsync(Guid businessId, Guid expenseId, UpdateExpenseRequest request);
    Task DeleteAsync(Guid businessId, Guid expenseId);
}

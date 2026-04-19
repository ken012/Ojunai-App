using BizPilot.API.Common;
using BizPilot.API.DTOs.Expenses;
using BizPilot.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BizPilot.API.Controllers;

[Route("api/expenses")]
public class ExpensesController : BizPilotBaseController
{
    private readonly IExpenseService _expenses;
    private readonly Data.AppDbContext _db;

    public ExpensesController(IExpenseService expenses, Data.AppDbContext db) { _expenses = expenses; _db = db; }

    [HttpPost]
    [RequirePermission(Permission.RecordExpenses)]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> Create([FromBody] CreateExpenseRequest request)
    {
        var user = await _db.Users.FindAsync(UserId);
        var result = await _expenses.CreateAsync(BusinessId, request, "Manual", user?.Id, user?.FullName);
        return Ok(ApiResponse<ExpenseDto>.Ok(result, "Expense recorded."));
    }

    [HttpGet]
    [RequirePermission(Permission.ViewOwnReports)]
    public async Task<ActionResult<ApiResponse<PaginatedResult<ExpenseDto>>>> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null,
        [FromQuery] string? expenseType = null)
    {
        var result = await _expenses.GetAllAsync(BusinessId, page, pageSize, from, to, expenseType);
        return Ok(ApiResponse<PaginatedResult<ExpenseDto>>.Ok(result));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permission.RecordExpenses)]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> Update(Guid id, [FromBody] UpdateExpenseRequest request)
    {
        var result = await _expenses.UpdateAsync(BusinessId, id, request);
        return Ok(ApiResponse<ExpenseDto>.Ok(result, "Expense updated."));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permission.RecordExpenses)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id)
    {
        await _expenses.DeleteAsync(BusinessId, id);
        return Ok(ApiResponse<object>.Ok(null!, "Expense deleted."));
    }
}

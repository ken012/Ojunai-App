using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Expenses;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services;

public class ExpenseService : IExpenseService
{
    private readonly AppDbContext _db;

    public ExpenseService(AppDbContext db) => _db = db;

    public async Task<ExpenseDto> CreateAsync(Guid businessId, CreateExpenseRequest request, string source = "Manual", Guid? recordedByUserId = null, string? recordedByName = null)
    {
        var expense = new Expense
        {
            BusinessId = businessId,
            Category = request.Category,
            ExpenseType = request.ExpenseType ?? "operating",
            Amount = request.Amount,
            Notes = request.Notes,
            PaidTo = request.PaidTo,
            PaymentMethod = request.PaymentMethod,
            Source = source,
            RecordedByUserId = recordedByUserId,
            RecordedByName = recordedByName,
            CreatedAtUtc = request.ExpenseDate ?? DateTime.UtcNow
        };
        _db.Expenses.Add(expense);
        await _db.SaveChangesAsync();
        return ToDto(expense);
    }

    public async Task<PaginatedResult<ExpenseDto>> GetAllAsync(
        Guid businessId, int page, int pageSize, DateTime? from, DateTime? to, string? expenseType = null, string? category = null, string? paymentMethod = null, string? source = null, string? search = null)
    {
        var query = _db.Expenses.Where(e => e.BusinessId == businessId);

        if (from.HasValue) query = query.Where(e => e.CreatedAtUtc >= from.Value);
        if (to.HasValue) query = query.Where(e => e.CreatedAtUtc <= to.Value);
        if (!string.IsNullOrEmpty(category))
            query = query.Where(e => EF.Functions.ILike(e.Category, category));
        if (!string.IsNullOrEmpty(paymentMethod))
            query = query.Where(e => e.PaymentMethod != null && EF.Functions.ILike(e.PaymentMethod, paymentMethod));
        if (!string.IsNullOrEmpty(source))
            query = query.Where(e => EF.Functions.ILike(e.Source, source));
        if (!string.IsNullOrEmpty(search))
        {
            var startPattern = $"{search}%";
            var wordPattern = $"% {search}%";
            query = query.Where(e =>
                EF.Functions.ILike(e.Category, startPattern) || EF.Functions.ILike(e.Category, wordPattern)
                || (e.PaidTo != null && (EF.Functions.ILike(e.PaidTo, startPattern) || EF.Functions.ILike(e.PaidTo, wordPattern)))
                || (e.Notes != null && (EF.Functions.ILike(e.Notes, startPattern) || EF.Functions.ILike(e.Notes, wordPattern))));
        }
        if (expenseType == "cogs")
        {
            // Inventory Expenses: explicitly tagged as cogs OR category matches inventory keywords
            query = query.Where(e => e.ExpenseType == "cogs"
                || EF.Functions.ILike(e.Category, "inventory%")
                || EF.Functions.ILike(e.Category, "%stock%")
                || EF.Functions.ILike(e.Category, "%goods for%")
                || EF.Functions.ILike(e.Category, "raw material%")
                || EF.Functions.ILike(e.Category, "%merchandise%")
                || EF.Functions.ILike(e.Category, "%replenish%")
                || EF.Functions.ILike(e.Category, "%restock%"));
        }
        else if (expenseType == "operating")
        {
            query = query.Where(e => e.ExpenseType != "cogs"
                && !EF.Functions.ILike(e.Category, "inventory%")
                && !EF.Functions.ILike(e.Category, "%stock%")
                && !EF.Functions.ILike(e.Category, "%goods for%")
                && !EF.Functions.ILike(e.Category, "raw material%")
                && !EF.Functions.ILike(e.Category, "%merchandise%")
                && !EF.Functions.ILike(e.Category, "%replenish%")
                && !EF.Functions.ILike(e.Category, "%restock%"));
        }

        var total = await query.CountAsync();
        var totalAmount = await query.SumAsync(e => e.Amount);
        var items = await query
            .OrderByDescending(e => e.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => ToDto(e))
            .ToListAsync();

        return new PaginatedResult<ExpenseDto>
        {
            Items = items,
            TotalCount = total,
            TotalAmount = totalAmount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<ExpenseFiltersDto> GetFiltersAsync(Guid businessId, string? expenseType = null)
    {
        var query = _db.Expenses.Where(e => e.BusinessId == businessId);

        if (expenseType == "cogs")
        {
            query = query.Where(e => e.ExpenseType == "cogs"
                || EF.Functions.ILike(e.Category, "inventory%")
                || EF.Functions.ILike(e.Category, "%stock%")
                || EF.Functions.ILike(e.Category, "%goods for%")
                || EF.Functions.ILike(e.Category, "raw material%")
                || EF.Functions.ILike(e.Category, "%merchandise%")
                || EF.Functions.ILike(e.Category, "%replenish%")
                || EF.Functions.ILike(e.Category, "%restock%"));
        }
        else if (expenseType == "operating")
        {
            query = query.Where(e => e.ExpenseType != "cogs"
                && !EF.Functions.ILike(e.Category, "inventory%")
                && !EF.Functions.ILike(e.Category, "%stock%")
                && !EF.Functions.ILike(e.Category, "%goods for%")
                && !EF.Functions.ILike(e.Category, "raw material%")
                && !EF.Functions.ILike(e.Category, "%merchandise%")
                && !EF.Functions.ILike(e.Category, "%replenish%")
                && !EF.Functions.ILike(e.Category, "%restock%"));
        }

        var categories = await query.Select(e => e.Category).Distinct().OrderBy(c => c).ToListAsync();
        var methods = await query.Where(e => e.PaymentMethod != null).Select(e => e.PaymentMethod!).Distinct().OrderBy(m => m).ToListAsync();
        var sources = await query.Select(e => e.Source).Distinct().OrderBy(s => s).ToListAsync();

        return new ExpenseFiltersDto { Categories = categories, PaymentMethods = methods, Sources = sources };
    }

    public async Task<ExpenseDto> UpdateAsync(Guid businessId, Guid expenseId, UpdateExpenseRequest request)
    {
        var expense = await _db.Expenses.FirstOrDefaultAsync(e => e.Id == expenseId && e.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Expense not found.");

        if (request.Category != null) expense.Category = request.Category;
        if (request.ExpenseType != null) expense.ExpenseType = request.ExpenseType;
        if (request.Amount.HasValue && request.Amount.Value > 0) expense.Amount = request.Amount.Value;
        if (request.Notes != null) expense.Notes = request.Notes;
        if (request.PaidTo != null) expense.PaidTo = request.PaidTo;
        if (request.PaymentMethod != null) expense.PaymentMethod = request.PaymentMethod;

        await _db.SaveChangesAsync();
        return ToDto(expense);
    }

    public async Task DeleteAsync(Guid businessId, Guid expenseId)
    {
        var expense = await _db.Expenses.FirstOrDefaultAsync(e => e.Id == expenseId && e.BusinessId == businessId)
            ?? throw new KeyNotFoundException("Expense not found.");

        expense.IsDeleted = true;
        expense.DeletedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    private static ExpenseDto ToDto(Expense e) => new()
    {
        Id = e.Id,
        Category = e.Category,
        ExpenseType = e.ExpenseType,
        Amount = e.Amount,
        Notes = e.Notes,
        PaidTo = e.PaidTo,
        PaymentMethod = e.PaymentMethod,
        Source = e.Source,
        CreatedAtUtc = e.CreatedAtUtc
    };
}

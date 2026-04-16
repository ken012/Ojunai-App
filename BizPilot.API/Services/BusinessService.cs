using System.Text.Json;
using BizPilot.API.Data;
using BizPilot.API.DTOs.Auth;
using BizPilot.API.DTOs.Business;
using BizPilot.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BizPilot.API.Services;

public class BusinessService : IBusinessService
{
    private readonly AppDbContext _db;

    public BusinessService(AppDbContext db) => _db = db;

    public async Task<BusinessDto> UpdateAsync(Guid businessId, UpdateBusinessRequest request)
    {
        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId)
            ?? throw new KeyNotFoundException("Business not found.");

        // Name is intentionally NOT editable — it's set at registration only.
        if (request.BusinessType != null) business.BusinessType = string.IsNullOrWhiteSpace(request.BusinessType) ? null : request.BusinessType.Trim();
        if (!string.IsNullOrWhiteSpace(request.Currency)) business.Currency = request.Currency.Trim().ToUpperInvariant();
        if (request.State != null) business.State = string.IsNullOrWhiteSpace(request.State) ? null : request.State.Trim();
        if (request.City != null) business.City = string.IsNullOrWhiteSpace(request.City) ? null : request.City.Trim();
        if (!string.IsNullOrWhiteSpace(request.Country)) business.Country = request.Country.Trim();
        if (request.LargeSaleThreshold.HasValue && request.LargeSaleThreshold.Value > 0) business.LargeSaleThreshold = request.LargeSaleThreshold.Value;
        if (request.CustomCategories != null) business.CustomCategories = JsonSerializer.Serialize(request.CustomCategories);
        if (request.AlertLowStock.HasValue) business.AlertLowStock = request.AlertLowStock.Value;
        if (request.AlertDailySummary.HasValue) business.AlertDailySummary = request.AlertDailySummary.Value;
        if (request.AlertLargeSale.HasValue) business.AlertLargeSale = request.AlertLargeSale.Value;

        await _db.SaveChangesAsync();
        return ToDto(business);
    }

    public async Task<BusinessDto> GetByIdAsync(Guid businessId)
    {
        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId)
            ?? throw new KeyNotFoundException("Business not found.");
        return ToDto(business);
    }

    private static BusinessDto ToDto(Models.Business b) => new()
    {
        Id = b.Id,
        Name = b.Name,
        BusinessType = b.BusinessType,
        Currency = b.Currency,
        State = b.State,
        City = b.City,
        Country = b.Country,
        Plan = b.Plan,
        SubscribedPlan = b.SubscribedPlan,
        TrialEndsAt = b.TrialEndsAt,
        LargeSaleThreshold = b.LargeSaleThreshold,
        CustomCategories = string.IsNullOrEmpty(b.CustomCategories)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(b.CustomCategories) ?? new List<string>(),
        AlertLowStock = b.AlertLowStock,
        AlertDailySummary = b.AlertDailySummary,
        AlertLargeSale = b.AlertLargeSale,
        IsActive = b.IsActive
    };
}

using System.Text.Json;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Auth;
using Ojunai.API.DTOs.Business;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services;

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
        if (!string.IsNullOrWhiteSpace(request.Currency))
            business.Currency = request.Currency.Trim().ToUpperInvariant();
        if (request.State != null) business.State = string.IsNullOrWhiteSpace(request.State) ? null : request.State.Trim();
        if (request.City != null) business.City = string.IsNullOrWhiteSpace(request.City) ? null : request.City.Trim();
        if (!string.IsNullOrWhiteSpace(request.Country))
        {
            business.Country = request.Country.Trim();
            // Auto-set timezone when country changes. Currency is only auto-set if the user didn't
            // explicitly provide one in the same request (they might want GHS country but USD currency).
            var countryInfo = Common.CountryLookup.GetByName(request.Country);
            if (countryInfo != null)
            {
                business.Timezone = countryInfo.Timezone;
                if (string.IsNullOrWhiteSpace(request.Currency)) business.Currency = countryInfo.Currency;
            }
        }
        if (request.LargeSaleThreshold.HasValue && request.LargeSaleThreshold.Value > 0) business.LargeSaleThreshold = request.LargeSaleThreshold.Value;
        if (request.CustomCategories != null) business.CustomCategories = JsonSerializer.Serialize(request.CustomCategories);
        if (request.AlertLowStock.HasValue) business.AlertLowStock = request.AlertLowStock.Value;
        if (request.AlertDailySummary.HasValue) business.AlertDailySummary = request.AlertDailySummary.Value;
        if (request.AlertLargeSale.HasValue) business.AlertLargeSale = request.AlertLargeSale.Value;
        if (request.ConfirmLargeSales.HasValue) business.ConfirmLargeSales = request.ConfirmLargeSales.Value;
        if (request.ConfirmLargeSaleThreshold.HasValue) business.ConfirmLargeSaleThreshold = request.ConfirmLargeSaleThreshold.Value;
        if (request.LargeSaleAlertWhatsApp.HasValue) business.LargeSaleAlertWhatsApp = request.LargeSaleAlertWhatsApp.Value;
        if (request.LargeSaleAlertTelegram.HasValue) business.LargeSaleAlertTelegram = request.LargeSaleAlertTelegram.Value;
        if (request.LargeSaleAlertMessenger.HasValue) business.LargeSaleAlertMessenger = request.LargeSaleAlertMessenger.Value;
        if (request.LargeSaleAlertDashboard.HasValue) business.LargeSaleAlertDashboard = request.LargeSaleAlertDashboard.Value;
        if (request.AlertDashboardLowStock.HasValue) business.AlertDashboardLowStock = request.AlertDashboardLowStock.Value;
        if (request.AlertDashboardDailySummary.HasValue) business.AlertDashboardDailySummary = request.AlertDashboardDailySummary.Value;
        if (request.AlertDashboardLargeSale.HasValue) business.AlertDashboardLargeSale = request.AlertDashboardLargeSale.Value;
        if (request.AlertDashboardAgedReceivable.HasValue) business.AlertDashboardAgedReceivable = request.AlertDashboardAgedReceivable.Value;
        if (request.AlertDashboardStaffChanges.HasValue) business.AlertDashboardStaffChanges = request.AlertDashboardStaffChanges.Value;
        // DailySalesGoal: 0 or null means "no goal." Generators only fire when value > 0.
        // We can't distinguish "field omitted" from "explicitly null" via HasValue, so we
        // only update when the caller sends a number. To clear, send 0.
        if (request.DailySalesGoal.HasValue) business.DailySalesGoal = request.DailySalesGoal.Value > 0 ? request.DailySalesGoal.Value : null;
        if (request.BackgroundImageOpacity.HasValue)
        {
            // Clamp 0..1 (Range attribute should already enforce, but be defensive).
            var op = request.BackgroundImageOpacity.Value;
            business.BackgroundImageOpacity = op < 0 ? 0 : op > 1 ? 1 : op;
        }
        if (request.Address != null) business.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        if (request.VatEnabled.HasValue) business.VatEnabled = request.VatEnabled.Value;
        if (request.VatRate.HasValue && request.VatRate.Value >= 0 && request.VatRate.Value <= 100) business.VatRate = request.VatRate.Value;
        if (request.TaxId != null) business.TaxId = string.IsNullOrWhiteSpace(request.TaxId) ? null : request.TaxId.Trim();
        if (request.ReceiptHeaderText != null) business.ReceiptHeaderText = string.IsNullOrWhiteSpace(request.ReceiptHeaderText) ? null : request.ReceiptHeaderText.Trim();
        if (request.ReceiptFooterText != null) business.ReceiptFooterText = string.IsNullOrWhiteSpace(request.ReceiptFooterText) ? null : request.ReceiptFooterText.Trim();
        if (request.ReceiptAccentColor != null) business.ReceiptAccentColor = string.IsNullOrWhiteSpace(request.ReceiptAccentColor) ? null : request.ReceiptAccentColor.Trim();

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
        Timezone = b.Timezone,
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
        ConfirmLargeSales = b.ConfirmLargeSales,
        ConfirmLargeSaleThreshold = b.ConfirmLargeSaleThreshold,
        LargeSaleAlertWhatsApp = b.LargeSaleAlertWhatsApp,
        LargeSaleAlertTelegram = b.LargeSaleAlertTelegram,
        LargeSaleAlertMessenger = b.LargeSaleAlertMessenger,
        LargeSaleAlertDashboard = b.LargeSaleAlertDashboard,
        AlertDashboardLowStock = b.AlertDashboardLowStock,
        AlertDashboardDailySummary = b.AlertDashboardDailySummary,
        AlertDashboardLargeSale = b.AlertDashboardLargeSale,
        AlertDashboardAgedReceivable = b.AlertDashboardAgedReceivable,
        AlertDashboardStaffChanges = b.AlertDashboardStaffChanges,
        DailySalesGoal = b.DailySalesGoal,
        BackgroundImageUrl = string.IsNullOrEmpty(b.BackgroundImageFileName)
            ? null
            : $"/uploads/businesses/{b.Id:N}/{b.BackgroundImageFileName}",
        BackgroundImageOpacity = b.BackgroundImageOpacity,
        IsActive = b.IsActive,
        AccountNumber = b.AccountNumber,
        Address = b.Address,
        VatEnabled = b.VatEnabled,
        VatRate = b.VatRate,
        TaxId = b.TaxId,
        ReceiptHeaderText = b.ReceiptHeaderText,
        ReceiptFooterText = b.ReceiptFooterText,
        ReceiptAccentColor = b.ReceiptAccentColor
    };
}

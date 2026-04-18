using BizPilot.API.Common;
using BizPilot.API.Data;
using BizPilot.API.Models;
using BizPilot.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BizPilot.API.Jobs;

public class TrialReminderJobService
{
    private readonly AppDbContext _db;
    private readonly IWhatsAppService _whatsApp;
    private readonly ILogger<TrialReminderJobService> _logger;

    public TrialReminderJobService(AppDbContext db, IWhatsAppService whatsApp, ILogger<TrialReminderJobService> logger)
    {
        _db = db;
        _whatsApp = whatsApp;
        _logger = logger;
    }

    public async Task SendTrialRemindersAsync()
    {
        var now = DateTime.UtcNow;
        var businesses = await _db.Businesses
            .Include(b => b.Users)
            .Where(b => b.IsActive && b.TrialEndsAt.HasValue)
            .ToListAsync();

        foreach (var biz in businesses)
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(biz.Timezone ?? "Africa/Lagos");
                var localHour = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Hour;
                if (localHour != 10) continue; // Only send at 10 AM local time

                var trial = PlanGuard.GetTrialStatus(biz);
                if (trial is not (TrialStatus.Active or TrialStatus.Expired)) continue;

                var owner = biz.Users.FirstOrDefault(u => u.Role == UserRole.Owner && u.IsActive);
                if (owner == null) continue;

                var daysLeft = (int)Math.Ceiling((biz.TrialEndsAt!.Value - now).TotalDays);
                var planLabel = biz.Plan[0..1].ToUpper() + biz.Plan[1..];
                var planConfig = PlanLimits.Get(biz.Plan);

                var features = new List<string>();
                if (planConfig.MaxProducts > 0) features.Add($"{planConfig.MaxProducts} products");
                if (planConfig.MaxProducts < 0) features.Add("Unlimited products");
                if (planConfig.MaxMessagesPerMonth > 0) features.Add($"{planConfig.MaxMessagesPerMonth} messages/month");
                if (planConfig.MaxMessagesPerMonth < 0) features.Add("Unlimited messages");
                if (planConfig.HasCsvImport) features.Add("CSV import");
                if (planConfig.HasAdvancedReports) features.Add("Advanced reports");
                if (planConfig.HasStockHolds) features.Add("Stock holds");
                if (planConfig.HasLedger) features.Add("Ledger");
                if (planConfig.MaxStaff > 1) features.Add($"Up to {planConfig.MaxStaff} users");

                var featureList = string.Join("\n• ", features);

                var message = daysLeft switch
                {
                    7 => $"👋 *{planLabel} Trial Reminder*\n\nYour BizPilot {planLabel} free trial ends in 7 days. Your plan includes:\n• {featureList}\n\nSubscribe at app.bizpilot-ai.com/settings to keep access.",
                    3 => $"⏳ *{planLabel} Trial Ending Soon*\n\nYour BizPilot {planLabel} free trial ends in 3 days. After that, your account will be restricted until you subscribe.\n\nSubscribe at app.bizpilot-ai.com/settings",
                    1 => $"⚠️ *Last Day of {planLabel} Trial*\n\nYour BizPilot {planLabel} free trial ends tomorrow. Subscribe now to keep access to:\n• {featureList}\n\nSubscribe at app.bizpilot-ai.com/settings",
                    0 => $"🔒 *{planLabel} Trial Ended*\n\nYour BizPilot {planLabel} free trial has ended. Subscribe at app.bizpilot-ai.com/settings to restore full access.",
                    _ => null
                };

                if (message != null)
                {
                    await _whatsApp.SendMessageAsync($"whatsapp:{owner.PhoneNumber}", message, biz.Id, owner.Id);
                    _logger.LogInformation("Sent trial reminder ({DaysLeft}d) to {Business} on {Plan} plan", daysLeft, biz.Name, biz.Plan);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send trial reminder for {Business}", biz.Name);
            }
        }
    }
}

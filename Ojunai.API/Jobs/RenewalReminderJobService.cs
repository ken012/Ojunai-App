using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Jobs;

public class RenewalReminderJobService
{
    private const int ReminderHour = 9;

    private readonly AppDbContext _db;
    private readonly IWhatsAppService _whatsApp;
    private readonly ILogger<RenewalReminderJobService> _logger;

    public RenewalReminderJobService(AppDbContext db, IWhatsAppService whatsApp, ILogger<RenewalReminderJobService> logger)
    {
        _db = db;
        _whatsApp = whatsApp;
        _logger = logger;
    }

    public async Task SendRenewalRemindersAsync()
    {
        var utcNow = DateTime.UtcNow;
        var businesses = await _db.Businesses
            .Include(b => b.Users)
            .Where(b => b.IsActive
                && !b.IsAutoRenew
                && b.SubscriptionEndsAt.HasValue
                && b.SubscriptionEndsAt > utcNow
                && b.SubscribedPlan != null
                && b.SubscribedPlan != "starter")
            .ToListAsync();

        foreach (var biz in businesses)
        {
            try
            {
                // Only send when it's 9 AM in the business's local timezone
                TimeZoneInfo tz;
                try { tz = TimeZoneInfo.FindSystemTimeZoneById(biz.Timezone ?? "Africa/Lagos"); }
                catch { tz = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos"); }
                var localHour = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz).Hour;
                if (localHour != ReminderHour) continue;

                // Only send one reminder per day per business
                var alreadySent = await _db.BillingEvents.AnyAsync(e =>
                    e.BusinessId == biz.Id
                    && e.EventType == "renewal.reminder"
                    && e.CreatedAtUtc >= utcNow.Date);
                if (alreadySent) continue;

                var owner = biz.Users.FirstOrDefault(u => u.Role == UserRole.Owner && u.IsActive);
                if (owner == null) continue;

                var daysLeft = (int)Math.Ceiling((biz.SubscriptionEndsAt!.Value - utcNow).TotalDays);
                var planLabel = biz.Plan[0..1].ToUpper() + biz.Plan[1..];
                var currency = biz.BillingCurrency ?? biz.Currency;
                var cycle = biz.BillingCycle ?? "monthly";

                if (!Enum.TryParse<BillingConfig.BillingCycle>(cycle, true, out var billingCycle))
                    billingCycle = BillingConfig.BillingCycle.Monthly;

                var price = BillingConfig.GetPrice(biz.Plan, billingCycle, currency);
                var formattedPrice = price.HasValue ? BillingConfig.FormatPrice(price.Value, currency) : "your plan";
                var expiryDate = biz.SubscriptionEndsAt.Value.ToString("dd MMM yyyy");

                var message = daysLeft switch
                {
                    7 => $"👋 *Subscription Renewal Reminder*\n\n"
                       + $"Your *{planLabel}* plan expires on {expiryDate}.\n\n"
                       + $"Renew at {formattedPrice} to keep all your features.\n\n"
                       + $"Renew at app.ojunai.com/settings",

                    3 => $"⏳ *Subscription Expiring Soon*\n\n"
                       + $"Your *{planLabel}* plan expires in 3 days ({expiryDate}).\n\n"
                       + $"After that your account will be restricted to Starter features.\n\n"
                       + $"Renew at app.ojunai.com/settings",

                    1 => $"⚠️ *Last Day — {planLabel} Plan*\n\n"
                       + $"Your *{planLabel}* plan expires tomorrow.\n\n"
                       + $"Renew now at {formattedPrice} to avoid losing access to your features.\n\n"
                       + $"Renew at app.ojunai.com/settings",

                    _ => null
                };

                if (message != null)
                {
                    _db.BillingEvents.Add(new BillingEvent
                    {
                        BusinessId = biz.Id,
                        EventType = "renewal.reminder",
                        Provider = biz.BillingProvider ?? "unknown",
                        Plan = biz.Plan,
                        Status = $"reminder_{daysLeft}d",
                        CreatedAtUtc = DateTime.UtcNow
                    });
                    await _db.SaveChangesAsync();

                    await _whatsApp.SendMessageAsync($"whatsapp:{owner.PhoneNumber}", message, biz.Id, owner.Id);
                    _logger.LogInformation("Sent renewal reminder ({DaysLeft}d) to {Business} on {Plan} (tz: {Tz})",
                        daysLeft, biz.Name, biz.Plan, biz.Timezone);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send renewal reminder for {Business}", biz.Name);
            }
        }
    }
}

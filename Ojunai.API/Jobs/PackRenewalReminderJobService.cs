using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Jobs;

/// <summary>
/// Daily job that surfaces upcoming WhatsApp pack expiries to merchants who need to renew
/// manually. Two windows fire:
///
///   - 3 days before <see cref="BusinessAddOn.NextBillingAtUtc"/> for non-auto-renewing packs:
///     gives the merchant time to come back and re-buy.
///   - 3 days before for past_due auto-renewing packs: warns that the failed-charge grace
///     window is about to close.
///
/// Idempotent — uses a BillingEvent record keyed by (BusinessId, EventType, day-of-event) to
/// detect "already reminded today" so re-runs are no-ops. Auto-renewing packs in active state
/// (no failed charge yet) are skipped — Paystack will charge automatically and no nudge is
/// needed.
/// </summary>
public sealed class PackRenewalReminderJobService
{
    private readonly AppDbContext _db;
    private readonly IAlertService _alerts;
    private readonly ILogger<PackRenewalReminderJobService> _logger;

    public PackRenewalReminderJobService(
        AppDbContext db,
        IAlertService alerts,
        ILogger<PackRenewalReminderJobService> logger)
    {
        _db = db;
        _alerts = alerts;
        _logger = logger;
    }

    public async Task RunDailyAsync()
    {
        var now = DateTime.UtcNow;
        var windowStart = now.AddDays(2); // remind 3 days out (window: [+2d, +4d])
        var windowEnd = now.AddDays(4);

        var candidates = await _db.BusinessAddOns
            .Where(a => a.AddOnCode.StartsWith("whatsapp_pack.")
                && (
                    // Non-renewing pack expiring soon
                    (a.Status == "active" && !a.IsAutoRenew
                        && a.NextBillingAtUtc != null
                        && a.NextBillingAtUtc >= windowStart && a.NextBillingAtUtc <= windowEnd)
                    // Past-due auto-renewing pack approaching 5-day expiry
                    || (a.Status == "past_due"
                        && a.UpdatedAtUtc >= now.AddDays(-4) && a.UpdatedAtUtc <= now.AddDays(-2))
                ))
            .ToListAsync();

        if (candidates.Count == 0)
        {
            _logger.LogDebug("PackRenewalReminderJob: no packs in reminder window");
            return;
        }

        var today = DateOnly.FromDateTime(now);
        var notified = 0;
        foreach (var addon in candidates)
        {
            // Idempotency: skip if we already logged a reminder for this addon today.
            var dedupeKey = $"whatsapp_pack.reminder.{addon.Id}.{today:yyyyMMdd}";
            var alreadyReminded = await _db.BillingEvents.AnyAsync(e =>
                e.BusinessId == addon.BusinessId
                && e.SubscriptionId == dedupeKey);
            if (alreadyReminded) continue;

            var packLabel = addon.AddOnCode.Replace("whatsapp_pack.", "");
            var daysRemaining = addon.NextBillingAtUtc.HasValue
                ? Math.Max(0, (int)(addon.NextBillingAtUtc.Value - now).TotalDays)
                : 0;
            var isPastDue = addon.Status == "past_due";

            var title = isPastDue
                ? $"WhatsApp {packLabel} pack — payment failed"
                : $"WhatsApp {packLabel} pack expires in {daysRemaining} day{(daysRemaining == 1 ? "" : "s")}";
            var body = isPastDue
                ? "We couldn't renew your WhatsApp pack. Update your payment method to keep messaging — your pack will expire soon."
                : $"Your WhatsApp pack ends on {addon.NextBillingAtUtc:MMM d}. Re-purchase a pack to keep WhatsApp messaging without interruption.";

            await _alerts.CreateAsync(
                addon.BusinessId,
                null, // business-wide alert; surface to whichever staff has notifications on
                AlertType.WhatsAppPackExpiringSoon,
                AlertSeverity.Warning,
                title: title,
                body: body,
                linkUrl: "/settings#plan",
                dedupeKey: dedupeKey);

            _db.BillingEvents.Add(new BillingEvent
            {
                BusinessId = addon.BusinessId,
                EventType = "whatsapp_pack.reminder_sent",
                Provider = "system",
                Plan = addon.AddOnCode,
                Status = "reminded",
                SubscriptionId = dedupeKey,
                CreatedAtUtc = now,
            });
            notified++;
        }

        await _db.SaveChangesAsync();
        if (notified > 0)
            _logger.LogInformation("PackRenewalReminderJob: sent {Count} reminder(s)", notified);
    }
}

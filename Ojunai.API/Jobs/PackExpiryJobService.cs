using Ojunai.API.Data;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Jobs;

/// <summary>
/// Daily sweep that expires non-renewing WhatsApp packs whose billing period has rolled over.
/// Without this job, a one-time pack purchase would stay "active" forever — the merchant pays
/// once and gets the pack's full quota every month for free. This job is the enforcement side
/// of the one-time billing model.
///
/// Behavior:
///   - Find BusinessAddOns where AddOnCode starts with "whatsapp_pack.", Status == "active",
///     IsAutoRenew == false, and NextBillingAtUtc &lt; now.
///   - Mark each as Status="expired", CancelledAtUtc=now.
///   - Log a BillingEvent for audit.
///
/// Auto-renewing packs (IsAutoRenew=true) are skipped. Those are expected to have their
/// NextBillingAtUtc bumped by the recurring webhook handler on each Paystack charge. If the
/// recurring charge fails for several days, the merchant's bank pulls the auth, and we'd want
/// to also expire — that's a follow-up (track failed renewal events and trip the expiry).
///
/// Idempotent — running twice is a no-op since the first run flips Status and the next run's
/// WHERE clause excludes expired rows.
/// </summary>
public sealed class PackExpiryJobService
{
    private readonly AppDbContext _db;
    private readonly ILogger<PackExpiryJobService> _logger;

    public PackExpiryJobService(AppDbContext db, ILogger<PackExpiryJobService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>How long a past_due pack stays around before final expiry. Gives Paystack
    /// time to retry the failed charge (their auto-retry cadence is approximately daily for
    /// the first 3 days then escalating).</summary>
    private static readonly TimeSpan PastDueGrace = TimeSpan.FromDays(5);

    public async Task RunDailyAsync()
    {
        var now = DateTime.UtcNow;
        var pastDueCutoff = now - PastDueGrace;

        var expiring = await _db.BusinessAddOns
            .Where(a => a.AddOnCode.StartsWith("whatsapp_pack.")
                && (
                    // One-time pack whose billing period rolled over.
                    (a.Status == "active" && !a.IsAutoRenew
                        && a.NextBillingAtUtc != null && a.NextBillingAtUtc < now)
                    // Auto-renewing pack that's been past_due (failed renewal) for >5 days.
                    || (a.Status == "past_due" && a.UpdatedAtUtc < pastDueCutoff)
                ))
            .ToListAsync();

        if (expiring.Count == 0)
        {
            _logger.LogDebug("PackExpiryJob: no packs to expire");
            return;
        }

        foreach (var addon in expiring)
        {
            var eventType = addon.Status == "past_due"
                ? "whatsapp_pack.expired_after_failed_renewal"
                : "whatsapp_pack.expired";
            addon.Status = "expired";
            addon.CancelledAtUtc = now;
            addon.UpdatedAtUtc = now;

            _db.BillingEvents.Add(new Models.BillingEvent
            {
                BusinessId = addon.BusinessId,
                EventType = eventType,
                Provider = "system",
                Plan = addon.AddOnCode,
                Amount = addon.BilledAmount,
                Currency = addon.BilledCurrency,
                PaymentMethod = "expiry_job",
                Status = "expired",
            });
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("PackExpiryJob: expired {Count} pack(s)", expiring.Count);
    }
}

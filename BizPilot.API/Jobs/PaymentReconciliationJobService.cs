using BizPilot.API.Data;
using BizPilot.API.Models;
using Microsoft.EntityFrameworkCore;

namespace BizPilot.API.Jobs;

public class PaymentReconciliationJobService
{
    private readonly AppDbContext _db;
    private readonly ILogger<PaymentReconciliationJobService> _logger;

    public PaymentReconciliationJobService(AppDbContext db, ILogger<PaymentReconciliationJobService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ReconcileAsync()
    {
        var now = DateTime.UtcNow;
        var checkWindow = now.AddDays(-3);

        // Find auto-renew subscriptions that should have renewed but SubscriptionEndsAt is in the past
        var stale = await _db.Businesses
            .Where(b => b.IsActive
                && b.IsAutoRenew
                && b.SubscriptionEndsAt.HasValue
                && b.SubscriptionEndsAt.Value < now
                && b.SubscriptionEndsAt.Value > checkWindow
                && b.SubscriptionStatus == "active"
                && b.Plan != "starter")
            .ToListAsync();

        foreach (var biz in stale)
        {
            _logger.LogWarning(
                "Reconciliation: {Business} auto-renew sub expired {EndsAt} but still active. Provider: {Provider}, Plan: {Plan}",
                biz.Name, biz.SubscriptionEndsAt, biz.BillingProvider, biz.Plan);

            biz.SubscriptionStatus = "past_due";

            _db.BillingEvents.Add(new BillingEvent
            {
                BusinessId = biz.Id,
                EventType = "reconciliation.past_due",
                Provider = biz.BillingProvider ?? "unknown",
                Plan = biz.Plan,
                Status = "past_due",
                ErrorDetails = $"Auto-renew subscription expired at {biz.SubscriptionEndsAt:u} but no renewal webhook received",
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        if (stale.Count > 0)
        {
            await _db.SaveChangesAsync();
            _logger.LogInformation("Reconciliation complete: {Count} stale subscriptions marked past_due", stale.Count);
        }
        else
        {
            _logger.LogInformation("Reconciliation complete: no stale subscriptions found");
        }
    }
}

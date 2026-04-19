using BizPilot.API.Data;
using BizPilot.API.Models;
using BizPilot.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BizPilot.API.Jobs;

public class PaymentReconciliationJobService
{
    private readonly AppDbContext _db;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PaymentReconciliationJobService> _logger;

    public PaymentReconciliationJobService(AppDbContext db, IServiceProvider serviceProvider, ILogger<PaymentReconciliationJobService> logger)
    {
        _db = db;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task ReconcileAsync()
    {
        var now = DateTime.UtcNow;
        var checkWindow = now.AddDays(-3);

        var stale = await _db.Businesses
            .Include(b => b.Users)
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

            // Notify business owner via WhatsApp
            try
            {
                var owner = biz.Users.FirstOrDefault(u => u.Role == UserRole.Owner && u.IsActive);
                if (owner != null)
                {
                    var planLabel = biz.Plan[0..1].ToUpper() + biz.Plan[1..];
                    var whatsApp = _serviceProvider.GetRequiredService<IWhatsAppService>();
                    await whatsApp.SendMessageAsync(
                        $"whatsapp:{owner.PhoneNumber}",
                        $"Your *{planLabel}* plan renewal could not be processed. " +
                        $"Please visit app.bizpilot-ai.com/settings to resubscribe and keep your features.",
                        biz.Id, owner.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send reconciliation alert for {Business}", biz.Name);
            }
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

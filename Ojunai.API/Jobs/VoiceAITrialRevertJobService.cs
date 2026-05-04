using Ojunai.API.Data;
using Ojunai.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Jobs;

public class VoiceAITrialRevertJobService
{
    private readonly AppDbContext _db;
    private readonly ILogger<VoiceAITrialRevertJobService> _logger;

    public VoiceAITrialRevertJobService(AppDbContext db, ILogger<VoiceAITrialRevertJobService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task RevertExpiredTrialsAsync()
    {
        var now = DateTime.UtcNow;

        // Expire Voice AI trials
        var expiredTrials = await _db.Businesses
            .Where(b => b.IsActive
                && b.VoiceAIPlanStatus == "trial"
                && b.VoiceAITrialEndsAt.HasValue
                && b.VoiceAITrialEndsAt.Value < now
                && !b.VoiceAIInternalOverride)
            .ToListAsync();

        foreach (var biz in expiredTrials)
        {
            biz.VoiceAIPlanStatus = "suspended";
            biz.VoiceAIEnabled = false;

            _db.BillingEvents.Add(new BillingEvent
            {
                BusinessId = biz.Id,
                EventType = "voiceai.trial.expired",
                Provider = "system",
                Plan = "voice_ai",
                Status = "suspended"
            });
        }

        // Suspend expired Voice AI subscriptions
        var expiredSubs = await _db.Businesses
            .Where(b => b.IsActive
                && b.VoiceAIPlanStatus == "active"
                && b.VoiceAISubscriptionEndsAt.HasValue
                && b.VoiceAISubscriptionEndsAt.Value < now
                && !b.VoiceAIInternalOverride)
            .ToListAsync();

        foreach (var biz in expiredSubs)
        {
            biz.VoiceAIPlanStatus = "suspended";
            biz.VoiceAIEnabled = false;

            _db.BillingEvents.Add(new BillingEvent
            {
                BusinessId = biz.Id,
                EventType = "voiceai.subscription.expired",
                Provider = "system",
                Plan = "voice_ai",
                Status = "suspended"
            });
        }

        var total = expiredTrials.Count + expiredSubs.Count;
        if (total > 0)
        {
            await _db.SaveChangesAsync();
            _logger.LogInformation("Voice AI revert: {Trials} trials expired, {Subs} subscriptions expired",
                expiredTrials.Count, expiredSubs.Count);
        }
    }
}

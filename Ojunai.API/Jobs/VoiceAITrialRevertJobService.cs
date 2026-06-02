using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Common;
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

        // Trial reverter — backstop for any business whose trial minutes hit the cap but didn't get
        // flipped during the live POST /voice-ai-minutes call (e.g., the Voice AI service crashed
        // between persisting minutes and our handler running). The minutes endpoint is the primary
        // gate; this job catches stragglers.
        var trialCap = BillingConfig.VoiceAITrialMinutes;
        var expiredTrials = await _db.Businesses
            .Where(b => b.IsActive
                && b.VoiceAIPlanStatus == "trial"
                && b.VoiceAITrialMinutesUsed >= trialCap
                && !b.VoiceAIInternalOverride)
            .ToListAsync();

        foreach (var biz in expiredTrials)
        {
            biz.VoiceAIPlanStatus = "suspended";
            biz.VoiceAIEnabled = false;

            _db.BillingEvents.Add(new BillingEvent
            {
                BusinessId = biz.Id,
                EventType = "voiceai.trial.minutes_exhausted",
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

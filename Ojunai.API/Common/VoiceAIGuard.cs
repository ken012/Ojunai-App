using Ojunai.API.Data;
using Ojunai.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Common;

public class VoiceAIGuard
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public VoiceAIGuard(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public bool IsFeatureEnabled()
        => _config.GetValue<bool>("VoiceAI:FeatureEnabled");

    public static bool HasAccess(Business business)
    {
        if (business.VoiceAIInternalOverride) return true;
        if (business.VoiceAIPlanStatus == "active") return true;
        // Trial access ends the moment the user has consumed their trial minutes — gate at read time
        // so the Voice AI service can't accidentally let a call through after the cap was hit (the
        // sweeper job is a backstop, not the primary enforcement).
        if (business.VoiceAIPlanStatus == "trial"
            && business.VoiceAITrialMinutesUsed < BillingConfig.VoiceAITrialMinutes)
            return true;
        return false;
    }

    public async Task<(bool Allowed, string? Error)> CheckAccessAsync(Guid businessId)
    {
        if (!IsFeatureEnabled())
            return (false, null);

        var business = await _db.Businesses.FindAsync(businessId);
        if (business == null)
            return (false, "Business not found.");

        if (HasAccess(business))
            return (true, null);

        return (false, "Voice AI is not enabled for this business. Enable it in your dashboard settings.");
    }

    public static string GetVoiceAITrialStatus(Business business)
    {
        if (business.VoiceAIPlanStatus != "trial") return "none";
        return business.VoiceAITrialMinutesUsed < BillingConfig.VoiceAITrialMinutes ? "active" : "expired";
    }

    /// <summary>Inbound minutes still available on the free trial (0 once used up).</summary>
    public static int? GetVoiceAITrialMinutesRemaining(Business business)
    {
        if (business.VoiceAIPlanStatus != "trial") return null;
        return Math.Max(0, BillingConfig.VoiceAITrialMinutes - business.VoiceAITrialMinutesUsed);
    }

    /// <summary>Inbound minutes still available in the current paid billing cycle. Null if not on a tier.</summary>
    public static int? GetVoiceAICycleMinutesRemaining(Business business)
    {
        if (string.IsNullOrEmpty(business.VoiceAITier)) return null;
        if (!BillingConfig.VoiceAITierMinutes.TryGetValue(business.VoiceAITier, out var included)) return null;
        return Math.Max(0, included - business.VoiceAICycleMinutesUsed);
    }
}

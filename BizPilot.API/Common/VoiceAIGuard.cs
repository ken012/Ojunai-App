using BizPilot.API.Data;
using BizPilot.API.Models;
using Microsoft.EntityFrameworkCore;

namespace BizPilot.API.Common;

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
        return business.VoiceAIPlanStatus is "active" or "trial";
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
        if (!business.VoiceAITrialEndsAt.HasValue) return "none";
        if (DateTime.UtcNow < business.VoiceAITrialEndsAt.Value) return "active";
        return "expired";
    }

    public static int? GetVoiceAITrialDaysLeft(Business business)
    {
        if (business.VoiceAIPlanStatus != "trial" || !business.VoiceAITrialEndsAt.HasValue)
            return null;
        return Math.Max(0, (int)Math.Ceiling((business.VoiceAITrialEndsAt.Value - DateTime.UtcNow).TotalDays));
    }
}

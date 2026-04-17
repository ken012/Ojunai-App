using BizPilot.API.Data;
using BizPilot.API.Models;
using Microsoft.EntityFrameworkCore;

namespace BizPilot.API.Common;

/// <summary>
/// Describes where a business is in its trial lifecycle.
/// </summary>
public enum TrialStatus
{
    None,         // No trial — either never had one, or trial has been fully cleared on subscription
    Active,       // Trial in progress (before TrialEndsAt)
    GracePeriod,  // Trial expired, but within the 3-day grace period where features still work
    Expired       // Trial + grace period have both passed
}

/// <summary>
/// Central gate for plan-based feature access and usage limits.
/// This class is the single source of truth for "can business X do Y?" across the API and WhatsApp flows.
/// Feature limits, plan tiers, trial rules, and upgrade messaging all flow through here.
/// </summary>
public class PlanGuard
{
    private readonly AppDbContext _db;

    // After a trial expires, features stay enabled for this many days so the user can subscribe without interruption.
    private const int GracePeriodDays = 3;

    // Default length of every new trial.
    public const int TrialDurationDays = 30;

    // Only these plans offer a free trial. Business tier users must pay from day one.
    public static readonly HashSet<string> TrialEligiblePlans = new(StringComparer.OrdinalIgnoreCase)
        { "starter", "shop", "pro" };

    // Hierarchy of plans from cheapest to most capable. Used to compare ranks (upgrade vs downgrade).
    private static readonly string[] PlanOrder = { "starter", "shop", "pro", "business" };

    private static readonly Dictionary<string, string> FeatureLabels = new()
    {
        ["ledger"] = "Ledger (credits & debts)",
        ["csv_import"] = "CSV Import",
        ["advanced_reports"] = "Advanced Reports",
        ["monthly_charts"] = "Insights & Charts",
        ["stock_holds"] = "Stock Holds",
        ["multi_branch"] = "Multi-Branch",
        ["api_access"] = "API Access",
        ["custom_exports"] = "Custom Exports",
    };

    private static readonly Dictionary<string, string> PlanLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["starter"] = "Starter",
        ["shop"] = "Shop",
        ["pro"] = "Pro",
        ["business"] = "Business",
    };

    public PlanGuard(AppDbContext db) => _db = db;

    public async Task<Business?> GetBusinessAsync(Guid businessId)
        => await _db.Businesses.FindAsync(businessId);

    /// <summary>
    /// Classifies where a business is in the trial lifecycle based on TrialEndsAt + current plan.
    /// Business tier has no trial regardless of what TrialEndsAt says (paid from day one).
    /// </summary>
    public static TrialStatus GetTrialStatus(Business business)
    {
        // No trial end date means no trial was ever started (e.g., existing pre-trial accounts or paid subscribers).
        if (!business.TrialEndsAt.HasValue) return TrialStatus.None;

        // Business tier is explicitly excluded from trials even if TrialEndsAt is set.
        if (!TrialEligiblePlans.Contains(business.Plan)) return TrialStatus.None;

        var now = DateTime.UtcNow;
        if (now < business.TrialEndsAt.Value) return TrialStatus.Active;

        // Grace period: features continue to work for a few days after the trial officially ends.
        // This gives users time to subscribe without losing access mid-workflow.
        if (now < business.TrialEndsAt.Value.AddDays(GracePeriodDays)) return TrialStatus.GracePeriod;

        return TrialStatus.Expired;
    }

    /// <summary>True if the business has an actual paid subscription (not just a trial).</summary>
    public static bool IsSubscriber(Business business)
        => !string.IsNullOrEmpty(business.SubscribedPlan);

    /// <summary>
    /// Returns the index of the plan in the upgrade hierarchy. 0 = cheapest (starter), 3 = most capable (business).
    /// Used to determine whether a plan change is an upgrade or downgrade.
    /// </summary>
    public static int PlanRank(string? plan)
        => Array.IndexOf(PlanOrder, plan?.ToLower() ?? "starter");

    private static string GetPlanLabel(string? plan)
        => PlanLabels.TryGetValue(plan ?? "starter", out var label) ? label : plan ?? "Starter";

    private static string FindMinimumPlan(string feature)
    {
        foreach (var planName in PlanOrder)
        {
            var config = PlanLimits.Get(planName);
            var has = feature switch
            {
                "ledger" => config.HasLedger,
                "csv_import" => config.HasCsvImport,
                "advanced_reports" => config.HasAdvancedReports,
                "monthly_charts" => config.HasMonthlyCharts,
                "stock_holds" => config.HasStockHolds,
                "multi_branch" => config.HasMultiBranch,
                "api_access" => config.HasApiAccess,
                "custom_exports" => config.HasCustomExports,
                _ => false
            };
            if (has) return planName;
        }
        return "business";
    }

    public async Task<(bool Ok, string? Error)> CanStartTrialAsync(Guid businessId, string targetPlan)
    {
        if (!TrialEligiblePlans.Contains(targetPlan))
            return (false, $"The {targetPlan} plan does not offer a free trial.");

        var business = await _db.Businesses.FindAsync(businessId);
        if (business == null) return (false, "Business not found.");

        var trial = GetTrialStatus(business);
        if (trial is TrialStatus.Active or TrialStatus.GracePeriod)
            return (false, "You already have an active trial. Wait until it ends before starting a new one.");

        var targetRank = PlanRank(targetPlan);

        if (targetRank <= PlanRank("starter"))
            return (true, null);

        if (!IsSubscriber(business))
            return (false, "You need an active subscription before you can try a higher plan. Subscribe to your current plan first.");

        if (PlanRank(business.SubscribedPlan) < PlanRank("starter"))
            return (false, "You need at least a Starter subscription to try higher plans.");

        return (true, null);
    }

    public async Task<string?> StartTrialAsync(Guid businessId, string targetPlan)
    {
        var (ok, err) = await CanStartTrialAsync(businessId, targetPlan);
        if (!ok) return err;

        var business = (await _db.Businesses.FindAsync(businessId))!;
        business.Plan = targetPlan;
        business.TrialEndsAt = DateTime.UtcNow.AddDays(TrialDurationDays);
        await _db.SaveChangesAsync();
        return null;
    }

    public async Task RevertExpiredTrialsAsync()
    {
        var now = DateTime.UtcNow;
        var graceThreshold = now.AddDays(-GracePeriodDays);

        var expired = await _db.Businesses
            .Where(b => b.IsActive
                && b.TrialEndsAt.HasValue
                && b.TrialEndsAt.Value < graceThreshold
                && b.SubscribedPlan != null
                && b.Plan != b.SubscribedPlan)
            .ToListAsync();

        foreach (var biz in expired)
        {
            biz.Plan = biz.SubscribedPlan!;
            biz.TrialEndsAt = null;
        }

        if (expired.Count > 0)
            await _db.SaveChangesAsync();

        // Handle pending plan changes for expired subscriptions
        var pendingChanges = await _db.Businesses
            .Where(b => b.IsActive
                && b.PendingPlanChange != null
                && b.SubscriptionEndsAt.HasValue
                && b.SubscriptionEndsAt.Value < now
                && string.IsNullOrEmpty(b.PaystackSubscriptionCode))
            .ToListAsync();

        foreach (var biz in pendingChanges)
        {
            // Keep SubscribedPlan set to the new plan (even Starter) so the user stays recognized
            // as a subscriber who changed plans, not a new user. Prevents trial UI from re-appearing.
            biz.Plan = biz.PendingPlanChange!;
            biz.SubscribedPlan = biz.PendingPlanChange;
            biz.PendingPlanChange = null;
            biz.SubscriptionEndsAt = null;
            biz.TrialEndsAt = null;
        }

        if (pendingChanges.Count > 0)
            await _db.SaveChangesAsync();
    }

    public async Task<PlanConfig> GetEffectivePlanAsync(Guid businessId)
    {
        var business = await _db.Businesses.FindAsync(businessId);
        if (business == null) return PlanLimits.Get("starter");

        return PlanLimits.Get(business.Plan);
    }

    public async Task<string?> CheckProductLimitAsync(Guid businessId)
    {
        var plan = await GetEffectivePlanAsync(businessId);
        if (plan.MaxProducts < 0) return null;

        var count = await _db.Products.CountAsync(p => p.BusinessId == businessId && p.IsActive);
        if (count >= plan.MaxProducts)
            return $"You've reached the {plan.MaxProducts}-product limit on your plan. Upgrade to Shop for unlimited products.";
        return null;
    }

    public async Task<string?> CheckMessageLimitAsync(Guid businessId)
    {
        var business = await _db.Businesses.FindAsync(businessId);
        if (business == null) return null;
        var plan = PlanLimits.Get(business.Plan);
        if (plan.MaxMessagesPerMonth < 0) return null;

        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var count = await _db.MessageLogs
            .CountAsync(m => m.BusinessId == businessId && m.Direction == MessageDirection.Inbound && m.CreatedAtUtc >= monthStart);

        if (count >= plan.MaxMessagesPerMonth)
        {
            var nextPlan = PlanRank(business.Plan) < PlanRank("pro")
                ? $"Upgrade to {GetPlanLabel(PlanOrder[PlanRank(business.Plan) + 1])} for more."
                : "You've reached your message limit.";
            return $"You've used all {plan.MaxMessagesPerMonth} WhatsApp messages for this month. {nextPlan}";
        }
        return null;
    }

    public async Task<string?> CheckStaffLimitAsync(Guid businessId)
    {
        var business = await _db.Businesses.FindAsync(businessId);
        if (business == null) return null;
        var plan = PlanLimits.Get(business.Plan);
        if (plan.MaxStaff < 0) return null;

        var count = await _db.Users.CountAsync(u => u.BusinessId == businessId && u.IsActive);
        if (count >= plan.MaxStaff)
        {
            var nextPlan = PlanRank(business.Plan) < PlanRank("business")
                ? $"Upgrade to {GetPlanLabel(PlanOrder[PlanRank(business.Plan) + 1])} for up to {PlanLimits.Get(PlanOrder[PlanRank(business.Plan) + 1]).MaxStaff} users."
                : "";
            var msg = plan.MaxStaff == 1
                ? $"Staff accounts aren't available on the Starter plan. {nextPlan}"
                : $"Your {GetPlanLabel(business.Plan)} plan allows {plan.MaxStaff} users. {nextPlan}";
            return msg.Trim();
        }
        return null;
    }

    public async Task<(bool Allowed, string? Error)> CheckFeatureAsync(Guid businessId, string feature)
    {
        var plan = await GetEffectivePlanAsync(businessId);
        var allowed = feature switch
        {
            "ledger" => plan.HasLedger,
            "csv_import" => plan.HasCsvImport,
            "advanced_reports" => plan.HasAdvancedReports,
            "monthly_charts" => plan.HasMonthlyCharts,
            "stock_holds" => plan.HasStockHolds,
            "multi_branch" => plan.HasMultiBranch,
            "api_access" => plan.HasApiAccess,
            "custom_exports" => plan.HasCustomExports,
            _ => true
        };
        if (!allowed)
        {
            var featureLabel = FeatureLabels.GetValueOrDefault(feature, feature);
            var minPlan = FindMinimumPlan(feature);
            return (false, $"{featureLabel} is available on the {GetPlanLabel(minPlan)} plan and above. Upgrade at app.bizpilot-ai.com/settings");
        }
        return (true, null);
    }

    public async Task<string?> GetTrialWarningAsync(Guid businessId)
    {
        var business = await _db.Businesses.FindAsync(businessId);
        if (business == null) return null;

        var status = GetTrialStatus(business);
        if (status == TrialStatus.GracePeriod)
        {
            var expiresAt = business.TrialEndsAt!.Value.AddDays(GracePeriodDays);
            var hoursLeft = Math.Max(1, (int)(expiresAt - DateTime.UtcNow).TotalHours);
            return $"Your {GetPlanLabel(business.Plan)} free trial has ended. You have {hoursLeft} hours left before your account is restricted. Subscribe at app.bizpilot-ai.com/settings";
        }
        return null;
    }
}

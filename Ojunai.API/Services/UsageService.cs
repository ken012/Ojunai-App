using Microsoft.EntityFrameworkCore;
using Ojunai.API.Data;
using Ojunai.API.Models;

namespace Ojunai.API.Services;

/// <summary>
/// Which assistant channel an action came in on. Used to apportion action counts to the
/// right quota bucket (WhatsApp pack vs Telegram + Messenger plan pool).
/// </summary>
public enum AssistantChannel
{
    WhatsApp = 1,
    Telegram = 2,
    Messenger = 3,
    /// <summary>Dashboard-originated actions (chat box on the web). Counted in Messaging pool
    /// since there's no separate dashboard meter today.</summary>
    Dashboard = 4,
}

public interface IUsageService
{
    /// <summary>Record one inbound assistant action against the business's quota.
    /// Bumps both the total <see cref="ActionUsage.Count"/> and the channel-specific
    /// sub-counter in a single round-trip via INSERT…ON CONFLICT.</summary>
    Task RecordActionAsync(Guid businessId, AssistantChannel channel, CancellationToken ct = default);

    /// <summary>Read the current period's quota snapshot for a business.</summary>
    Task<QuotaSnapshot> GetSnapshotAsync(Guid businessId, CancellationToken ct = default);
}

/// <summary>
/// Caps come from this static table — easier to tune than schema-stored values and read by
/// the quota endpoint at every call (no caching → always reflects the latest code-time edit).
///
/// Plan names are the LEGACY scheme (starter/shop/pro/business). The pricing rename to
/// Starter/Lite/Operator/Pro/Scale is a separate effort; until then we look up by legacy name
/// and translate at the display layer if needed.
///
/// -1 means unlimited (Scale-tier Telegram+Messenger, Unlimited WhatsApp pack).
/// 0 means "not included on this plan" (Free tier WhatsApp — must buy a pack).
/// </summary>
public static class QuotaLimits
{
    /// <summary>Plan → monthly Telegram + Messenger combined cap. Keyed by lowercase plan name.</summary>
    public static readonly Dictionary<string, int> MessagingByPlan = new(StringComparer.OrdinalIgnoreCase)
    {
        // Free tier: 15 actions TOTAL across all channels (capped here on Messaging side; WhatsApp pack = 0).
        ["starter"] = 15,
        ["shop"] = 500,        // Lite
        ["pro"] = 1500,        // Operator
        ["business"] = 4000,   // Pro
        // Future: ["scale"] = -1
    };

    /// <summary>WhatsApp pack name → monthly cap. Keyed by lowercase pack name.</summary>
    public static readonly Dictionary<string, int> WhatsAppByPack = new(StringComparer.OrdinalIgnoreCase)
    {
        ["none"] = 0,          // Free tier or no pack selected
        ["start"] = 100,
        ["grow"] = 300,
        ["pro"] = 800,
        ["scale"] = 2000,
        ["unlimited"] = -1,
    };
}

public record QuotaChannel(int Used, int Cap, double PercentUsed, string Label, bool IsUnlimited);

public record QuotaSnapshot(
    QuotaChannel WhatsApp,
    QuotaChannel Messaging,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    string PlanName,
    string WhatsAppPackName);

public class UsageService : IUsageService
{
    private readonly AppDbContext _db;

    public UsageService(AppDbContext db) => _db = db;

    public async Task RecordActionAsync(Guid businessId, AssistantChannel channel, CancellationToken ct = default)
    {
        var period = MonthStartUtc(DateTime.UtcNow);

        // Single SQL upsert keeps the increment atomic — multiple workers handling messages
        // for the same business in the same minute don't race on the counter.
        // Postgres-only: relies on jsonb-style ON CONFLICT.
        var whatsAppDelta = channel == AssistantChannel.WhatsApp ? 1 : 0;
        var messagingDelta = channel == AssistantChannel.WhatsApp ? 0 : 1;
        var now = DateTime.UtcNow;

        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ""ActionUsages"" (""BusinessId"", ""ProductLine"", ""PeriodStartUtc"",
                ""Count"", ""WhatsAppCount"", ""MessagingCount"", ""LastIncrementedAtUtc"")
            VALUES ({businessId}, {(int)ProductLine.Dashboard}, {period},
                1, {whatsAppDelta}, {messagingDelta}, {now})
            ON CONFLICT (""BusinessId"", ""ProductLine"", ""PeriodStartUtc"")
            DO UPDATE SET
                ""Count"" = ""ActionUsages"".""Count"" + 1,
                ""WhatsAppCount"" = ""ActionUsages"".""WhatsAppCount"" + {whatsAppDelta},
                ""MessagingCount"" = ""ActionUsages"".""MessagingCount"" + {messagingDelta},
                ""LastIncrementedAtUtc"" = {now};
        ", ct);
    }

    public async Task<QuotaSnapshot> GetSnapshotAsync(Guid businessId, CancellationToken ct = default)
    {
        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId, ct)
            ?? throw new KeyNotFoundException("Business not found.");

        var period = MonthStartUtc(DateTime.UtcNow);
        var periodEnd = period.AddMonths(1);

        var row = await _db.ActionUsages.FirstOrDefaultAsync(
            u => u.BusinessId == businessId
              && u.ProductLine == ProductLine.Dashboard
              && u.PeriodStartUtc == period,
            ct);

        var planName = (business.Plan ?? "starter").ToLowerInvariant();
        var packName = "none"; // Phase 2 will read this from a real WhatsApp pack subscription row.

        var whatsAppCap = QuotaLimits.WhatsAppByPack.GetValueOrDefault(packName, 0);
        var messagingCap = QuotaLimits.MessagingByPlan.GetValueOrDefault(planName, 0);

        var whatsAppUsed = row?.WhatsAppCount ?? 0;
        var messagingUsed = row?.MessagingCount ?? 0;

        return new QuotaSnapshot(
            WhatsApp: Build(whatsAppUsed, whatsAppCap, "WhatsApp"),
            Messaging: Build(messagingUsed, messagingCap, "Telegram + Messenger"),
            PeriodStartUtc: period,
            PeriodEndUtc: periodEnd,
            PlanName: planName,
            WhatsAppPackName: packName);
    }

    private static QuotaChannel Build(int used, int cap, string label)
    {
        if (cap < 0) return new QuotaChannel(used, -1, 0, label, IsUnlimited: true);
        if (cap == 0) return new QuotaChannel(used, 0, 100, label, IsUnlimited: false);
        var pct = Math.Min(100.0, Math.Round((double)used / cap * 100, 1));
        return new QuotaChannel(used, cap, pct, label, IsUnlimited: false);
    }

    private static DateTime MonthStartUtc(DateTime t) =>
        new DateTime(t.Year, t.Month, 1, 0, 0, 0, DateTimeKind.Utc);
}

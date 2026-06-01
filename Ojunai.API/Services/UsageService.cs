using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Ojunai.API.Common;
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

    /// <summary>Decide what the WhatsApp bot should do for the next inbound message from this
    /// business — allow it, allow with a one-time warning attached, or block. Caller is
    /// responsible for sending the returned <see cref="WhatsAppGateResult.Message"/> (gated
    /// by <see cref="MarkNotifiedIfNewDay"/> so the same warning isn't sent multiple times
    /// per day).</summary>
    Task<WhatsAppGateResult> GetWhatsAppGateAsync(Guid businessId, CancellationToken ct = default);

    /// <summary>Returns true if this business hasn't been notified of <paramref name="kind"/>
    /// today AND atomically claims the notification slot for the rest of the day. Use this to
    /// decide whether to actually deliver the gate's warning/block message to the user — it
    /// keeps a warning from firing on every inbound message after the threshold is crossed.</summary>
    bool MarkNotifiedIfNewDay(Guid businessId, WhatsAppGateNotificationKind kind);
}

/// <summary>Possible outcomes when the WhatsApp bot checks the gate before processing an inbound message.</summary>
public enum WhatsAppGate
{
    /// <summary>Below all thresholds. Process normally; no warning needed.</summary>
    Allow,
    /// <summary>≥80% of cap (free tier or paid). Process, but send a one-shot warning today.</summary>
    WarnApproaching,
    /// <summary>Over the paid pack's cap but within the 5-action grace. Process, but warn.</summary>
    WarnGrace,
    /// <summary>Free tier exhausted (≥15) or paid pack + grace exhausted. Reject the message.</summary>
    Block,
}

/// <summary>Categories of notifications the WhatsApp gate can emit — one-per-day cap per kind per business.</summary>
public enum WhatsAppGateNotificationKind { Approaching, Grace, Block }

public record WhatsAppGateResult(WhatsAppGate State, string? Message, WhatsAppGateNotificationKind? NotificationKind);

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
    /// <summary>Plan → monthly Telegram + Messenger combined cap. Keyed by lowercase plan name.
    /// WhatsApp pack caps live in <see cref="BillingConfig.WhatsAppPackActions"/> — pack metadata
    /// belongs with pack pricing as the single source of truth.</summary>
    public static readonly Dictionary<string, int> MessagingByPlan = new(StringComparer.OrdinalIgnoreCase)
    {
        // Free tier: 15 actions TOTAL across all channels (capped here on Messaging side; WhatsApp pack = 0).
        ["starter"] = 15,
        ["shop"] = 500,        // Lite
        ["pro"] = 1500,        // Operator
        ["business"] = 4000,   // Pro
        // Future: ["scale"] = -1
    };

    // ── WhatsApp paywall tuning ────────────────────────────────────────────
    // The numbers below drive both the meter (cap displayed when no pack is active) and the
    // gate (what the bot does when caps are crossed). Free-taste = 15 actions/mo with no
    // grace; paid pack = cap + 5 grace actions then blocked.

    /// <summary>Free WhatsApp taste-test cap for businesses without an active pack.</summary>
    public const int WhatsAppFreeTasteActions = 15;

    /// <summary>Grace allowance after a paid pack's cap is exhausted — soft buffer before
    /// hard block, lets a merchant cross the line mid-conversation without going dark.</summary>
    public const int WhatsAppPackGraceActions = 5;

    /// <summary>Send the "approaching cap" warning at this fraction of cap (free or pack).</summary>
    public const double WhatsAppApproachingThreshold = 0.80;
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

    /// <summary>
    /// Process-local "last notified" tracker. Keyed by (businessId, kind). Value is the UTC
    /// calendar date the last notification was sent. <see cref="MarkNotifiedIfNewDay"/>
    /// atomically compares + swaps so two concurrent inbound messages can't both send the
    /// warning. Multi-instance deployments may double-send across instances; acceptable.
    /// </summary>
    private static readonly ConcurrentDictionary<(Guid businessId, WhatsAppGateNotificationKind kind), DateOnly> _lastNotified = new();

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

        // Look up the business's active WhatsApp pack from BusinessAddOn. Codes are
        // "whatsapp_pack.<size>" — we strip the prefix and use the bare size to drive the cap.
        // Multiple pack rows shouldn't co-exist; if they do (data drift), the most recent wins.
        var packCode = await _db.BusinessAddOns
            .Where(a => a.BusinessId == businessId
                && a.Status == "active"
                && a.AddOnCode.StartsWith("whatsapp_pack."))
            .OrderByDescending(a => a.UpdatedAtUtc)
            .Select(a => a.AddOnCode)
            .FirstOrDefaultAsync(ct);

        var packName = packCode?["whatsapp_pack.".Length..].ToLowerInvariant() ?? "none";

        // Pull WhatsApp cap from the BillingConfig catalog (single source of truth for pack
        // metadata). Fall back to 0 if the active pack code isn't in the catalog — i.e. a
        // stale row from a deprecated SKU. Treat as "no pack" rather than crashing the meter.
        // When the business has NO pack at all ("none"), surface the free taste-test cap
        // (15) so the meter shows "X / 15 free" rather than "X / 0 — not included". The gate
        // honors the same number.
        var whatsAppCap = packName == "none"
            ? QuotaLimits.WhatsAppFreeTasteActions
            : BillingConfig.WhatsAppPackActions.GetValueOrDefault(packName, 0);
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

    public async Task<WhatsAppGateResult> GetWhatsAppGateAsync(Guid businessId, CancellationToken ct = default)
    {
        var snapshot = await GetSnapshotAsync(businessId, ct);

        // Unlimited pack — never gate.
        if (snapshot.WhatsApp.IsUnlimited)
            return new WhatsAppGateResult(WhatsAppGate.Allow, null, null);

        var used = snapshot.WhatsApp.Used;
        var cap = snapshot.WhatsApp.Cap;
        var hasNoPack = string.Equals(snapshot.WhatsAppPackName, "none", StringComparison.OrdinalIgnoreCase);
        var approachingAt = (int)Math.Ceiling(cap * QuotaLimits.WhatsAppApproachingThreshold);

        // ── Free taste-test path ───────────────────────────────────────────
        if (hasNoPack)
        {
            if (used >= QuotaLimits.WhatsAppFreeTasteActions)
            {
                return new WhatsAppGateResult(
                    WhatsAppGate.Block,
                    "WhatsApp is a paid channel. Add a pack at https://app.ojunai.com/settings#plan to continue messaging.",
                    WhatsAppGateNotificationKind.Block);
            }
            if (used >= approachingAt)
            {
                return new WhatsAppGateResult(
                    WhatsAppGate.WarnApproaching,
                    $"⚠️ You're using your free WhatsApp taste-test ({used}/{cap} actions). " +
                    "Add a pack at https://app.ojunai.com/settings#plan to keep messaging.",
                    WhatsAppGateNotificationKind.Approaching);
            }
            return new WhatsAppGateResult(WhatsAppGate.Allow, null, null);
        }

        // ── Paid pack path ────────────────────────────────────────────────
        var graceLimit = cap + QuotaLimits.WhatsAppPackGraceActions;
        if (used >= graceLimit)
        {
            return new WhatsAppGateResult(
                WhatsAppGate.Block,
                $"You've used your WhatsApp pack plus the {QuotaLimits.WhatsAppPackGraceActions}-message grace. " +
                "Upgrade your pack or buy a top-up at https://app.ojunai.com/settings#plan.",
                WhatsAppGateNotificationKind.Block);
        }
        if (used >= cap)
        {
            var graceUsed = used - cap + 1;
            return new WhatsAppGateResult(
                WhatsAppGate.WarnGrace,
                $"⚠️ Over your WhatsApp pack — grace message {graceUsed}/{QuotaLimits.WhatsAppPackGraceActions}. " +
                "Upgrade at https://app.ojunai.com/settings#plan to avoid interruption.",
                WhatsAppGateNotificationKind.Grace);
        }
        if (used >= approachingAt)
        {
            var pct = Math.Round((double)used / cap * 100);
            return new WhatsAppGateResult(
                WhatsAppGate.WarnApproaching,
                $"⚠️ You're at {pct}% of your WhatsApp {snapshot.WhatsAppPackName} pack ({used}/{cap}). " +
                "Top up or upgrade at https://app.ojunai.com/settings#plan.",
                WhatsAppGateNotificationKind.Approaching);
        }
        return new WhatsAppGateResult(WhatsAppGate.Allow, null, null);
    }

    public bool MarkNotifiedIfNewDay(Guid businessId, WhatsAppGateNotificationKind kind)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var key = (businessId, kind);

        // Compare-and-swap loop: return true once if and only if this call is the FIRST one for
        // (businessId, kind) today. Concurrent callers on the same business+kind in the same
        // millisecond all retry through this loop; exactly one wins.
        while (true)
        {
            if (_lastNotified.TryGetValue(key, out var existing))
            {
                if (existing >= today) return false; // already claimed today
                if (_lastNotified.TryUpdate(key, today, existing)) return true; // we won the swap
                // Lost the race — retry.
            }
            else
            {
                if (_lastNotified.TryAdd(key, today)) return true; // first ever for this key
                // Lost the race — retry.
            }
        }
    }

    private static DateTime MonthStartUtc(DateTime t) =>
        new DateTime(t.Year, t.Month, 1, 0, 0, 0, DateTimeKind.Utc);
}

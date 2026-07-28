using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Models.Messaging;
using Ojunai.API.Services;
using Ojunai.API.Services.Channels;
using Ojunai.API.Services.Interfaces;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Jobs;

/// <summary>
/// Opt-in morning "act on this today" nudge. Once a day, at ~8 AM in each business's local timezone, sends
/// the owner (on their chosen alert channel) a short, ACTIONABLE message: products about to run out with a
/// suggested reorder quantity, plus the most overdue customer debt. Reuses the existing report engines
/// (stockout predictions + outstanding-debt summary) and the channel-agnostic NotificationDispatcher, so
/// an owner who has opted into Telegram/Messenger for alerts gets the nudge there instead of WhatsApp.
///
/// Deliberately quiet: gated on the per-business <see cref="Business.AlertDailyNudges"/> opt-in AND the owner
/// having selected an alert channel; deduped once per LOCAL day (<see cref="Business.LastDailyNudgeOn"/>) so a
/// retried hourly tick can't double-send; skipped entirely when nothing is actionable; and — for WhatsApp —
/// suppressed when the pack gate is Blocked (never burn an out-of-quota WhatsApp session on a proactive push).
/// One hourly tick, self-filtered by local hour, mirroring <see cref="SummaryJobService"/>.
/// </summary>
public class DailyNudgeJobService
{
    private readonly AppDbContext _db;
    private readonly IReportService _reports;
    private readonly INotificationDispatcher _dispatcher;
    private readonly IUsageService _usage;
    private readonly ILogger<DailyNudgeJobService> _logger;

    // Only nudge about products projected to run out within this many days (keeps it "act now", not "someday").
    private const int StockoutHorizonDays = 14;
    // Local hour to deliver — morning, so the owner can act during the trading day.
    private const int SendHourLocal = 8;

    public DailyNudgeJobService(
        AppDbContext db,
        IReportService reports,
        INotificationDispatcher dispatcher,
        IUsageService usage,
        ILogger<DailyNudgeJobService> logger)
    {
        _db = db;
        _reports = reports;
        _dispatcher = dispatcher;
        _usage = usage;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync()
    {
        // Only businesses that opted in — keeps the hourly fan-out small.
        var businesses = await _db.Businesses
            .Include(b => b.Users)
            .Where(b => b.IsActive && b.AlertDailyNudges)
            .ToListAsync();

        foreach (var business in businesses)
        {
            try
            {
                await SendForBusinessAsync(business);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Daily nudge failed for business {BusinessId}", business.Id);
            }
        }
    }

    private async Task SendForBusinessAsync(Business business)
    {
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(business.Timezone ?? "Africa/Lagos"); }
        catch { tz = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos"); }

        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        if (localNow.Hour != SendHourLocal) return; // deliver once, in the morning local time

        var localToday = DateOnly.FromDateTime(localNow);
        if (business.LastDailyNudgeOn == localToday) return; // already nudged today

        var owner = business.Users.FirstOrDefault(u => u.Role == UserRole.Owner && u.IsActive);
        if (owner == null || AlertChannels.IsNone(owner.AlertChannel)) return; // opt-in requires a chosen channel

        // For WhatsApp delivery, don't push to an out-of-quota business — a proactive nudge is a real Twilio
        // session. Telegram/Messenger have their own cheaper quota and aren't gated here (matches the summary job).
        if (AlertChannels.WhatsApp.Equals(owner.AlertChannel, StringComparison.OrdinalIgnoreCase))
        {
            var gate = await _usage.GetWhatsAppGateAsync(business.Id);
            if (gate.State == WhatsAppGate.Block) return;
        }

        var message = await BuildNudgeAsync(business);
        if (message == null) return; // nothing actionable — stay silent

        // Claim the day BEFORE sending: persist the once-per-day marker first, then send. SendToUserAsync is
        // best-effort and never throws, so once the claim commits the send is virtually guaranteed to at least
        // be attempted, and the day is deduped. If the claim save itself fails, the per-business catch swallows
        // it and NOTHING was sent, so a later tick retries cleanly. The reverse order (send, then mark) would
        // re-send within the same 8 AM local hour if the marker save failed after the message already went out.
        business.LastDailyNudgeOn = localToday;
        await _db.SaveChangesAsync();

        await _dispatcher.SendToUserAsync(owner.Id, new ReplyComposition { Text = message });
    }

    /// <summary>Builds the nudge body, or null when there's nothing worth interrupting the owner about.</summary>
    private async Task<string?> BuildNudgeAsync(Business business)
    {
        var stockouts = await _reports.GetStockoutPredictionsAsync(business.Id);
        var debt = await _reports.GetOutstandingDebtSummaryAsync(business.Id);
        return FormatNudge(BillingConfig.Symbol(business.Currency), stockouts, debt);
    }

    /// <summary>
    /// Pure formatter (no I/O) so the content rules are unit-testable: soonest-stockouts-first with a reorder
    /// quantity, the single most-overdue debtor (7+ days), and null when there's nothing actionable.
    /// </summary>
    internal static string? FormatNudge(
        string currencySymbol,
        IReadOnlyList<DTOs.Reports.StockoutPredictionDto> stockouts,
        DTOs.Reports.OutstandingDebtSummaryDto debt)
    {
        var lines = new List<string>();

        // Products about to stock out — the highest-value nudge (a stockout is lost sales). Soonest first,
        // with the report's suggested restock quantity so the owner can act from this one message.
        foreach (var s in stockouts
                     .Where(s => s.DaysLeft <= StockoutHorizonDays)
                     .OrderBy(s => s.DaysLeft)
                     .Take(3))
        {
            var days = (int)Math.Floor(s.DaysLeft);
            var when = days <= 0 ? "out of stock now"
                     : days == 1 ? "~1 day of stock left"
                     : $"~{days} days of stock left";
            var reorder = s.RestockQty > 0
                ? $" — reorder {s.RestockQty:0.##} {UnitFormat.Plural(s.RestockQty, s.Unit)}"
                : "";
            lines.Add($"🔴 *{s.ProductName}*: {when}{reorder}");
        }

        // Most overdue customer debt (7+ days) — a chase-your-money prompt.
        var overdue = debt.TopReceivables
            .Where(r => r.DaysOld >= 7)
            .OrderByDescending(r => r.DaysOld)
            .FirstOrDefault();
        if (overdue != null)
            lines.Add($"💰 *{overdue.ContactName}* owes {currencySymbol}{overdue.Amount:N0} — {overdue.DaysOld} days overdue");

        if (lines.Count == 0) return null;

        return "☀️ *Good morning — a few things to act on today:*\n" +
               string.Join("\n", lines) +
               "\n\nReply here to record a sale, restock, or ask me anything.";
    }
}

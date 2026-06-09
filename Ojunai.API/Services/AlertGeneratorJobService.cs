using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services;

/// <summary>
/// Hangfire job that scans every active business and emits scheduled dashboard alerts:
///   - Aged receivable (any contact owed money for ≥30 days)
///   - Trial ending (7 / 3 / 1 days left)
///   - Daily summary (yesterday's revenue, expenses, net) — fires once per local-day
///
/// Runs hourly. Each per-business check is idempotent + dedup'd, so running every hour
/// just gives us a tighter delivery window. Daily-summary fires once a day at the local
/// 8 AM hour for each business.
/// </summary>
public class AlertGeneratorJobService
{
    private readonly AppDbContext _db;
    private readonly IAlertService _alerts;
    private readonly ILogger<AlertGeneratorJobService> _logger;

    public AlertGeneratorJobService(AppDbContext db, IAlertService alerts, ILogger<AlertGeneratorJobService> logger)
    {
        _db = db;
        _alerts = alerts;
        _logger = logger;
    }

    // Serialize runs so an overrunning hourly sweep (aged-receivable scan + alert fan-out) can't
    // overlap itself and double the DB load.
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync()
    {
        var businesses = await _db.Businesses
            .AsNoTracking()
            .Where(b => b.IsActive)
            .ToListAsync();

        foreach (var b in businesses)
        {
            try { await ProcessBusinessAsync(b); }
            catch (Exception ex) { _logger.LogError(ex, "Scheduled alerts failed for business {Business}", b.Id); }
        }
    }

    private async Task ProcessBusinessAsync(Business b)
    {
        var nowUtc = DateTime.UtcNow;
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(b.Timezone); }
        catch (TimeZoneNotFoundException) { tz = TimeZoneInfo.Utc; }
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);

        // ── 1. Trial ending: fires once a day per threshold (7, 3, 1) ──
        if (b.TrialEndsAt.HasValue && b.TrialEndsAt.Value > nowUtc)
        {
            var daysLeft = (int)Math.Ceiling((b.TrialEndsAt.Value - nowUtc).TotalDays);
            if (daysLeft is 7 or 3 or 1)
            {
                await _alerts.CreateAsync(
                    b.Id, userId: null,
                    type: AlertType.TrialEnding,
                    severity: daysLeft == 1 ? AlertSeverity.Critical : AlertSeverity.Warning,
                    title: $"Trial ends in {daysLeft} day{(daysLeft == 1 ? "" : "s")}",
                    body: $"Your free trial expires {b.TrialEndsAt.Value.ToLocalTime():MMMM d}. Upgrade now to keep using Ojunai without interruption.",
                    linkUrl: "/settings#plan",
                    dedupeKey: $"trial-ending:{b.Id}:{daysLeft}");
            }
        }

        // ── 2. Aged receivable: any contact whose oldest unpaid receivable is ≥30 days old ──
        if (b.AlertDashboardAgedReceivable)
        {
            var thirtyDaysAgo = nowUtc.AddDays(-30);

            // Aggregate receivable balance per contact IN SQL (GROUP BY ContactId) so we fetch one
            // row per contact instead of pulling every receivable/payment entry for the business into
            // memory. Net outstanding = SUM(receivable) - SUM(payment), oldest = MIN(receivable date).
            // Filter to "overdue + still owing" in memory on the small per-contact result (avoids a
            // HAVING-clause translation dependency).
            var byContact = (await _db.LedgerEntries
                .Where(e => e.BusinessId == b.Id
                    && (e.EntryType == LedgerEntryType.Receivable || e.EntryType == LedgerEntryType.ReceivablePayment))
                .GroupBy(e => new { e.ContactId, e.Contact.Name })
                .Select(g => new
                {
                    g.Key.ContactId,
                    ContactName = g.Key.Name,
                    Outstanding = g.Sum(e => e.EntryType == LedgerEntryType.Receivable ? e.Amount : -e.Amount),
                    OldestReceivableUtc = g.Min(e => e.EntryType == LedgerEntryType.Receivable ? (DateTime?)e.CreatedAtUtc : null)
                })
                .ToListAsync())
                .Where(x => x.Outstanding > 0
                    && x.OldestReceivableUtc != null
                    && x.OldestReceivableUtc <= thirtyDaysAgo)
                .ToList();

            var cs = BillingConfig.Symbol(b.Currency);
            foreach (var row in byContact)
            {
                var daysOld = (int)Math.Floor((nowUtc - row.OldestReceivableUtc!.Value).TotalDays);
                await _alerts.CreateAsync(
                    b.Id, userId: null,
                    type: AlertType.AgedReceivable,
                    severity: AlertSeverity.Warning,
                    title: $"{row.ContactName ?? "Customer"} has owed for {daysOld} days",
                    body: $"Outstanding balance: {cs}{row.Outstanding:N0}. Send a friendly reminder?",
                    linkUrl: $"/contacts?id={row.ContactId}",
                    dedupeKey: $"aged-receivable:{row.ContactId}:{nowUtc:yyyyMMdd}");
            }
        }

        // ── 3. Daily summary: once per local day, emitted at the local 8 AM hour ──
        if (b.AlertDashboardDailySummary && localNow.Hour == 8)
        {
            var localToday = localNow.Date;
            var localYesterday = localToday.AddDays(-1);
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localYesterday, DateTimeKind.Unspecified), tz);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localToday, DateTimeKind.Unspecified), tz);

            var revenue = await _db.Sales
                .Where(s => s.BusinessId == b.Id && s.CreatedAtUtc >= startUtc && s.CreatedAtUtc < endUtc && !s.IsDeleted)
                .SumAsync(s => (decimal?)s.TotalAmount) ?? 0m;
            var expenses = await _db.Expenses
                .Where(e => e.BusinessId == b.Id && e.CreatedAtUtc >= startUtc && e.CreatedAtUtc < endUtc)
                .SumAsync(e => (decimal?)e.Amount) ?? 0m;
            var net = revenue - expenses;

            var cs = BillingConfig.Symbol(b.Currency);
            await _alerts.CreateAsync(
                b.Id, userId: null,
                type: AlertType.DailySummary,
                severity: AlertSeverity.Info,
                title: $"Yesterday's summary",
                body: $"Revenue {cs}{revenue:N0} · Expenses {cs}{expenses:N0} · Net {cs}{net:N0}.",
                linkUrl: "/reports",
                dedupeKey: $"daily-summary:{b.Id}:{localYesterday:yyyyMMdd}");
        }
    }
}

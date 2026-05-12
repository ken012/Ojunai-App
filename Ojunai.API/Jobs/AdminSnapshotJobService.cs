using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Models.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Jobs;

/// <summary>
/// Daily snapshot job. Runs at 00:05 UTC and writes one row per metric (plus per-channel
/// breakdowns for the ones that have them) into AdminMetricSnapshots. The endpoint
/// /api/admin/metrics/snapshots reads from this table to build trend charts that the live-
/// computing endpoints can't produce.
///
/// Idempotency: each (MetricName, ChannelFilter, CapturedDate) is constrained UNIQUE in the
/// schema. If the job runs twice on the same day (manual re-trigger), the second insert is
/// caught and skipped.
/// </summary>
public sealed class AdminSnapshotJobService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AdminSnapshotJobService> _logger;

    public AdminSnapshotJobService(AppDbContext db, ILogger<AdminSnapshotJobService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task RunDailyAsync()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTime.UtcNow;

        // Cross-channel totals first
        await SnapshotAsync(date, "dau", null, await CountActiveBusinessesAsync(now.AddDays(-1), null));
        await SnapshotAsync(date, "wau", null, await CountActiveBusinessesAsync(now.AddDays(-7), null));
        await SnapshotAsync(date, "mau", null, await CountActiveBusinessesAsync(now.AddDays(-30), null));

        await SnapshotAsync(date, "total_businesses", null,
            await _db.Businesses.CountAsync(b => b.IsActive));
        await SnapshotAsync(date, "total_users", null,
            await _db.Users.CountAsync(u => u.IsActive));
        await SnapshotAsync(date, "new_signups_24h", null,
            await _db.Businesses.CountAsync(b => b.CreatedAtUtc >= now.AddDays(-1) && b.IsActive));

        // Paid conversion + churn snapshots
        await SnapshotAsync(date, "paid_businesses", null,
            await _db.Businesses.CountAsync(b => b.IsActive && b.SubscribedPlan != null && b.SubscribedPlan != "starter"));
        await SnapshotAsync(date, "churn_events_7d", null,
            await _db.BillingEvents.CountAsync(e => e.CreatedAtUtc >= now.AddDays(-7)
                && (e.EventType == "subscription.expired" || e.EventType == "subscription.cancelled")));
        await SnapshotAsync(date, "failed_payments_24h", null,
            await _db.BillingEvents.CountAsync(e => e.CreatedAtUtc >= now.AddDays(-1)
                && (e.EventType == "payment.failed" || e.EventType == "payment.rejected")));

        // Misparse rate — last 7 days, percentage
        await SnapshotAsync(date, "misparse_rate_7d", null, await ComputeMisparseRateAsync(now.AddDays(-7), null));

        // Per-channel DAU + message volume + misparse — drives the per-channel trend charts
        foreach (var channel in new[] { "WhatsApp", "Telegram", "Messenger" })
        {
            await SnapshotAsync(date, "dau", channel, await CountActiveBusinessesAsync(now.AddDays(-1), channel));
            await SnapshotAsync(date, "messages_24h", channel, await CountInboundMessagesAsync(now.AddDays(-1), channel));
            await SnapshotAsync(date, "misparse_rate_7d", channel, await ComputeMisparseRateAsync(now.AddDays(-7), channel));
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("AdminSnapshotJob: captured snapshots for {Date}", date);
    }

    private async Task<int> CountActiveBusinessesAsync(DateTime since, string? channel)
    {
        var q = _db.MessageLogs.Where(m =>
            m.CreatedAtUtc >= since
            && m.Direction == MessageDirection.Inbound
            && m.BusinessId.HasValue);
        if (channel != null) q = q.Where(m => m.Channel == channel);
        return await q.Select(m => m.BusinessId).Distinct().CountAsync();
    }

    private async Task<int> CountInboundMessagesAsync(DateTime since, string? channel)
    {
        var q = _db.MessageLogs.Where(m =>
            m.CreatedAtUtc >= since && m.Direction == MessageDirection.Inbound);
        if (channel != null) q = q.Where(m => m.Channel == channel);
        return await q.CountAsync();
    }

    private async Task<decimal> ComputeMisparseRateAsync(DateTime since, string? channel)
    {
        // Same definition as /admin/telemetry/misparse-rate: percentage of inbound messages whose
        // ProcessingStatus is NeedsClarification or Failed.
        var q = _db.MessageLogs.Where(m =>
            m.CreatedAtUtc >= since && m.Direction == MessageDirection.Inbound && m.BusinessId != null);
        if (channel != null) q = q.Where(m => m.Channel == channel);

        var totals = await q
            .GroupBy(m => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Problems = g.Count(x => x.ProcessingStatus == MessageProcessingStatus.NeedsClarification
                                     || x.ProcessingStatus == MessageProcessingStatus.Failed),
            })
            .FirstOrDefaultAsync();
        if (totals == null || totals.Total == 0) return 0m;
        return Math.Round((decimal)totals.Problems / totals.Total * 100m, 2);
    }

    private async Task SnapshotAsync(DateOnly date, string metric, string? channel, int value)
        => await SnapshotAsync(date, metric, channel, (decimal)value);

    private async Task SnapshotAsync(DateOnly date, string metric, string? channel, decimal value)
    {
        // Idempotent — skip if a row already exists for (metric, channel, date). Lets us re-run
        // the job safely without exploding on the unique constraint.
        var exists = await _db.AdminMetricSnapshots.AnyAsync(s =>
            s.MetricName == metric && s.ChannelFilter == channel && s.CapturedDate == date);
        if (exists) return;

        _db.AdminMetricSnapshots.Add(new AdminMetricSnapshot
        {
            MetricName = metric,
            ChannelFilter = channel,
            CapturedDate = date,
            Value = value,
        });
    }
}

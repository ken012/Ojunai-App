using System.Net.Http.Json;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Models.Messaging;
using Ojunai.API.Services;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Jobs;

/// <summary>
/// Periodic check that computes a few critical metrics and fires an alert if any breach
/// configured thresholds. Runs every 15 minutes. Alerts go to a Slack webhook and/or an admin
/// email; both are optional via config.
///
/// Dedup: each (metricName, day-bucket) is tracked in-memory so the same alert doesn't fire
/// every 15 min for an hour. Crossing back below the threshold + crossing back above clears
/// the dedup so a re-spike triggers a new alert.
///
/// Thresholds are env-driven so ops can adjust without redeploying:
///   Admin:Alerts:SlackWebhook
///   Admin:Alerts:Email
///   Admin:Alerts:MisparseRatePct       — fire if >X (default 5)
///   Admin:Alerts:FailedPayments24h     — fire if >X (default 5)
///   Admin:Alerts:VoiceAIFailureRatePct — fire if >X (default 20)
/// </summary>
public sealed class AdminAlertJobService
{
    // Process-local dedup. Hangfire workers can run in multiple processes — for our scale a
    // duplicate alert from a different worker is acceptable noise. If it becomes a problem we
    // move the dedup state into a DB table keyed by (metric, hour).
    private static readonly Dictionary<string, DateTime> _lastFired = new();
    private static readonly object _lock = new();
    private static readonly TimeSpan DedupWindow = TimeSpan.FromHours(1);

    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IEmailService _email;
    private readonly ILogger<AdminAlertJobService> _logger;

    public AdminAlertJobService(
        AppDbContext db,
        IConfiguration config,
        IHttpClientFactory httpFactory,
        IEmailService email,
        ILogger<AdminAlertJobService> logger)
    {
        _db = db;
        _config = config;
        _httpFactory = httpFactory;
        _email = email;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        var slackWebhook = _config["Admin:Alerts:SlackWebhook"];
        var adminEmail = _config["Admin:Alerts:Email"];
        if (string.IsNullOrEmpty(slackWebhook) && string.IsNullOrEmpty(adminEmail))
        {
            // No delivery target configured → don't bother computing. Saves DB cycles on
            // installations that haven't opted into alerts.
            return;
        }

        var now = DateTime.UtcNow;

        // ── Misparse rate (last 7 days) ──
        var misparseThreshold = _config.GetValue<double>("Admin:Alerts:MisparseRatePct", 5.0);
        var misparseRate = await ComputeMisparseRateAsync(now.AddDays(-7));
        if (misparseRate > misparseThreshold && ShouldFire("misparse_rate"))
        {
            await FireAsync(slackWebhook, adminEmail,
                $"⚠️ Misparse rate is {misparseRate:F2}% (threshold {misparseThreshold:F2}%)",
                "The bot is misunderstanding more user messages than usual. Check /admin/telemetry " +
                "and inspect the top failure clusters — a recent deploy may have broken prompt " +
                "classification or a usage pattern shifted.");
        }

        // ── Failed payments in last 24h ──
        var failedThreshold = _config.GetValue<int>("Admin:Alerts:FailedPayments24h", 5);
        var failedCount = await _db.BillingEvents.CountAsync(e =>
            e.CreatedAtUtc >= now.AddDays(-1)
            && (e.EventType == "payment.failed" || e.EventType == "payment.rejected"));
        if (failedCount > failedThreshold && ShouldFire("failed_payments"))
        {
            await FireAsync(slackWebhook, adminEmail,
                $"💳 {failedCount} payment failures in the last 24h (threshold {failedThreshold})",
                "Multiple Paystack failures recently. Check /admin/metrics/failed-payments to see " +
                "which businesses are affected — typically card issuer issues that warrant outbound " +
                "support to reduce churn.");
        }

        // ── Voice AI failure rate ──
        var voiceThreshold = _config.GetValue<double>("Admin:Alerts:VoiceAIFailureRatePct", 20.0);
        var voiceTotals = await _db.MessageLogs
            .Where(m => m.CreatedAtUtc >= now.AddDays(-1)
                && m.Direction == MessageDirection.Inbound
                && m.Channel == "Voice")
            .GroupBy(m => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Failed = g.Count(x => x.ProcessingStatus == MessageProcessingStatus.Failed),
            })
            .FirstOrDefaultAsync();
        if (voiceTotals != null && voiceTotals.Total > 5)  // ignore noise from very low volume
        {
            var rate = (double)voiceTotals.Failed / voiceTotals.Total * 100.0;
            if (rate > voiceThreshold && ShouldFire("voice_failure_rate"))
            {
                await FireAsync(slackWebhook, adminEmail,
                    $"📞 Voice AI failure rate {rate:F1}% ({voiceTotals.Failed}/{voiceTotals.Total} in 24h, threshold {voiceThreshold:F1}%)",
                    "Voice AI is failing more calls than expected. Likely culprits: phone number " +
                    "provisioning issue, Whisper/transcription outage, or downstream Twilio Voice " +
                    "API problem. Check /admin/voice-ai for recent failed sessions.");
            }
        }
    }

    private static bool ShouldFire(string metric)
    {
        lock (_lock)
        {
            if (_lastFired.TryGetValue(metric, out var last) && DateTime.UtcNow - last < DedupWindow)
                return false;
            _lastFired[metric] = DateTime.UtcNow;
            return true;
        }
    }

    private async Task<double> ComputeMisparseRateAsync(DateTime since)
    {
        var totals = await _db.MessageLogs
            .Where(m => m.CreatedAtUtc >= since && m.Direction == MessageDirection.Inbound && m.BusinessId != null)
            .GroupBy(m => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Problems = g.Count(x => x.ProcessingStatus == MessageProcessingStatus.NeedsClarification
                                     || x.ProcessingStatus == MessageProcessingStatus.Failed),
            })
            .FirstOrDefaultAsync();
        if (totals == null || totals.Total == 0) return 0;
        return (double)totals.Problems / totals.Total * 100;
    }

    private async Task FireAsync(string? slackWebhook, string? adminEmail, string subject, string body)
    {
        _logger.LogWarning("ADMIN ALERT — {Subject}", subject);

        if (!string.IsNullOrEmpty(slackWebhook))
        {
            try
            {
                var client = _httpFactory.CreateClient();
                var payload = new { text = $"*{subject}*\n{body}" };
                await client.PostAsJsonAsync(slackWebhook, payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to post admin alert to Slack");
            }
        }

        if (!string.IsNullOrEmpty(adminEmail) && _email.IsConfigured)
        {
            try
            {
                await _email.SendAsync(
                    adminEmail,
                    "Ojunai Admin",
                    $"[Ojunai admin alert] {subject}",
                    $"<p><strong>{subject}</strong></p><p>{body}</p>",
                    plainBody: $"{subject}\n\n{body}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send admin alert email");
            }
        }
    }
}

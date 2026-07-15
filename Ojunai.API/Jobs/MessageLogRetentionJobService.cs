using Ojunai.API.Data;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Jobs;

/// <summary>
/// Daily cleanup that deletes MessageLogs older than the configured retention window. Default
/// is 180 days; configurable via <c>Admin:MessageLogRetentionDays</c>. Set to 0 to disable the
/// job entirely (safe default for new deploys that want to keep all history).
///
/// The table grows ~unbounded otherwise — at 10k messages/day that's 3.6M rows/year. Without a
/// retention policy the bot still works, but every aggregation gets slower as the table fills.
/// 180 days covers anything you'd realistically want for ops analytics; longer history lives
/// in AdminMetricSnapshots (daily roll-ups, much smaller).
/// </summary>
public sealed class MessageLogRetentionJobService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<MessageLogRetentionJobService> _logger;

    public MessageLogRetentionJobService(AppDbContext db, IConfiguration config, ILogger<MessageLogRetentionJobService> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task RunDailyAsync()
    {
        // Only the MessageLog sweep is disable-able via Admin:MessageLogRetentionDays=0. The
        // dedup-claim / token / audit sweeps below ALWAYS run — they are independent of it (do not
        // early-return here, or setting message-log retention to 0 would silently disable them too).
        var retentionDays = _config.GetValue<int>("Admin:MessageLogRetentionDays", 180);
        if (retentionDays > 0)
        {
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

            // ExecuteDeleteAsync issues a single DELETE — no entity tracking, fast even with millions
            // of rows. EF Core 7+ feature; we're on 8 so it's available.
            var deleted = await _db.MessageLogs
                .Where(m => m.CreatedAtUtc < cutoff)
                .ExecuteDeleteAsync();

            _logger.LogInformation(
                "MessageLogRetentionJob: deleted {Deleted} rows older than {Cutoff:yyyy-MM-dd} (retention {Days} days)",
                deleted, cutoff, retentionDays);
        }
        else
        {
            _logger.LogInformation("MessageLogRetentionJob: message-log sweep disabled (retention <= 0)");
        }

        // Inbound dedup claims only need to outlive a provider's re-delivery window (minutes to a
        // few hours) — 30 days is a generous safety margin. Purge older ones so the table stays
        // small regardless of the MessageLog retention setting above.
        var claimCutoff = DateTime.UtcNow.AddDays(-30);
        var claimsDeleted = await _db.InboundMessageClaims
            .Where(c => c.ClaimedAtUtc < claimCutoff)
            .ExecuteDeleteAsync();

        _logger.LogInformation(
            "MessageLogRetentionJob: deleted {Deleted} inbound dedup claims older than {Cutoff:yyyy-MM-dd}",
            claimsDeleted, claimCutoff);

        // Purge expired short-lived tokens. These are useless once past ExpiresAtUtc (lifetimes are
        // minutes), but the tables grow with every signup/login/channel-link attempt. Keep a 1-day
        // buffer for post-mortem debugging, then sweep. Independent of the MessageLog retention setting.
        var tokenCutoff = DateTime.UtcNow.AddDays(-1);
        var codes = await _db.PhoneVerificationCodes.Where(c => c.ExpiresAtUtc < tokenCutoff).ExecuteDeleteAsync();
        var links = await _db.ChannelLinkTokens.Where(t => t.ExpiresAtUtc < tokenCutoff).ExecuteDeleteAsync();
        var pending = await _db.PendingTelegramActions.Where(p => p.ExpiresAtUtc < tokenCutoff).ExecuteDeleteAsync();

        _logger.LogInformation(
            "MessageLogRetentionJob: purged expired tokens — phone:{Codes} channel-link:{Links} pending-telegram:{Pending}",
            codes, links, pending);

        // Activity/audit log — kept LONGER than message logs since it's the "who did what" record
        // (useful for security/compliance review). Default 365 days; Admin:ActivityLogRetentionDays=0
        // disables. Independent of the MessageLog retention setting above.
        var auditRetentionDays = _config.GetValue<int>("Admin:ActivityLogRetentionDays", 365);
        if (auditRetentionDays > 0)
        {
            var auditCutoff = DateTime.UtcNow.AddDays(-auditRetentionDays);
            var auditDeleted = await _db.ActivityLogEntries
                .Where(a => a.CreatedAtUtc < auditCutoff)
                .ExecuteDeleteAsync();
            _logger.LogInformation(
                "MessageLogRetentionJob: deleted {Deleted} activity-log entries older than {Cutoff:yyyy-MM-dd} (retention {Days} days)",
                auditDeleted, auditCutoff, auditRetentionDays);
        }
    }
}

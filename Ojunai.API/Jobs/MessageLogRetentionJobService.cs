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
        var retentionDays = _config.GetValue<int>("Admin:MessageLogRetentionDays", 180);
        if (retentionDays <= 0)
        {
            _logger.LogInformation("MessageLogRetentionJob: disabled (retention <= 0)");
            return;
        }

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
}

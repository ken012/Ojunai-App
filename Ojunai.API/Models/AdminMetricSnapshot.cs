namespace Ojunai.API.Models;

/// <summary>
/// Daily point-in-time snapshot of an admin metric. The snapshot job writes one row per
/// (MetricName, ChannelFilter) per day. Lets us draw historical trend charts ("DAU over the
/// last 90 days") which the live-computing admin endpoints can't do.
///
/// Keep this table append-only — never edit a snapshot after it's written. If a job runs twice
/// in a day, the unique (MetricName, ChannelFilter, CapturedDate) constraint blocks the
/// duplicate.
/// </summary>
public class AdminMetricSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Local-date the snapshot represents (the timezone the job ran in — typically UTC).
    /// Used as the X-axis on trend charts and as part of the unique constraint to prevent dupes.</summary>
    public DateOnly CapturedDate { get; set; }

    /// <summary>Stable identifier for the metric — e.g. "dau", "wau", "mau", "mrr",
    /// "misparse_rate", "failed_payments_24h". Lowercase snake_case for consistency.</summary>
    public string MetricName { get; set; } = string.Empty;

    /// <summary>Optional channel filter — "Whatsapp", "Telegram", "Messenger", "Dashboard", or
    /// null for the cross-channel total.</summary>
    public string? ChannelFilter { get; set; }

    /// <summary>Numeric value of the metric. We store as decimal so revenue, percentages, and
    /// counts all fit. Trend charts render this directly.</summary>
    public decimal Value { get; set; }

    /// <summary>Optional human-readable form ("19.7%", "₦125,400") for UI convenience. The
    /// raw <see cref="Value"/> is still the source of truth.</summary>
    public string? ValueText { get; set; }
}

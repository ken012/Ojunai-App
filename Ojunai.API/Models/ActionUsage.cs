namespace Ojunai.API.Models;

/// <summary>
/// Incremental counter for business actions per product line per period. One row per
/// (Business, ProductLine, PeriodStart). Atomic INSERT…ON CONFLICT bumps <see cref="Count"/>
/// in a single round-trip; cap checks read this single row.
///
/// Period semantics:
///   - Dashboard: <see cref="PeriodStartUtc"/> = Monday 00:00 UTC for that ISO week.
///   - WBOS:      <see cref="PeriodStartUtc"/> = 1st of the month at midnight in the user's TZ
///                (stored as the corresponding UTC instant).
///
/// "Action" = one inbound user message to the bot. Bot replies don't increment.
///
/// Old rows aren't deleted — they're audit history for billing disputes and usage analytics.
/// A nightly reconciliation job cross-checks <see cref="Count"/> against the message log to
/// catch drift from webhook retries or other anomalies.
/// </summary>
public class ActionUsage
{
    public Guid BusinessId { get; set; }
    public ProductLine ProductLine { get; set; }
    public DateTime PeriodStartUtc { get; set; }

    /// <summary>Total actions across all channels — historical / aggregate counter.</summary>
    public int Count { get; set; }

    /// <summary>Actions where the inbound channel was WhatsApp. Sub-count of <see cref="Count"/>.
    /// Drives the WhatsApp pack quota meter — capped by whichever WhatsApp pack the business has.</summary>
    public int WhatsAppCount { get; set; }

    /// <summary>Actions where the inbound channel was Telegram or Messenger (the combined pool).
    /// Sub-count of <see cref="Count"/>. Drives the T+M meter — capped by the business's plan tier.</summary>
    public int MessagingCount { get; set; }

    public DateTime LastIncrementedAtUtc { get; set; } = DateTime.UtcNow;
}

namespace Ojunai.API.Models;

/// <summary>
/// One row per email address we will never send to again. Populated by the SES
/// bounce / complaint webhook. Checked by EmailService before every outbound send.
///
/// Why this exists: continuing to send to a hard-bounced address (mailbox doesn't
/// exist, domain dead, blocked by recipient) wrecks our sender reputation with SES.
/// Continuing after a complaint is worse — it can get us throttled or production
/// access revoked. The cheapest fix is a one-table suppression list keyed by the
/// normalized address.
/// </summary>
public class SuppressedEmail
{
    public Guid Id { get; set; }

    /// <summary>Lowercased + trimmed for case-insensitive lookups.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>"bounce" or "complaint" — drives the human-readable reason without us having to interpret the SES payload at read time.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>SES bounce type: "Permanent" / "Transient" / "Undetermined". Null for complaints.</summary>
    public string? BounceType { get; set; }

    /// <summary>SES bounce sub-type (General, NoEmail, Suppressed, …). Null for complaints.</summary>
    public string? BounceSubType { get; set; }

    /// <summary>Raw SES SNS payload, kept for audit + debugging.</summary>
    public string? RawPayload { get; set; }

    public DateTime SuppressedAtUtc { get; set; }
}

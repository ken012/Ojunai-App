namespace Ojunai.API.Models;

/// <summary>
/// One in-app notification, delivered through the dashboard's notification bell.
/// Distinct from WhatsApp alerts (which are sent as messages, not stored). The bell
/// shows unread count, the dropdown lists recent items, each can be marked read or
/// dismissed.
///
/// Scoping:
/// - UserId == null  → business-wide alert. Visible to Owner + Admin.
/// - UserId != null  → user-specific (security alerts: login, password, recovery, etc.)
///                     Visible only to that user.
///
/// Dedup: generators set DedupeKey to prevent the same alert spamming the bell. The
/// service rejects new rows with a matching unread DedupeKey within a per-type window.
/// </summary>
public class Alert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    public Guid? UserId { get; set; }
    public AlertType Type { get; set; }
    public AlertSeverity Severity { get; set; } = AlertSeverity.Info;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    /// <summary>Internal route the bell links to (e.g. "/inventory?focus=...").</summary>
    public string? LinkUrl { get; set; }
    /// <summary>Optional structured payload for the row's UI (e.g. amounts, ids).</summary>
    public string? MetadataJson { get; set; }
    /// <summary>Used to suppress duplicates. Same key + still unread = skip.</summary>
    public string? DedupeKey { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAtUtc { get; set; }
    public DateTime? DismissedAtUtc { get; set; }
}

public enum AlertType
{
    LowStock = 1,
    DailySummary = 2,
    LargeSale = 3,
    AgedReceivable = 4,
    NegativeStock = 5,
    LoginFromNewDevice = 6,
    PasswordChanged = 7,
    EmailVerified = 8,
    AccountRecoveryUsed = 9,
    FailedLoginBurst = 10,
    TrialEnding = 11,
    PaymentFailed = 12,
    StaffAdded = 13,
    StaffRemoved = 14,
    SalesGoalHit = 15,
    WhatsAppPackExpiringSoon = 16,
    WhatsAppPackExpired = 17,
    ProfileUpdated = 18,
}

public enum AlertSeverity
{
    Info = 1,
    Warning = 2,
    Critical = 3,
}

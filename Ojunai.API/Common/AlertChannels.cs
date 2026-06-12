namespace Ojunai.API.Common;

/// <summary>
/// The delivery channel a user has chosen for business alerts (low-stock, daily/weekly
/// summary, large-sale). Stored on <c>User.AlertChannel</c>.
///
/// <para><c>"none"</c> is the default for new accounts — until the owner explicitly picks a
/// channel in Settings → Alerts, business alerts are not delivered anywhere. This is distinct
/// from billing/account reminders (trial ending, renewal warnings), which always keep a
/// WhatsApp safety net via the NotificationDispatcher regardless of this setting.</para>
/// </summary>
public static class AlertChannels
{
    public const string None = "none";
    public const string WhatsApp = "whatsapp";
    public const string Telegram = "telegram";
    public const string Messenger = "messenger";

    /// <summary>Channels a user can actually select as their alert destination.</summary>
    public static readonly string[] Selectable = { WhatsApp, Telegram, Messenger };

    /// <summary>All accepted values for the alert-channel endpoint, including the "off" state.</summary>
    public static readonly string[] All = { None, WhatsApp, Telegram, Messenger };

    /// <summary>
    /// True when no channel is selected. Treats null/empty (legacy/unset rows) as "none" so
    /// fresh accounts and never-configured accounts both start with business alerts off.
    /// </summary>
    public static bool IsNone(string? channel) =>
        string.IsNullOrWhiteSpace(channel) ||
        channel.Trim().Equals(None, StringComparison.OrdinalIgnoreCase);
}

using Ojunai.API.Models.Messaging;

namespace Ojunai.API.Models;

/// <summary>
/// One-time token a dashboard user can hand off to a messaging channel to bind their account
/// to a channel-native identity (Telegram chat_id, Messenger PSID, etc.). Created when a user
/// clicks "Connect Telegram" in dashboard Settings; consumed by the orchestrator when the bot
/// receives <c>/start &lt;token&gt;</c> (Telegram) or the equivalent referral payload on other
/// channels.
///
/// Single-use: once <see cref="ConsumedAtUtc"/> is set the token is dead. Short-lived: tokens
/// expire after 30 minutes, plenty for "click the link, tap Start" but tight enough that an
/// intercepted token isn't replayable indefinitely.
/// </summary>
public class ChannelLinkToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid BusinessId { get; set; }
    public Channel Channel { get; set; }

    /// <summary>The opaque value handed to the user (and round-tripped through the channel).
    /// Random 32-byte hex (64 chars). Indexed for fast lookup on consume.</summary>
    public string Token { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ConsumedAtUtc { get; set; }

    /// <summary>The channel identity that consumed the token (chat_id for Telegram, PSID for Messenger).
    /// Useful for audit and debugging the binding step.</summary>
    public string? BoundToIdentity { get; set; }
}

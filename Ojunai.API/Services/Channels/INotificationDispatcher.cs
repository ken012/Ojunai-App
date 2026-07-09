using Ojunai.API.Models.Messaging;

namespace Ojunai.API.Services.Channels;

/// <summary>
/// Channel-aware outbound notifications. Resolves the user's preferred channel
/// (<c>User.AlertChannel</c>), looks up their <see cref="Models.ContactIdentity"/> on that
/// channel, and routes through the right <see cref="IChannelAdapter"/>. Used for alerts,
/// daily summaries, trial reminders — anything that's fire-and-forget rather than a direct
/// reply to an inbound message.
///
/// What this is NOT for:
///   - OTPs / password-reset codes (always WhatsApp — phone-bound by definition)
///   - Direct webhook replies (the orchestrator routes those channel-specifically)
///   - Customer-side receipts to walk-ins (no User → no preference)
/// </summary>
public interface INotificationDispatcher
{
    /// <summary>
    /// Sends a notification to the user on their preferred channel. Falls back to WhatsApp
    /// if the preferred channel isn't bound (e.g. user set Telegram preference but later
    /// disconnected Telegram). Returns the channel actually used so callers can log.
    /// </summary>
    Task<Channel> SendToUserAsync(Guid userId, ReplyComposition reply, CancellationToken ct = default);

    /// <summary>
    /// Same but addresses a user by phone (for paths where we don't have the userId in hand —
    /// e.g. some legacy job services). Looks up the User, then delegates.
    /// </summary>
    Task<Channel> SendToPhoneAsync(string phone, ReplyComposition reply, CancellationToken ct = default);

    /// <summary>
    /// Fan-out variant: sends to EVERY channel the user is reachable on — WhatsApp (whenever a
    /// phone is on record) plus any bound Telegram/Messenger identities — instead of only their
    /// single preferred <c>AlertChannel</c>. Best-effort per channel; one channel failing never
    /// blocks the others. Returns the channels a send actually succeeded on (empty if none).
    /// Use for high-signal notices (e.g. a bulk import finishing) where the owner should hear
    /// about it wherever they are.
    /// </summary>
    Task<IReadOnlyList<Channel>> SendToAllUserChannelsAsync(Guid userId, ReplyComposition reply, CancellationToken ct = default);
}

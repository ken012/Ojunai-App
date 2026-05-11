using Ojunai.API.Models.Messaging;

namespace Ojunai.API.Services.Channels;

/// <summary>
/// Issues and consumes one-time tokens used to bind a dashboard User to a channel-native
/// identity (Telegram chat_id, Messenger PSID). Both ends — the dashboard "Connect Telegram"
/// flow and the bot's <c>/start &lt;token&gt;</c> handler — go through this service.
/// </summary>
public interface IChannelLinkingService
{
    /// <summary>
    /// Mints a fresh token for the (user, business, channel) tuple and returns the full deep-link
    /// the user should follow. For Telegram that's <c>https://t.me/{bot}?start={token}</c>.
    /// </summary>
    Task<string> CreateLinkAsync(Guid userId, Guid businessId, Channel channel, CancellationToken ct = default);

    /// <summary>
    /// Looks up the token, validates it (not expired, not consumed), creates or updates the
    /// ContactIdentity row to bind <paramref name="channelIdentity"/> to the User the token was
    /// minted for, and marks the token consumed. Returns the bound User+Business or null if the
    /// token was invalid.
    /// </summary>
    Task<(Guid UserId, Guid BusinessId)?> ConsumeAsync(
        string token,
        Channel channel,
        string channelIdentity,
        string? displayName,
        CancellationToken ct = default);
}

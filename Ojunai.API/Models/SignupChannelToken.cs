using Ojunai.API.Models.Messaging;

namespace Ojunai.API.Models;

/// <summary>
/// Token issued when a visitor on /register clicks "Sign up via Telegram" (or eventually
/// Messenger). The visitor hands this token to a chat bot via /start; the bot collects the
/// missing signup details (phone via Telegram's request_contact button, business name in
/// chat) and then creates the User + Business.
///
/// Distinct from <see cref="ChannelLinkToken"/> because that one binds an EXISTING dashboard
/// user to a chat — UserId/BusinessId are required at creation. For signup we don't have
/// those yet; they get filled in on consume after the chat captures phone + name.
///
/// Single-use: <see cref="ConsumedAtUtc"/> is set when the bot completes signup.
/// Short-lived: tokens expire after 30 minutes — long enough to "click link, tap Start,
/// share contact" but tight enough that an intercepted token isn't replayable.
///
/// Token format: "signup_" prefix + 64 hex chars. The prefix lets the orchestrator route
/// signup tokens through the signup handler without colliding with regular link tokens.
/// </summary>
public class SignupChannelToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Channel Channel { get; set; }

    /// <summary>The opaque value handed to the user. "signup_" prefix + 64 hex chars.</summary>
    public string Token { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ConsumedAtUtc { get; set; }

    /// <summary>Set on successful consume — the chat identity (chat_id) that completed signup.</summary>
    public string? ConsumedByIdentity { get; set; }

    /// <summary>Set on successful consume — the User row the signup created.</summary>
    public Guid? CreatedUserId { get; set; }

    /// <summary>Set on successful consume — the Business row the signup created.</summary>
    public Guid? CreatedBusinessId { get; set; }

    /// <summary>Optional IP / user-agent context captured at issue time for fraud monitoring.</summary>
    public string? RequestIp { get; set; }
}

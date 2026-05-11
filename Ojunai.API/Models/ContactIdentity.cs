using Ojunai.API.Models.Messaging;

namespace Ojunai.API.Models;

/// <summary>
/// Per-channel identity for a User. Lets one human reach Ojunai through multiple channels
/// without duplicating their User row. <see cref="User.PhoneNumber"/> stays as the canonical
/// account phone (used for OTPs, password reset, billing identity); ContactIdentity is what
/// the messaging orchestrator looks up when a webhook fires.
///
/// Unique on (<see cref="Channel"/>, <see cref="ChannelIdentityValue"/>) — there's exactly
/// one User per channel-handle worldwide.
///
/// On WhatsApp launch, a backfill will create one Whatsapp identity per existing User using
/// their PhoneNumber. New Telegram/Messenger users get a row created when they first message
/// the bot (the "/start" or first-Page-DM event).
/// </summary>
public class ContactIdentity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Owning user. Null = anonymous lead (someone messaged the bot but hasn't completed
    /// onboarding). On successful onboarding the row gets bound to the new User row.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Owning business. Useful for tenant scoping queries and auto-routing inbound messages
    /// when one identity could plausibly belong to multiple businesses (rare but possible).
    /// </summary>
    public Guid? BusinessId { get; set; }

    public Channel Channel { get; set; }

    /// <summary>
    /// The handle as the channel knows it. Channel-specific format:
    ///   - Whatsapp / Sms: E.164 phone, e.g. <c>+2348012345678</c>
    ///   - Telegram:       chat_id stringified, e.g. <c>123456789</c>
    ///   - Messenger:      page-scoped user ID (PSID)
    /// </summary>
    public string ChannelIdentityValue { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    /// <summary>
    /// True when this identity is the user's primary contact for the channel — drives where
    /// staff alerts go when a User has multiple identities of the same channel (rare but possible
    /// e.g. work vs personal WhatsApp number).
    /// </summary>
    public bool IsPrimary { get; set; } = true;

    public DateTime LinkedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenAtUtc { get; set; }
}

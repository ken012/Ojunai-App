using Ojunai.API.Models.Messaging;

namespace Ojunai.API.Services.Channels;

/// <summary>
/// One adapter per messaging channel. Hides the provider-specific protocol behind a
/// uniform surface so the orchestrator never sees Twilio webhooks, Telegram Updates, or
/// Meta messaging events directly.
///
/// Adding a new channel = implementing this interface + a webhook controller route + DI registration.
/// </summary>
public interface IChannelAdapter
{
    /// <summary>Channel this adapter handles. Used by the registry to look up the right adapter.</summary>
    Channel Channel { get; }

    /// <summary>Static feature matrix for the channel. The orchestrator consults this before
    /// composing replies that depend on rich features (buttons, media, etc.).</summary>
    ChannelCapabilities Capabilities { get; }

    /// <summary>
    /// Verifies the inbound HTTP request actually came from the channel provider. Each provider
    /// has its own scheme: Twilio HMAC-SHA1 of URL+sorted-form, Telegram secret_token header,
    /// Meta HMAC-SHA256 of body with App Secret. Adapters return false for any failure
    /// (signature mismatch, missing config, replay) — the controller returns 401/403 in response.
    /// </summary>
    Task<bool> VerifySignatureAsync(HttpRequest request);

    /// <summary>
    /// Parses the inbound HTTP request body into a universal <see cref="ConversationMessage"/>.
    /// Returns null when the request is a non-message event we should ignore (status callback,
    /// delivery receipt, channel ping, etc.) — the controller responds 200 OK without invoking
    /// the orchestrator.
    /// </summary>
    Task<ConversationMessage?> ParseInboundAsync(HttpRequest request);

    /// <summary>
    /// Renders the universal <see cref="ReplyComposition"/> into a channel-native send and
    /// invokes the channel provider's API.
    /// </summary>
    /// <param name="recipientIdentity">
    /// The channel-native address — E.164 phone for WhatsApp/SMS, chat_id string for Telegram,
    /// PSID for Messenger. Looked up from <c>ContactIdentity</c> by the orchestrator.
    /// </param>
    Task<SendResult> SendAsync(string recipientIdentity, ReplyComposition reply, CancellationToken ct = default);
}

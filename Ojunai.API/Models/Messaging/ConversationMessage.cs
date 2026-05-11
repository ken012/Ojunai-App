namespace Ojunai.API.Models.Messaging;

/// <summary>
/// Universal inbound message shape. Every channel adapter parses its provider-specific
/// webhook payload into this. The orchestrator never sees raw Twilio/Telegram/Meta
/// shapes — only ConversationMessage.
///
/// Outbound messages are NOT modeled here; the orchestrator returns
/// <see cref="ReplyComposition"/> and the adapter renders that back into a channel-native send.
/// </summary>
public sealed record ConversationMessage
{
    /// <summary>Internal ID; assigned when we persist to MessageLog.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Channel Channel { get; init; }

    /// <summary>
    /// The provider's identifier for this message — Twilio's <c>MessageSid</c>,
    /// Telegram's <c>update_id</c>, Meta's <c>mid</c>. Used for idempotency: if we've
    /// already processed this id, skip.
    /// </summary>
    public required string ProviderMessageId { get; init; }

    /// <summary>
    /// The sender's identity in the channel's native format. For WhatsApp/SMS this is an
    /// E.164 phone (+234...), for Telegram it's the chat_id as a string, for Messenger
    /// it's the page-scoped user ID (PSID).
    /// </summary>
    public required string SenderIdentity { get; init; }

    public string? SenderDisplayName { get; init; }

    public string? Text { get; init; }

    public IReadOnlyList<MediaAttachment> Media { get; init; } = Array.Empty<MediaAttachment>();

    public DateTime ReceivedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Provider-side identifier of the bot message this message is a reply to, when applicable.
    /// Populated for Telegram callback_query events (so handlers can edit the original keyboard
    /// message to remove buttons after first tap) and for WhatsApp "reply to" threading. Null
    /// when the message stands alone. Long because Telegram's message_id is a 53-bit int.
    /// </summary>
    public long? InReplyToMessageId { get; init; }
}

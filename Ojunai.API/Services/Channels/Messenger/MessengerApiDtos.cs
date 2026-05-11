using System.Text.Json.Serialization;

namespace Ojunai.API.Services.Channels.Messenger;

/// <summary>
/// Subset of Meta's Messenger Platform shapes that we actually consume. Full reference:
/// <see href="https://developers.facebook.com/docs/messenger-platform/reference/webhook-events/messages"/>.
///
/// Inbound webhook events all arrive under a top-level "object: page" + "entry" array, where
/// each entry has a "messaging" array of individual events. We only model the message + postback
/// shapes — quick_reply, attachments, referrals get added as needed.
/// </summary>
public sealed class MessengerWebhookPayload
{
    /// <summary>Always "page" for Messenger Platform events; anything else we ignore.</summary>
    [JsonPropertyName("object")] public string? Object { get; set; }

    [JsonPropertyName("entry")] public List<MessengerEntry>? Entry { get; set; }
}

public sealed class MessengerEntry
{
    /// <summary>The Page ID this event is for. Pages our app is subscribed to.</summary>
    [JsonPropertyName("id")] public string? Id { get; set; }

    /// <summary>Unix timestamp in ms when the event occurred.</summary>
    [JsonPropertyName("time")] public long Time { get; set; }

    [JsonPropertyName("messaging")] public List<MessengerEvent>? Messaging { get; set; }
}

public sealed class MessengerEvent
{
    [JsonPropertyName("sender")] public MessengerAgent? Sender { get; set; }
    [JsonPropertyName("recipient")] public MessengerAgent? Recipient { get; set; }

    /// <summary>Unix timestamp ms when the user sent the message.</summary>
    [JsonPropertyName("timestamp")] public long Timestamp { get; set; }

    [JsonPropertyName("message")] public MessengerMessage? Message { get; set; }
    [JsonPropertyName("postback")] public MessengerPostback? Postback { get; set; }
    [JsonPropertyName("referral")] public MessengerReferral? Referral { get; set; }
}

/// <summary>
/// Sender or recipient identifier. For inbound user messages, <see cref="Id"/> is the PSID
/// (Page-Scoped User ID) — an opaque per-(user, page) identifier we treat as the user's
/// channel-native handle.
/// </summary>
public sealed class MessengerAgent
{
    [JsonPropertyName("id")] public string? Id { get; set; }
}

public sealed class MessengerMessage
{
    /// <summary>Meta's message id, format "mid.xxx". Used for inbound idempotency.</summary>
    [JsonPropertyName("mid")] public string? Mid { get; set; }

    [JsonPropertyName("text")] public string? Text { get; set; }

    /// <summary>Set when the user tapped a Quick Reply button. The payload is what we sent.</summary>
    [JsonPropertyName("quick_reply")] public MessengerQuickReplyPayload? QuickReply { get; set; }
}

public sealed class MessengerQuickReplyPayload
{
    [JsonPropertyName("payload")] public string? Payload { get; set; }
}

/// <summary>
/// Postback events fire when the user taps a structured-template button or a Get Started button.
/// The bot configures the payload string; Meta echoes it back here.
/// </summary>
public sealed class MessengerPostback
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("payload")] public string? Payload { get; set; }
    [JsonPropertyName("referral")] public MessengerReferral? Referral { get; set; }
}

/// <summary>
/// Referral events fire when a user reaches the bot via <c>m.me/&lt;page&gt;?ref=&lt;ref&gt;</c>.
/// The <see cref="Ref"/> string is whatever we put in the deep link — our one-time linking token.
/// </summary>
public sealed class MessengerReferral
{
    [JsonPropertyName("ref")] public string? Ref { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
}

// ─── Outbound shapes ─────────────────────────────────────────────────────────

/// <summary>
/// POST body for <c>graph.facebook.com/v19.0/me/messages</c>. Messaging type matters: RESPONSE
/// is for within-24h-window replies; MESSAGE_TAG with a specific tag is for outside the window
/// (receipts, account updates, etc.). UPDATE is for one-off notifications inside the window.
/// </summary>
public sealed class MessengerSendMessageRequest
{
    [JsonPropertyName("recipient")] public MessengerRecipient Recipient { get; set; } = new();

    /// <summary>"RESPONSE" | "UPDATE" | "MESSAGE_TAG". Phase 3a defaults to RESPONSE (within-window).</summary>
    [JsonPropertyName("messaging_type")] public string MessagingType { get; set; } = "RESPONSE";

    /// <summary>Required when MessagingType = "MESSAGE_TAG". Examples: "CONFIRMED_EVENT_UPDATE",
    /// "POST_PURCHASE_UPDATE", "ACCOUNT_UPDATE".</summary>
    [JsonPropertyName("tag")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tag { get; set; }

    [JsonPropertyName("message")] public MessengerOutboundMessage Message { get; set; } = new();
}

public sealed class MessengerRecipient
{
    /// <summary>The recipient's PSID.</summary>
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
}

public sealed class MessengerOutboundMessage
{
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("quick_replies")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<MessengerQuickReply>? QuickReplies { get; set; }
}

public sealed class MessengerQuickReply
{
    /// <summary>Currently we only use "text" type quick replies (vs "user_phone_number" etc.).</summary>
    [JsonPropertyName("content_type")] public string ContentType { get; set; } = "text";

    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;

    [JsonPropertyName("payload")] public string Payload { get; set; } = string.Empty;
}

public sealed class MessengerApiResponse
{
    [JsonPropertyName("recipient_id")] public string? RecipientId { get; set; }
    [JsonPropertyName("message_id")] public string? MessageId { get; set; }
    [JsonPropertyName("error")] public MessengerApiError? Error { get; set; }
}

public sealed class MessengerApiError
{
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("error_subcode")] public int? ErrorSubcode { get; set; }
}

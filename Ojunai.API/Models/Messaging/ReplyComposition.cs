namespace Ojunai.API.Models.Messaging;

/// <summary>
/// What the orchestrator returns to be sent back to the user. Channel-agnostic; the
/// <see cref="Services.Channels.IChannelAdapter"/> renders this to the channel's native format.
/// Channels with limited capabilities degrade gracefully:
///   - <see cref="QuickReplies"/> on a channel without buttons → flatten to a numbered list in <see cref="Text"/>.
///   - <see cref="Media"/> on SMS → drop and append a download link in <see cref="Text"/>.
/// </summary>
public sealed record ReplyComposition
{
    /// <summary>Plain-text body. Required even when the primary content is media — many
    /// channels won't render media without a text caption, and SMS can't render media at all.</summary>
    public required string Text { get; init; }

    public IReadOnlyList<MediaAttachment>? Media { get; init; }

    /// <summary>Tap-to-reply buttons. Only sent on channels that support them; otherwise
    /// flattened into the text body.</summary>
    public IReadOnlyList<QuickReply>? QuickReplies { get; init; }

    /// <summary>True when sending requires a pre-approved template (e.g. WhatsApp outside
    /// the 24h service window, or Messenger outside the 24h conversation window with
    /// a Message Tag). Adapters that don't support templates fail loudly.</summary>
    public bool RequiresApprovedTemplate { get; init; } = false;

    /// <summary>Optional template name when <see cref="RequiresApprovedTemplate"/> is true.</summary>
    public string? TemplateName { get; init; }

    /// <summary>
    /// Messenger-specific. When the recipient's 24h conversation window is closed, the adapter
    /// must send with <c>messaging_type=MESSAGE_TAG</c> + a tag (Meta blocks free-form replies
    /// outside the window). Adapters for other channels ignore this. Default <see cref="MessageTag.None"/>
    /// means "no tag" — the send will fail outside the window unless the window is open.
    /// </summary>
    public MessageTag MessageTag { get; init; } = MessageTag.None;
}

public sealed record QuickReply(string Label, string Payload);

/// <summary>
/// Messenger Message Tags allowed for outside-the-24h-window sends.
/// <see href="https://developers.facebook.com/docs/messenger-platform/send-messages/message-tags"/>.
///
/// Use the most specific tag that fits the message — Meta enforces appropriate use per tag and
/// repeated misuse can block the page. Pick:
///   - <see cref="ConfirmedEventUpdate"/> for things like upcoming-reservation reminders.
///   - <see cref="PostPurchaseUpdate"/> for receipts and order-status updates.
///   - <see cref="AccountUpdate"/> for billing changes, plan changes, login alerts, etc.
///   - <see cref="HumanAgent"/> only when a human is actively responding (requires extra permission).
/// </summary>
public enum MessageTag
{
    None = 0,
    ConfirmedEventUpdate,
    PostPurchaseUpdate,
    AccountUpdate,
    HumanAgent,
}

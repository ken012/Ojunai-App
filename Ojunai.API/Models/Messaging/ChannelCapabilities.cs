namespace Ojunai.API.Models.Messaging;

/// <summary>
/// Per-channel feature matrix. Adapters expose this so the orchestrator can decide
/// whether a planned <see cref="ReplyComposition"/> is sendable as-is, needs degradation,
/// or must be rejected. Compile-time channel-specific behavior stays in the adapter;
/// runtime "what can I send through this channel right now" goes through this record.
/// </summary>
public sealed record ChannelCapabilities(
    bool SupportsMedia,
    bool SupportsButtons,
    bool SupportsTypingIndicator,
    /// <summary>
    /// True when the channel has a free reply window after user-initiated message (WhatsApp 24h,
    /// Messenger 24h). False when every outbound message costs (SMS) or there's no window at all (Telegram = always free).
    /// </summary>
    bool HasFreeServiceWindow,
    int MaxTextLength);

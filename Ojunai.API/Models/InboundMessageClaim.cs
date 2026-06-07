using Ojunai.API.Models.Messaging;

namespace Ojunai.API.Models;

/// <summary>
/// One row per inbound message we've started processing, keyed by the provider's message id.
/// Acts as a durable, atomic "claim" so a given inbound message is acted on AT MOST ONCE — the
/// composite primary key (<see cref="Channel"/>, <see cref="ProviderMessageId"/>) lets Postgres
/// reject a second insert, which is how we win the race that a plain read-then-act check loses.
///
/// Why a dedicated table instead of reusing MessageLog: MessageLog is an audit log (and already
/// contains the duplicate rows the old non-atomic check produced), so a unique constraint there
/// couldn't be added cleanly. This table starts empty and is purged on a short retention window —
/// the only thing it has to outlive is a provider's re-delivery window (minutes to a few hours).
///
/// See <see cref="Services.IInboundDedupService"/> for the insert-or-skip claim logic and
/// <see cref="Jobs.MessageLogRetentionJobService"/> for cleanup.
/// </summary>
public class InboundMessageClaim
{
    public Channel Channel { get; set; }

    /// <summary>Provider's native id: Twilio MessageSid, Telegram update/message id, Meta mid.</summary>
    public string ProviderMessageId { get; set; } = string.Empty;

    public DateTime ClaimedAtUtc { get; set; } = DateTime.UtcNow;
}

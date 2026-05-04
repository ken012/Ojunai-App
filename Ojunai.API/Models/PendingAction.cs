namespace Ojunai.API.Models;

/// <summary>
/// A conversational state row tracking an intent the bot started parsing but couldn't execute because
/// a required field was missing. On the next inbound WhatsApp message from the same user, the handler
/// checks for a matching row first — if present, it merges the answer into the partial payload and
/// executes the intent in one shot, bypassing a fresh Claude parse.
///
/// This is what prevents the "bot loses its train of thought" problem: even if Claude's follow-up
/// interpretation fails, we have a deterministic server-side memory of what was pending and why.
///
/// Keyed by (BusinessId, UserId). Only one pending action per user at a time — a new one overwrites
/// the previous. Expired rows are ignored.
/// </summary>
public class PendingAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>
    /// Original intent name (e.g. "create_sale"). Used to dispatch the completion back to the right handler.
    /// </summary>
    public string Intent { get; set; } = string.Empty;

    /// <summary>
    /// JSON-serialized partial businessAction payload. The handler that originally set the pending state
    /// decides what shape this has; the completion handler merges in the answer and deserializes.
    /// </summary>
    public string PartialPayloadJson { get; set; } = string.Empty;

    /// <summary>
    /// What field we're waiting on: "unitPrice", "quantity", "contactName", etc. Consumed by the
    /// completion path to decide how to interpret the user's reply.
    /// </summary>
    public string AwaitingField { get; set; } = string.Empty;

    /// <summary>
    /// The exact question the bot sent, for audit and for the UI to cite if the user asks "what were we
    /// talking about?"
    /// </summary>
    public string QuestionText { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
}

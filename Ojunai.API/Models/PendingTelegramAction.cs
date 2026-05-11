namespace Ojunai.API.Models;

/// <summary>
/// Server-side state for multi-step Telegram flows that span a callback round-trip. Telegram's
/// callback_data is limited to 64 bytes, so we store the rich context (sale items, customer name,
/// etc.) here and reference it from the inline-keyboard button via a short token.
///
/// Single-use: once <see cref="ConsumedAtUtc"/> is set, the token can't be replayed. 30-minute
/// expiry — enough for the user to scroll back and tap the button, tight enough that an
/// intercepted token isn't valuable for long.
///
/// Phase 2.8 ships this for "add product on the fly" (Yes/No on unknown product during sale).
/// Phase 2.9 reuses it for the full pending-action state machine (multi-turn clarifications).
/// </summary>
public class PendingTelegramAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>The Telegram chat_id the action lives in. Used to confirm the callback arrives from
    /// the same chat that opened the action — prevents cross-user replay if a token leaks.</summary>
    public string ChatId { get; set; } = string.Empty;

    /// <summary>Short opaque token embedded in callback_data. 16 url-safe chars.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Identifies which action handler picks this up on resume. E.g. "add_product_and_sell".</summary>
    public string ActionType { get; set; } = string.Empty;

    /// <summary>Action-specific JSON payload (full sale context, missing-product names, etc.).</summary>
    public string PayloadJson { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
}

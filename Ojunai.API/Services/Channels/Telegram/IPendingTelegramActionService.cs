namespace Ojunai.API.Services.Channels.Telegram;

/// <summary>
/// CRUD for <see cref="Models.PendingTelegramAction"/>. Used by the intent handler to stash
/// rich context (sale items, customer name, missing products) when a flow requires a user
/// callback — since Telegram's callback_data is limited to 64 bytes, we store the data
/// server-side and reference it via a short token.
/// </summary>
public interface IPendingTelegramActionService
{
    /// <summary>Creates a pending action and returns the short token to embed in callback_data.</summary>
    Task<string> CreateAsync(Guid businessId, Guid userId, string chatId, string actionType, string payloadJson, CancellationToken ct = default);

    /// <summary>
    /// Looks up a token, validates it (not expired, not consumed, chat matches), marks it consumed,
    /// returns the payload. Null if invalid.
    /// </summary>
    Task<PendingActionConsumeResult?> ConsumeAsync(string token, string chatId, CancellationToken ct = default);

    /// <summary>Marks the token cancelled without consuming the payload. Used for the "No" button.</summary>
    Task CancelAsync(string token, string chatId, CancellationToken ct = default);
}

public sealed record PendingActionConsumeResult(
    Guid BusinessId,
    Guid UserId,
    string ActionType,
    string PayloadJson);

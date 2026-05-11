using System.Text.Json.Serialization;

namespace Ojunai.API.Services.Channels.Telegram;

/// <summary>
/// Subset of Telegram's Bot API shapes that we actually consume. Full API:
/// <see href="https://core.telegram.org/bots/api"/>. We deliberately don't model
/// every field — just what the orchestrator needs (text messages, basic media,
/// /start onboarding, sender identity). Anything else gets ignored gracefully.
///
/// Naming matches Telegram's snake_case wire format via <c>JsonPropertyName</c>;
/// .NET-side property names stay PascalCase.
/// </summary>
public sealed class TelegramUpdate
{
    [JsonPropertyName("update_id")] public long UpdateId { get; set; }
    [JsonPropertyName("message")]   public TelegramMessage? Message { get; set; }
    [JsonPropertyName("edited_message")] public TelegramMessage? EditedMessage { get; set; }
    [JsonPropertyName("callback_query")] public TelegramCallbackQuery? CallbackQuery { get; set; }
}

public sealed class TelegramMessage
{
    [JsonPropertyName("message_id")] public long MessageId { get; set; }
    [JsonPropertyName("from")]       public TelegramUser? From { get; set; }
    [JsonPropertyName("chat")]       public TelegramChat? Chat { get; set; }
    [JsonPropertyName("date")]       public long Date { get; set; }   // Unix epoch
    [JsonPropertyName("text")]       public string? Text { get; set; }
    [JsonPropertyName("photo")]      public List<TelegramPhotoSize>? Photo { get; set; }
    [JsonPropertyName("document")]   public TelegramDocument? Document { get; set; }
    [JsonPropertyName("voice")]      public TelegramVoice? Voice { get; set; }
}

public sealed class TelegramUser
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("is_bot")] public bool IsBot { get; set; }
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("language_code")] public string? LanguageCode { get; set; }
}

public sealed class TelegramChat
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }   // "private", "group", "supergroup", "channel"
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
}

public sealed class TelegramPhotoSize
{
    [JsonPropertyName("file_id")] public string FileId { get; set; } = string.Empty;
    [JsonPropertyName("file_unique_id")] public string FileUniqueId { get; set; } = string.Empty;
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("file_size")] public long? FileSize { get; set; }
}

public sealed class TelegramDocument
{
    [JsonPropertyName("file_id")] public string FileId { get; set; } = string.Empty;
    [JsonPropertyName("file_name")] public string? FileName { get; set; }
    [JsonPropertyName("mime_type")] public string? MimeType { get; set; }
    [JsonPropertyName("file_size")] public long? FileSize { get; set; }
}

public sealed class TelegramVoice
{
    [JsonPropertyName("file_id")] public string FileId { get; set; } = string.Empty;
    [JsonPropertyName("duration")] public int Duration { get; set; }
    [JsonPropertyName("mime_type")] public string? MimeType { get; set; }
    [JsonPropertyName("file_size")] public long? FileSize { get; set; }
}

/// <summary>Reply to a tap-on-inline-keyboard. Phase 2 doesn't use these heavily yet,
/// but the shape is here for the confirm/cancel flows we'll wire up next.</summary>
public sealed class TelegramCallbackQuery
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("from")] public TelegramUser? From { get; set; }
    [JsonPropertyName("message")] public TelegramMessage? Message { get; set; }
    [JsonPropertyName("data")] public string? Data { get; set; }
}

// ─── Outbound shapes (what we POST to api.telegram.org) ──────────────────────

public sealed class TelegramSendMessageRequest
{
    [JsonPropertyName("chat_id")] public string ChatId { get; set; } = string.Empty;
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;

    // Telegram rejects "parse_mode": null and "reply_markup": null with "Bad Request: object
    // expected". Omit these fields entirely when not set so we send a clean payload.
    [JsonPropertyName("parse_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParseMode { get; set; } = "Markdown";

    [JsonPropertyName("reply_markup")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TelegramReplyMarkup? ReplyMarkup { get; set; }
}

public sealed class TelegramReplyMarkup
{
    [JsonPropertyName("inline_keyboard")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<List<TelegramInlineKeyboardButton>>? InlineKeyboard { get; set; }
}

public sealed class TelegramInlineKeyboardButton
{
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;

    [JsonPropertyName("callback_data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CallbackData { get; set; }
}

public sealed class TelegramApiResponse<T>
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("result")] public T? Result { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
}

public sealed class TelegramSentMessage
{
    [JsonPropertyName("message_id")] public long MessageId { get; set; }
}

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ojunai.API.Models.Messaging;

namespace Ojunai.API.Services.Channels.Telegram;

/// <summary>
/// Telegram via the official Bot API (<see href="https://core.telegram.org/bots/api"/>).
/// Free, no per-message cost, no 24h conversation window — best second channel after WhatsApp.
///
/// Inbound: Telegram POSTs <c>Update</c> JSON to our webhook. We verify the
/// <c>X-Telegram-Bot-Api-Secret-Token</c> header (set when we called <c>setWebhook</c>) so
/// only Telegram (or someone who knows our secret) can hit the URL.
///
/// Outbound: POST to <c>api.telegram.org/bot{token}/sendMessage</c> (or sendDocument for PDFs).
/// Renders <see cref="ReplyComposition.QuickReplies"/> as inline keyboard buttons,
/// <see cref="ReplyComposition.Media"/> by sending separate sendDocument calls.
/// </summary>
public sealed class TelegramAdapter : IChannelAdapter
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<TelegramAdapter> _logger;

    public TelegramAdapter(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        IWebHostEnvironment env,
        ILogger<TelegramAdapter> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _env = env;
        _logger = logger;
    }

    public Channel Channel => Channel.Telegram;

    public ChannelCapabilities Capabilities { get; } = new(
        SupportsMedia: true,
        SupportsButtons: true,           // inline keyboards work everywhere
        SupportsTypingIndicator: true,   // sendChatAction "typing"
        HasFreeServiceWindow: false,     // no window concept — outbound is always free
        MaxTextLength: 4096);             // Telegram's per-message limit

    public Task<bool> VerifySignatureAsync(HttpRequest request)
    {
        var expected = _config["Telegram:WebhookSecret"];
        if (string.IsNullOrEmpty(expected))
        {
            // Dev-only convenience: allow when no secret is set. Production deploys must always set it.
            if (_env.IsDevelopment())
            {
                _logger.LogWarning("Telegram:WebhookSecret not set — accepting in dev mode");
                return Task.FromResult(true);
            }
            _logger.LogError("Telegram:WebhookSecret not configured in production");
            return Task.FromResult(false);
        }

        var provided = request.Headers["X-Telegram-Bot-Api-Secret-Token"].FirstOrDefault();
        if (provided is null) return Task.FromResult(false);

        var ok = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(expected));
        return Task.FromResult(ok);
    }

    public async Task<ConversationMessage?> ParseInboundAsync(HttpRequest request)
    {
        request.Body.Position = 0;
        TelegramUpdate? update;
        try
        {
            update = await JsonSerializer.DeserializeAsync<TelegramUpdate>(
                request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Telegram Update");
            return null;
        }

        if (update is null) return null;

        // Phase 2 handles message + edited_message + callback_query. Other update types
        // (channel posts, bot interactions in groups we don't manage) get ignored — return
        // null so the controller responds 200 OK without engaging the orchestrator.
        var msg = update.Message ?? update.EditedMessage;
        if (msg?.Chat is null)
        {
            // Callback query (button tap) → manufacture a ConversationMessage with the data
            // string as the text so the orchestrator can route it. Carry the bot-message id
            // through InReplyToMessageId so the handler can edit/remove the original keyboard
            // after the action is consumed (Phase 2.8.1).
            if (update.CallbackQuery?.Message?.Chat is { } cbChat && update.CallbackQuery.Data is { } cbData)
            {
                return new ConversationMessage
                {
                    Channel = Channel.Telegram,
                    ProviderMessageId = $"cb_{update.UpdateId}",
                    SenderIdentity = cbChat.Id.ToString(),
                    SenderDisplayName = update.CallbackQuery.From?.FirstName,
                    Text = cbData,
                    InReplyToMessageId = update.CallbackQuery.Message.MessageId,
                };
            }
            return null;
        }

        // Build the universal message. SenderIdentity = chat.id stringified — that's the
        // canonical "address" for sending messages back to this user.
        var displayName = string.Join(" ",
            new[] { msg.Chat.FirstName, msg.Chat.LastName }.Where(s => !string.IsNullOrEmpty(s))!);

        var media = new List<MediaAttachment>();
        if (msg.Document is { } doc)
        {
            media.Add(new MediaAttachment(
                doc.MimeType ?? "application/octet-stream",
                $"telegram://file/{doc.FileId}", // resolved on demand if we ever need to fetch
                FileName: doc.FileName));
        }
        // We don't need to resolve photo/voice for Phase 2 — orchestrator just records they exist.

        return new ConversationMessage
        {
            Channel = Channel.Telegram,
            ProviderMessageId = msg.MessageId.ToString(),
            SenderIdentity = msg.Chat.Id.ToString(),
            SenderDisplayName = string.IsNullOrEmpty(displayName) ? msg.Chat.Username : displayName,
            Text = msg.Text,
            Media = media,
            ReceivedAtUtc = DateTimeOffset.FromUnixTimeSeconds(msg.Date).UtcDateTime,
        };
    }

    public async Task<SendResult> SendAsync(string recipientIdentity, ReplyComposition reply, CancellationToken ct = default)
    {
        var token = _config["Telegram:BotToken"];
        if (string.IsNullOrEmpty(token))
        {
            return new SendResult(false, null, "Telegram:BotToken not configured");
        }

        var http = _httpFactory.CreateClient();
        var url = $"https://api.telegram.org/bot{token}/sendMessage";

        // Build the payload. Inline keyboards turn QuickReplies into tap buttons; otherwise
        // the text body is sent as plain Markdown (Telegram's limited Markdown variant — *bold*,
        // _italic_, `code`, [text](url)). Long replies are auto-segmented because Telegram's
        // 4096-char limit is rarely hit by our use cases.
        var request = new TelegramSendMessageRequest
        {
            ChatId = recipientIdentity,
            Text = TruncateIfNeeded(reply.Text, 4096),
            ParseMode = "Markdown",
            ReplyMarkup = BuildReplyMarkup(reply.QuickReplies),
        };

        try
        {
            var response = await http.PostAsJsonAsync(url, request, ct);
            var body = await response.Content.ReadFromJsonAsync<TelegramApiResponse<TelegramSentMessage>>(cancellationToken: ct);

            if (!response.IsSuccessStatusCode || body is null || !body.Ok)
            {
                var reason = body?.Description ?? $"HTTP {(int)response.StatusCode}";
                _logger.LogWarning("Telegram send failed for chat {Chat}: {Reason}", recipientIdentity, reason);
                return new SendResult(false, null, reason);
            }

            // Phase 2 sends text only via the adapter. Media is sent as a separate sendDocument
            // call when the orchestrator wires up PDF receipts (see SendDocumentAsync below).
            // For now reply.Media is ignored if present — the orchestrator should call
            // SendDocumentAsync directly when it has a PDF.

            return new SendResult(true, body.Result?.MessageId.ToString(), null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram send threw for chat {Chat}", recipientIdentity);
            return new SendResult(false, null, ex.Message);
        }
    }

    /// <summary>
    /// Telegram-specific extension: strip the inline keyboard from a previously-sent message.
    /// Used after a callback (Yes/No tap) so stale buttons can't be tapped again. Telegram's
    /// <c>editMessageReplyMarkup</c> with an empty <c>inline_keyboard</c> removes the buttons
    /// while leaving the message text intact.
    ///
    /// Best-effort: any failure (message too old, already edited, network blip) gets logged
    /// and swallowed — the action itself already succeeded; stale-button cleanup is gravy.
    /// </summary>
    public async Task RemoveInlineKeyboardAsync(string chatId, long messageId, CancellationToken ct = default)
    {
        var token = _config["Telegram:BotToken"];
        if (string.IsNullOrEmpty(token)) return;

        var http = _httpFactory.CreateClient();
        var url = $"https://api.telegram.org/bot{token}/editMessageReplyMarkup";

        // Empty inline_keyboard array is how Telegram tells you to remove the keyboard
        // (omitting reply_markup entirely is a no-op; you have to send an empty one explicitly).
        var payload = new
        {
            chat_id = chatId,
            message_id = messageId,
            reply_markup = new { inline_keyboard = Array.Empty<object[]>() },
        };

        try
        {
            await http.PostAsJsonAsync(url, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram editMessageReplyMarkup failed for {Chat} msg {Msg} — non-fatal", chatId, messageId);
        }
    }

    /// <summary>
    /// Telegram-specific extension: send a document (PDF receipt) as a native attachment.
    /// Called by the orchestrator after a sale is recorded. The PDF lands in the user's chat
    /// thread as a tap-to-open file with the configured caption.
    /// </summary>
    public async Task<SendResult> SendDocumentAsync(
        string recipientIdentity,
        Stream documentStream,
        string fileName,
        string? caption,
        CancellationToken ct = default)
    {
        var token = _config["Telegram:BotToken"];
        if (string.IsNullOrEmpty(token))
            return new SendResult(false, null, "Telegram:BotToken not configured");

        var http = _httpFactory.CreateClient();
        var url = $"https://api.telegram.org/bot{token}/sendDocument";

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(recipientIdentity), "chat_id");
        if (!string.IsNullOrEmpty(caption))
        {
            content.Add(new StringContent(caption), "caption");
            content.Add(new StringContent("Markdown"), "parse_mode");
        }

        var streamContent = new StreamContent(documentStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        content.Add(streamContent, "document", fileName);

        try
        {
            var response = await http.PostAsync(url, content, ct);
            var body = await response.Content.ReadFromJsonAsync<TelegramApiResponse<TelegramSentMessage>>(cancellationToken: ct);

            if (!response.IsSuccessStatusCode || body is null || !body.Ok)
            {
                var reason = body?.Description ?? $"HTTP {(int)response.StatusCode}";
                _logger.LogWarning("Telegram document send failed for chat {Chat}: {Reason}", recipientIdentity, reason);
                return new SendResult(false, null, reason);
            }

            return new SendResult(true, body.Result?.MessageId.ToString(), null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram document send threw for chat {Chat}", recipientIdentity);
            return new SendResult(false, null, ex.Message);
        }
    }

    private static TelegramReplyMarkup? BuildReplyMarkup(IReadOnlyList<QuickReply>? quickReplies)
    {
        if (quickReplies is null || quickReplies.Count == 0) return null;

        // Lay buttons out one-per-row. Telegram supports multi-column layouts but for
        // our typical "Yes / No" or "Confirm / Cancel" prompts a single row reads better
        // on narrow screens. Future: pack 2-per-row when there are 4+ buttons.
        var rows = quickReplies
            .Select(qr => new List<TelegramInlineKeyboardButton>
            {
                new() { Text = qr.Label, CallbackData = qr.Payload }
            })
            .ToList();

        return new TelegramReplyMarkup { InlineKeyboard = rows };
    }

    private static string TruncateIfNeeded(string text, int max)
        => text.Length <= max ? text : text[..(max - 1)] + "…";
}

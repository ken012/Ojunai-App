using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ojunai.API.Data;
using Ojunai.API.Models.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services.Channels.Messenger;

/// <summary>
/// Facebook Messenger Platform via Meta's Graph API.
/// (<see href="https://developers.facebook.com/docs/messenger-platform"/>).
///
/// Inbound: Meta POSTs <c>{object: "page", entry: [{messaging: [...]}]}</c> events to our webhook.
/// We HMAC-SHA256 the raw body against our App Secret and compare to the <c>X-Hub-Signature-256</c>
/// header — Meta signs every webhook so a malicious caller can't fake them. Multiple events can
/// arrive in one POST; we surface only the first (one inbound event = one ConversationMessage),
/// good enough because Meta typically batches at most a few per call.
///
/// Outbound: POST to <c>graph.facebook.com/v19.0/me/messages</c> with the Page Access Token in
/// the query string. Inside the 24-hour conversation window, MessagingType = "RESPONSE" works
/// for anything. Outside the window we MUST use MessagingType = "MESSAGE_TAG" + a specific tag
/// (CONFIRMED_EVENT_UPDATE for receipts, ACCOUNT_UPDATE for billing, etc.). For Phase 3a we
/// default to RESPONSE — window-aware tagging arrives in Phase 3d.
///
/// Identity: Messenger gives us a PSID (Page-Scoped User ID) per (user, page) pair — opaque,
/// per-page-stable. We use it as the user's <see cref="ContactIdentity.ChannelIdentityValue"/>.
/// </summary>
public sealed class MessengerAdapter : IChannelAdapter
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly AppDbContext _db;
    private readonly ILogger<MessengerAdapter> _logger;

    public MessengerAdapter(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        IWebHostEnvironment env,
        AppDbContext db,
        ILogger<MessengerAdapter> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _env = env;
        _db = db;
        _logger = logger;
    }

    public Channel Channel => Channel.Messenger;

    public ChannelCapabilities Capabilities { get; } = new(
        SupportsMedia: true,
        SupportsButtons: true,           // quick_replies + structured templates
        SupportsTypingIndicator: true,   // sender_action: typing_on
        HasFreeServiceWindow: true,      // 24h conversation window
        MaxTextLength: 2000);             // Meta's hard cap on message text

    /// <summary>
    /// Validates <c>X-Hub-Signature-256</c>: <c>"sha256=" + HMAC-SHA256(raw-body, app-secret)</c>.
    /// Meta sends the signature on every webhook delivery; missing or mismatching = reject.
    ///
    /// Important: this reads the request body, so the controller must call EnableBuffering()
    /// and rewind the stream before calling this. Otherwise the body is empty by the time
    /// ParseInbound tries to deserialize it.
    /// </summary>
    public async Task<bool> VerifySignatureAsync(HttpRequest request)
    {
        // Dev-only bypass — production must never set this flag.
        if (_env.IsDevelopment() && _config.GetValue<bool>("Messenger:SkipSignatureValidation"))
            return true;

        var appSecret = _config["Messenger:AppSecret"];
        if (string.IsNullOrEmpty(appSecret))
        {
            _logger.LogError("Messenger:AppSecret not configured — can't verify webhook signatures");
            return false;
        }

        if (!request.Headers.TryGetValue("X-Hub-Signature-256", out var providedHeader))
        {
            _logger.LogWarning("Missing X-Hub-Signature-256 header on Messenger webhook");
            return false;
        }

        var providedSig = providedHeader.ToString();
        if (!providedSig.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Unexpected X-Hub-Signature-256 format: {Header}", providedSig);
            return false;
        }
        var providedHex = providedSig["sha256=".Length..];

        // Read raw body bytes for HMAC. Caller is responsible for EnableBuffering + rewind.
        request.Body.Position = 0;
        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms);
        var body = ms.ToArray();
        request.Body.Position = 0;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var hash = hmac.ComputeHash(body);
        var computedHex = Convert.ToHexString(hash).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(providedHex.ToLowerInvariant()),
            Encoding.UTF8.GetBytes(computedHex));
    }

    public async Task<ConversationMessage?> ParseInboundAsync(HttpRequest request)
    {
        request.Body.Position = 0;
        MessengerWebhookPayload? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<MessengerWebhookPayload>(request.Body);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Messenger webhook body");
            return null;
        }

        if (payload?.Object != "page" || payload.Entry is null || payload.Entry.Count == 0)
            return null;

        // Meta can batch multiple events in one delivery; for Phase 3 we surface the first
        // user-message-or-postback event. If we ever need to handle batches, the controller
        // would loop and call orchestrator multiple times.
        foreach (var entry in payload.Entry)
        {
            if (entry.Messaging is null) continue;
            foreach (var ev in entry.Messaging)
            {
                if (ev.Sender?.Id is null) continue;
                var senderPsid = ev.Sender.Id;

                // 1. Plain message (text or quick-reply tap)
                if (ev.Message is { } msg)
                {
                    if (msg.Mid is null) continue;
                    // Quick-reply tap arrives as a message with the original text PLUS quick_reply.payload.
                    // The payload is what we set when sending the button — typically our "pa:yes:<token>"
                    // convention. We surface the payload as Text so the orchestrator's callback dispatcher
                    // sees it the same way as Telegram callback_query data.
                    var text = msg.QuickReply?.Payload ?? msg.Text;
                    return new ConversationMessage
                    {
                        Channel = Channel.Messenger,
                        ProviderMessageId = msg.Mid,
                        SenderIdentity = senderPsid,
                        Text = text,
                        ReceivedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(ev.Timestamp).UtcDateTime,
                    };
                }

                // 2. Postback (tap on a structured-template button or Get Started)
                if (ev.Postback is { } pb)
                {
                    return new ConversationMessage
                    {
                        Channel = Channel.Messenger,
                        ProviderMessageId = $"pb_{ev.Timestamp}_{senderPsid}",
                        SenderIdentity = senderPsid,
                        // Postback payload is what we configured when sending the button. Surface as Text.
                        // m.me/page?ref=<X> referrals come through here too via pb.Referral.Ref — we
                        // pack that into a "pa:link:<ref>" pseudo-callback the linking service handles.
                        Text = pb.Payload ?? (pb.Referral?.Ref is { } r ? $"mref:{r}" : null),
                    };
                }

                // 3. Referral standalone (user reaches the bot via m.me/page?ref=X without sending a message)
                if (ev.Referral?.Ref is { } refValue)
                {
                    return new ConversationMessage
                    {
                        Channel = Channel.Messenger,
                        ProviderMessageId = $"ref_{ev.Timestamp}_{senderPsid}",
                        SenderIdentity = senderPsid,
                        Text = $"mref:{refValue}",
                    };
                }
            }
        }
        return null;
    }

    public async Task<SendResult> SendAsync(string recipientIdentity, ReplyComposition reply, CancellationToken ct = default)
    {
        var pageToken = _config["Messenger:PageAccessToken"];
        if (string.IsNullOrEmpty(pageToken))
            return new SendResult(false, null, "Messenger:PageAccessToken not configured");

        // Phase 3d: pick messaging_type based on the 24-hour conversation window. Meta blocks
        // free-form replies outside the window — must send a MESSAGE_TAG with an appropriate tag,
        // or the send fails with a permissions error. Most intent-handler sends happen seconds
        // after the user messaged us so the window is wide open; the dispatcher path (scheduled
        // summaries, low-stock alerts) is what needs MESSAGE_TAG fallback.
        var (messagingType, tagString, failureReason) = await ResolveMessagingTypeAsync(recipientIdentity, reply.MessageTag, ct);
        if (messagingType is null)
            return new SendResult(false, null, failureReason);

        var http = _httpFactory.CreateClient("Channel");
        var url = $"https://graph.facebook.com/v19.0/me/messages?access_token={pageToken}";

        var request = new MessengerSendMessageRequest
        {
            Recipient = new MessengerRecipient { Id = recipientIdentity },
            MessagingType = messagingType,
            Tag = tagString,
            Message = new MessengerOutboundMessage
            {
                Text = TruncateIfNeeded(StripWhatsAppMarkdown(reply.Text), 2000),
                QuickReplies = BuildQuickReplies(reply.QuickReplies),
            },
        };

        try
        {
            var response = await http.PostAsJsonAsync(url, request, ct);
            var body = await response.Content.ReadFromJsonAsync<MessengerApiResponse>(cancellationToken: ct);

            if (!response.IsSuccessStatusCode || body?.Error is not null)
            {
                // `err` from `body?.Error is { } err` is only definitely assigned when the pattern
                // matches — short-circuiting on the HTTP-status side leaves it unassigned, which
                // the C# definite-assignment analyzer rejects (CS0165). Use direct null-conditional
                // member access on body.Error instead.
                var reason = body?.Error?.Message ?? $"HTTP {(int)response.StatusCode}";
                _logger.LogWarning("Messenger send failed for PSID {Psid}: {Reason}", recipientIdentity, reason);
                return new SendResult(false, null, reason);
            }

            return new SendResult(true, body?.MessageId, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Messenger send threw for PSID {Psid}", recipientIdentity);
            return new SendResult(false, null, ex.Message);
        }
    }

    private static List<MessengerQuickReply>? BuildQuickReplies(IReadOnlyList<QuickReply>? quickReplies)
    {
        if (quickReplies is null || quickReplies.Count == 0) return null;

        // Messenger caps at 13 quick replies per message; we typically send 1–2 (Yes/No prompts).
        return quickReplies.Take(13).Select(qr => new MessengerQuickReply
        {
            Title = TruncateIfNeeded(qr.Label, 20),  // Messenger truncates labels >20 chars on its own
            Payload = TruncateIfNeeded(qr.Payload, 1000), // Meta's hard limit
        }).ToList();
    }

    /// <summary>
    /// Resolves the right <c>messaging_type</c> / tag combination for an outbound send.
    /// Returns a tuple where Type is null when the send is impossible (closed window + no tag).
    ///
    /// Rules per Meta:
    /// - Window open (last user inbound &lt; 24h ago) → "RESPONSE" works for any free-form text.
    /// - Window closed → must send "MESSAGE_TAG" + a specific tag like CONFIRMED_EVENT_UPDATE or
    ///   ACCOUNT_UPDATE. Free-form text without a tag is rejected and risks page restrictions.
    ///
    /// We read <see cref="Models.ContactIdentity.LastSeenAtUtc"/> for the timestamp — the orchestrator
    /// touches that on every inbound (including referrals and postbacks, which Meta counts as
    /// user-initiated for window purposes).
    /// </summary>
    private async Task<(string? Type, string? Tag, string? FailureReason)> ResolveMessagingTypeAsync(
        string psid, MessageTag tag, CancellationToken ct)
    {
        var lastSeen = await _db.ContactIdentities
            .AsNoTracking()
            .Where(x => x.Channel == Channel.Messenger && x.ChannelIdentityValue == psid)
            .Select(x => (DateTime?)x.LastSeenAtUtc)
            .FirstOrDefaultAsync(ct);

        var windowOpen = lastSeen is { } seen && seen > DateTime.UtcNow.AddHours(-24);

        if (windowOpen)
        {
            // Within window: RESPONSE is the default and works for any text. If a tag was set by
            // the caller we still honor MESSAGE_TAG (some apps do this for analytics) — but only
            // when the tag is non-None.
            return tag == MessageTag.None
                ? ("RESPONSE", null, null)
                : ("MESSAGE_TAG", ToMetaTag(tag), null);
        }

        // Window closed: must send MESSAGE_TAG. If the caller didn't supply one, we can't proceed
        // — sending unsolicited free-form text outside the window is a TOS violation.
        if (tag == MessageTag.None)
            return (null, null, "Outside the 24h Messenger conversation window and no MessageTag was set. Use ReplyComposition.MessageTag to send.");

        return ("MESSAGE_TAG", ToMetaTag(tag), null);
    }

    /// <summary>Maps our <see cref="MessageTag"/> enum to Meta's tag string format.</summary>
    private static string ToMetaTag(MessageTag tag) => tag switch
    {
        MessageTag.ConfirmedEventUpdate => "CONFIRMED_EVENT_UPDATE",
        MessageTag.PostPurchaseUpdate => "POST_PURCHASE_UPDATE",
        MessageTag.AccountUpdate => "ACCOUNT_UPDATE",
        MessageTag.HumanAgent => "HUMAN_AGENT",
        _ => throw new ArgumentOutOfRangeException(nameof(tag), tag, "Unknown MessageTag"),
    };

    private static string TruncateIfNeeded(string text, int max)
        => text.Length <= max ? text : text[..(max - 1)] + "…";

    // WhatsApp uses *bold*, _italic_, ~strike~, `code` markers. Messenger renders them literally
    // (as actual asterisks/underscores) which looks broken. Strip them at the boundary so the
    // shared formatters (which target WhatsApp) still produce readable text on Messenger. We
    // match each pair non-greedily on a single line so legitimate stray markers don't disappear.
    private static readonly Regex BoldPattern = new(@"\*([^*\n]+)\*", RegexOptions.Compiled);
    private static readonly Regex ItalicPattern = new(@"(?<=^|\s)_([^_\n]+)_(?=\s|$|[.,!?;:])", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex StrikePattern = new(@"~([^~\n]+)~", RegexOptions.Compiled);
    private static readonly Regex CodePattern = new(@"`([^`\n]+)`", RegexOptions.Compiled);

    private static string StripWhatsAppMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = BoldPattern.Replace(text, "$1");
        text = ItalicPattern.Replace(text, "$1");
        text = StrikePattern.Replace(text, "$1");
        text = CodePattern.Replace(text, "$1");
        return text;
    }
}

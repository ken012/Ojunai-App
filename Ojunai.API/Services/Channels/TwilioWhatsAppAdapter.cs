using System.Security.Cryptography;
using System.Text;
using Ojunai.API.Models.Messaging;
using Ojunai.API.Services.Interfaces;

namespace Ojunai.API.Services.Channels;

/// <summary>
/// WhatsApp via Twilio. Wraps the existing <see cref="IWhatsAppService"/> for outbound — Phase 1
/// keeps the legacy bot logic untouched and only adds the abstraction surface so new channels
/// (Telegram, Messenger) can plug in. Future phases will gradually move logic from
/// WhatsAppService into the channel-blind orchestrator.
///
/// Inbound: parses Twilio's <c>application/x-www-form-urlencoded</c> webhook body into
/// <see cref="ConversationMessage"/> with the bare E.164 phone as SenderIdentity (the
/// <c>whatsapp:</c> prefix is added back here when sending — universal types stay clean).
///
/// Signature verification: HMAC-SHA1 of (URL + sorted-form-fields concatenated) using the
/// Twilio Auth Token. Honors X-Forwarded-* from Nginx so signed URL matches the proxied scheme/host.
/// </summary>
public sealed class TwilioWhatsAppAdapter : IChannelAdapter
{
    private readonly IWhatsAppService _whatsApp;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<TwilioWhatsAppAdapter> _logger;

    public TwilioWhatsAppAdapter(
        IWhatsAppService whatsApp,
        IConfiguration config,
        IWebHostEnvironment env,
        ILogger<TwilioWhatsAppAdapter> logger)
    {
        _whatsApp = whatsApp;
        _config = config;
        _env = env;
        _logger = logger;
    }

    public Channel Channel => Channel.Whatsapp;

    public ChannelCapabilities Capabilities { get; } = new(
        SupportsMedia: true,
        SupportsButtons: true,        // via approved templates only
        SupportsTypingIndicator: false, // not exposed by Twilio's WhatsApp API
        HasFreeServiceWindow: true,   // 24h after user-initiated message
        MaxTextLength: 1600);          // Twilio's per-message limit (longer messages auto-segment)

    public async Task<bool> VerifySignatureAsync(HttpRequest request)
    {
        // Dev-only bypass; production deploys never set this.
        if (_env.IsDevelopment() && _config.GetValue<bool>("Twilio:SkipSignatureValidation"))
            return true;

        var authToken = _config["Twilio:AuthToken"];
        if (string.IsNullOrEmpty(authToken))
        {
            _logger.LogError("Twilio:AuthToken not configured — webhook signature can't be verified");
            return false;
        }

        if (!request.Headers.TryGetValue("X-Twilio-Signature", out var providedSig))
            return false;

        // Reconstruct the URL Twilio signed. Honor X-Forwarded-* from Nginx so signed URL matches.
        var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;
        var host = request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? request.Host.Value;
        var url = $"{scheme}://{host}{request.Path}{request.QueryString}";

        request.Body.Position = 0;
        var form = await request.ReadFormAsync();
        var sb = new StringBuilder(url);
        foreach (var key in form.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            sb.Append(key).Append(form[key]);
        }

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(authToken));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        var expected = Convert.ToBase64String(hash);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(providedSig.ToString()));
    }

    public async Task<ConversationMessage?> ParseInboundAsync(HttpRequest request)
    {
        request.Body.Position = 0;
        var form = await request.ReadFormAsync();

        var from = form["From"].ToString();
        var body = form["Body"].ToString();
        var messageSid = form["MessageSid"].ToString();
        var profileName = form["ProfileName"].FirstOrDefault();

        // Twilio sends status callbacks (delivered, read, failed) on the same webhook URL —
        // those have no From or no MessageSid. Skip them.
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(messageSid))
            return null;

        // Strip the "whatsapp:" prefix so the universal type holds bare E.164.
        // Adapter re-adds it when sending; everything in between deals with clean phone numbers.
        var bareIdentity = from.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase)
            ? from["whatsapp:".Length..]
            : from;
        var normalized = WhatsAppService.NormalizePhone(bareIdentity);

        // Media — Twilio reports up to 10 media items as MediaUrl0..MediaUrl9 + matching MediaContentType*.
        var media = new List<MediaAttachment>();
        var numMedia = int.TryParse(form["NumMedia"].FirstOrDefault(), out var n) ? n : 0;
        for (var i = 0; i < numMedia; i++)
        {
            var mediaUrl = form[$"MediaUrl{i}"].FirstOrDefault();
            var mediaType = form[$"MediaContentType{i}"].FirstOrDefault() ?? "application/octet-stream";
            if (!string.IsNullOrEmpty(mediaUrl))
                media.Add(new MediaAttachment(mediaType, mediaUrl));
        }

        return new ConversationMessage
        {
            Channel = Channel.Whatsapp,
            ProviderMessageId = messageSid,
            SenderIdentity = normalized,
            SenderDisplayName = profileName,
            Text = string.IsNullOrEmpty(body) ? null : body,
            Media = media,
        };
    }

    public async Task<SendResult> SendAsync(string recipientIdentity, ReplyComposition reply, CancellationToken ct = default)
    {
        // Universal type holds bare E.164. WhatsAppService expects the "whatsapp:+234..." prefix
        // (it builds Twilio resources from it directly). Re-add the prefix here at the channel boundary.
        var to = recipientIdentity.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase)
            ? recipientIdentity
            : "whatsapp:" + recipientIdentity;

        try
        {
            // Phase 1 sends only text. Media + quick replies will be wired in Phase 2 when the
            // orchestrator actually composes ReplyComposition objects (right now WhatsAppService
            // sends its own messages directly, bypassing the adapter).
            await _whatsApp.SendMessageAsync(to, reply.Text);
            return new SendResult(Success: true, ProviderMessageId: null, FailureReason: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Twilio WhatsApp send failed for {To}", to);
            return new SendResult(Success: false, ProviderMessageId: null, FailureReason: ex.Message);
        }
    }
}

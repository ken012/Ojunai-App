using System.Security.Cryptography;
using System.Text;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Models.Messaging;
using Ojunai.API.Services.Channels;
using Ojunai.API.Services.Interfaces;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Controllers;

[AllowAnonymous]
[Route("api/webhooks")]
[ApiController]
public class WebhooksController : ControllerBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WebhooksController> _logger;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public WebhooksController(IServiceScopeFactory scopeFactory, ILogger<WebhooksController> logger, IConfiguration config, IWebHostEnvironment env)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _config = config;
        _env = env;
    }

    [HttpPost("whatsapp")]
    [Consumes("application/x-www-form-urlencoded")]
    [RequestSizeLimit(64 * 1024)] // 64KB — Twilio webhooks are small form posts
    public async Task<IActionResult> Receive([FromForm] TwilioInboundForm form)
    {
        // Phase 1 multi-channel rollout flag. When OFF, the legacy direct path runs (proven, untouched).
        // When ON, inbound flows through the new ConversationOrchestrator → still delegates to
        // IWhatsAppService underneath, but exercises the abstraction layer so we catch regressions
        // before turning on Telegram/Messenger production traffic. Flip via Multichannel__V1Enabled env.
        var useV1 = _config.GetValue<bool>("Multichannel:V1Enabled");

        if (useV1)
        {
            return await ReceiveViaOrchestratorAsync();
        }

        // ── Legacy path (default — preserves exact pre-Phase-1 behavior) ────────
        if (!await ValidateTwilioSignatureAsync())
        {
            _logger.LogWarning("Rejected webhook with invalid Twilio signature");
            return Unauthorized();
        }

        if (string.IsNullOrEmpty(form.From))
            return Ok(); // Status callback, ignore

        if (string.IsNullOrEmpty(form.Body))
        {
            // Media message (image, voice note, etc.) — reply with friendly message
            BackgroundJob.Enqueue<IWhatsAppService>(svc =>
                svc.SendMessageAsync(form.From, "I can only process text messages for now. Please type your request.", null, null));
            return Content("<Response/>", "text/xml");
        }

        // Do NOT log the full sender number or the raw message body at Information level — the body
        // routinely carries customer PII / financial detail and is attacker-controlled (newlines could
        // forge log lines). Log only the redacted sender; a sanitized short preview goes to Debug.
        _logger.LogInformation("Inbound WhatsApp from {From}", RedactSender(form.From));
        _logger.LogDebug("Inbound WhatsApp body preview: {Preview}", SanitizeLogPreview(form.Body));

        // Enqueue via Hangfire so the message is durable and retried on failure
        BackgroundJob.Enqueue<IWhatsAppService>(svc =>
            svc.HandleInboundAsync(form.From, form.MessageSid, form.Body));

        return Content("<Response/>", "text/xml");
    }

    /// <summary>
    /// Phase-1 path: signature-verify + parse via the channel adapter, then hand off to
    /// the channel-blind orchestrator. Behavior should be observably identical to the legacy
    /// path while the V1 flag is rolled out.
    /// </summary>
    private async Task<IActionResult> ReceiveViaOrchestratorAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IChannelRegistry>();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IConversationOrchestrator>();
        var adapter = registry.Get(Channel.Whatsapp);

        if (!await adapter.VerifySignatureAsync(Request))
        {
            _logger.LogWarning("Rejected WhatsApp webhook (V1 path): bad signature");
            return Unauthorized();
        }

        var message = await adapter.ParseInboundAsync(Request);
        if (message is null)
            return Content("<Response/>", "text/xml"); // status callback or non-message event

        if (string.IsNullOrEmpty(message.Text))
        {
            // Same friendly-error UX as the legacy path — media-only messages get a polite reply.
            BackgroundJob.Enqueue<IWhatsAppService>(svc =>
                svc.SendMessageAsync("whatsapp:" + message.SenderIdentity,
                    "I can only process text messages for now. Please type your request.", null, null));
            return Content("<Response/>", "text/xml");
        }

        _logger.LogInformation("Inbound WhatsApp (V1) from {From}", RedactSender(message.SenderIdentity));
        _logger.LogDebug("Inbound WhatsApp (V1) body preview: {Preview}", SanitizeLogPreview(message.Text));

        // Hangfire ensures the orchestrator call is durable and retried on transient failure,
        // matching the legacy path's reliability semantics.
        var providerMessageId = message.ProviderMessageId;
        var senderIdentity = message.SenderIdentity;
        var text = message.Text;
        BackgroundJob.Enqueue<IConversationOrchestrator>(o =>
            o.ProcessInboundAsync(
                new ConversationMessage
                {
                    Channel = Channel.Whatsapp,
                    ProviderMessageId = providerMessageId,
                    SenderIdentity = senderIdentity,
                    Text = text,
                },
                CancellationToken.None));

        return Content("<Response/>", "text/xml");
    }

    // ─── Messenger (Phase 0 stub) ──────────────────────────────────────────────
    //
    // Two endpoints required to register a Meta Messenger webhook:
    //   GET  — Meta's verification handshake. Echo back hub.challenge if our verify_token matches.
    //   POST — Meta sends messaging events. Phase 0 just logs to MessageLog so we have visibility
    //          while the app is in Meta's review queue. Phase 3 wires the full MessengerAdapter.
    //
    // Setting up on Meta's side requires us to expose this endpoint URL + a verify token.
    // Verify token is any string we choose — must match between our env file and Meta's dashboard.

    [HttpGet("messenger")]
    public IActionResult VerifyMessengerWebhook(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.challenge")] string? challenge,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken)
    {
        var expected = _config["Messenger:VerifyToken"];
        if (string.IsNullOrEmpty(expected))
        {
            _logger.LogError("Messenger:VerifyToken not configured — handshake will always fail");
            return StatusCode(500);
        }

        // Constant-time comparison so the token can't be brute-forced via timing.
        var ok = mode == "subscribe"
                 && verifyToken is not null
                 && CryptographicOperations.FixedTimeEquals(
                     Encoding.UTF8.GetBytes(verifyToken),
                     Encoding.UTF8.GetBytes(expected));

        if (!ok)
        {
            _logger.LogWarning("Messenger handshake rejected (mode={Mode})", mode);
            return Forbid();
        }

        // Meta expects the challenge value echoed back as plain text.
        return Content(challenge ?? "", "text/plain");
    }

    [HttpPost("messenger")]
    [RequestSizeLimit(256 * 1024)]
    public async Task<IActionResult> ReceiveMessenger()
    {
        // Phase 3a wires Messenger through the channel-blind orchestrator. The adapter verifies
        // X-Hub-Signature-256 internally; controller just buffers + audits + dispatches.
        using var scope = _scopeFactory.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IChannelRegistry>();
        if (!registry.TryGet(Channel.Messenger, out var adapter))
        {
            _logger.LogError("No Messenger adapter registered — accepting and dropping update");
            return Ok();
        }

        // Body has to be buffered before signature verification (HMAC over raw bytes) AND parse —
        // we read the stream twice. EnableBuffering lets us seek back to start.
        Request.EnableBuffering();
        Request.Body.Position = 0;
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        if (!await adapter.VerifySignatureAsync(Request))
        {
            _logger.LogWarning("Rejected Messenger webhook (bad X-Hub-Signature-256)");
            return Unauthorized();
        }

        // Audit log every inbound, post-verification. Mid-flight failures still have a paper trail.
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditLog = new MessageLog
        {
            Channel = "Messenger",
            Direction = MessageDirection.Inbound,
            RawMessage = Truncate(body, 4000),
            ProcessingStatus = MessageProcessingStatus.Received,
        };
        db.MessageLogs.Add(auditLog);
        await db.SaveChangesAsync();
        Request.Body.Position = 0;

        // Meta can batch multiple messaging events into one delivery; parse ALL of them so a second
        // quick message or a message+referral pair isn't silently dropped (the single 200 ack below
        // covers the whole batch, so dropped events are never re-delivered). Falls back to the single
        // interface method for any non-Messenger adapter.
        List<ConversationMessage> messages;
        if (adapter is Ojunai.API.Services.Channels.Messenger.MessengerAdapter mAdapter)
        {
            messages = await mAdapter.ParseAllInboundAsync(Request);
        }
        else
        {
            var single = await adapter.ParseInboundAsync(Request);
            messages = single is null ? new List<ConversationMessage>() : new List<ConversationMessage> { single };
        }
        if (messages.Count == 0)
        {
            // Non-message event (delivery receipt, read receipt, etc.) — already logged, ignore.
            return Ok();
        }

        var first = true;
        foreach (var message in messages)
        {
            // Idempotency — Meta retries on 5xx or slow 2xx responses, which means the same mid can
            // arrive multiple times. Without this check we'd Claude-parse twice and (worse) potentially
            // record a duplicate sale. Match the same dedup pattern WhatsApp uses (WhatsAppService.cs:297).
            if (!string.IsNullOrEmpty(message.ProviderMessageId))
            {
                var seenBefore = await db.MessageLogs.AnyAsync(l =>
                    l.Channel == "Messenger"
                    && l.Direction == MessageDirection.Inbound
                    && l.WhatsAppMessageId == message.ProviderMessageId
                    && l.Id != auditLog.Id);
                if (seenBefore)
                {
                    _logger.LogInformation("Dropping duplicate Messenger webhook for mid {Mid}", message.ProviderMessageId);
                    continue;
                }

                // Persist the FIRST event's mid on the shared audit log so the next retry's check finds
                // it; additional events in the batch are deduped by their own mid via their orchestrator
                // audit trail. (One audit row per delivery is retained.)
                if (first)
                {
                    auditLog.WhatsAppMessageId = message.ProviderMessageId;
                    await db.SaveChangesAsync();
                }
            }

            // Hangfire enqueue for durability and async handling — Meta wants a 200 within 5s.
            var msg = message;
            BackgroundJob.Enqueue<IConversationOrchestrator>(o =>
                o.ProcessInboundAsync(msg, CancellationToken.None));
            first = false;
        }

        return Ok();
    }

    // ─── Telegram (Phase 0 stub) ───────────────────────────────────────────────
    //
    // Telegram's webhook is a single POST endpoint. We verify the secret_token we set when
    // calling setWebhook so only Telegram (or someone who knows our secret) can hit this URL.
    //
    // Phase 0 just logs. Phase 2 wires the full TelegramAdapter with command parsing,
    // identity binding (/start <token>), and reply rendering with inline keyboards.

    [HttpPost("telegram")]
    [RequestSizeLimit(256 * 1024)]
    public async Task<IActionResult> ReceiveTelegram()
    {
        // Phase 2 wires Telegram through the channel-blind orchestrator. Adapter verifies the
        // X-Telegram-Bot-Api-Secret-Token header internally; controller stays thin.
        using var scope = _scopeFactory.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IChannelRegistry>();
        if (!registry.TryGet(Channel.Telegram, out var adapter))
        {
            // Should never happen if DI is wired; degrade gracefully so Telegram doesn't keep retrying.
            _logger.LogError("No Telegram adapter registered — accepting and dropping update");
            return Ok();
        }

        if (!await adapter.VerifySignatureAsync(Request))
        {
            _logger.LogWarning("Rejected Telegram webhook (bad secret token)");
            return Unauthorized();
        }

        // Always buffer so we can both log and parse — orchestrator may want the raw payload too
        // for debugging post-mortems.
        Request.EnableBuffering();
        Request.Body.Position = 0;
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        // Audit log every inbound update. Useful when a user reports "the bot ignored me" —
        // we have ground truth of what arrived even if the parse later fails.
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditLog = new MessageLog
        {
            Channel = "Telegram",
            Direction = MessageDirection.Inbound,
            RawMessage = Truncate(body, 4000),
            ProcessingStatus = MessageProcessingStatus.Received,
        };
        db.MessageLogs.Add(auditLog);
        await db.SaveChangesAsync();
        Request.Body.Position = 0;

        var message = await adapter.ParseInboundAsync(Request);
        if (message is null)
        {
            // Non-message update (channel post, my_chat_member event, etc.) — ignore.
            return Ok();
        }

        // Idempotency — Telegram retries on non-2xx and any timeout, so the same update_id /
        // message_id can arrive multiple times. Without this check we'd Claude-parse twice and
        // (worse) potentially record a duplicate sale. Same pattern as WhatsApp's dedup.
        if (!string.IsNullOrEmpty(message.ProviderMessageId))
        {
            var seenBefore = await db.MessageLogs.AnyAsync(l =>
                l.Channel == "Telegram"
                && l.Direction == MessageDirection.Inbound
                && l.WhatsAppMessageId == message.ProviderMessageId
                && l.Id != auditLog.Id);
            if (seenBefore)
            {
                _logger.LogInformation("Dropping duplicate Telegram webhook for message {Mid}", message.ProviderMessageId);
                return Ok();
            }

            auditLog.WhatsAppMessageId = message.ProviderMessageId;
            await db.SaveChangesAsync();
        }

        // Hangfire-enqueue the orchestrator call so processing is durable across restarts and
        // automatically retried on transient failure. Telegram considers anything other than 200
        // a failure and will retry the whole update — we want to ack fast, do work async.
        var msg = message;
        BackgroundJob.Enqueue<IConversationOrchestrator>(o =>
            o.ProcessInboundAsync(msg, CancellationToken.None));

        return Ok();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    // Redact an inbound sender identity (phone / MSISDN / chat id) to its last 4 digits for logging,
    // so customer phone numbers don't land in plaintext logs.
    private static string RedactSender(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "****";
        var digits = new string(s.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? $"***{digits[^4..]}" : "****";
    }

    // Sanitize a user-controlled message body for a Debug-only preview: strip CR/LF (prevents log-line
    // forgery) and cap the length so a huge/hostile body can't flood or spoof the log.
    private static string SanitizeLogPreview(string? body, int max = 120)
    {
        if (string.IsNullOrEmpty(body)) return "";
        var oneLine = body.Replace('\r', ' ').Replace('\n', ' ');
        return oneLine.Length <= max ? oneLine : oneLine[..max];
    }

    private async Task<bool> ValidateTwilioSignatureAsync()
    {
        // Allow bypass ONLY in Development environment
        if (_env.IsDevelopment() && _config.GetValue<bool>("Twilio:SkipSignatureValidation"))
            return true;

        var authToken = _config["Twilio:AuthToken"];
        if (string.IsNullOrEmpty(authToken))
        {
            _logger.LogError("Twilio:AuthToken not configured");
            return false;
        }

        if (!Request.Headers.TryGetValue("X-Twilio-Signature", out var providedSig))
            return false;

        // Reconstruct the URL Twilio signed. Honor X-Forwarded-* from Nginx.
        var scheme = Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? Request.Scheme;
        var host = Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? Request.Host.Value;
        var url = $"{scheme}://{host}{Request.Path}{Request.QueryString}";

        // Read form fields, sort by key, append to URL
        Request.Body.Position = 0;
        var form = await Request.ReadFormAsync();
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
}

public class TwilioInboundForm
{
    [System.ComponentModel.DataAnnotations.Required]
    public string From { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string MessageSid { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string AccountSid { get; set; } = string.Empty;
}

using System.Text.RegularExpressions;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Models.Messaging;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services.Channels;

/// <summary>
/// Channel-blind processing pipeline. Phase 1 wires this up but keeps WhatsApp behavior
/// unchanged: when a Whatsapp <see cref="ConversationMessage"/> arrives, we update
/// <c>ContactIdentity.LastSeenAtUtc</c> and then delegate the heavy lifting (intent parsing,
/// action dispatch, reply send) to the legacy <see cref="IWhatsAppService"/>. That keeps the
/// existing bot logic intact while we build out the abstraction surface.
///
/// Phase 2+ will introduce per-channel handlers for Telegram and Messenger that go through
/// this orchestrator end-to-end, composing <see cref="ReplyComposition"/> objects that are
/// rendered + sent by the channel adapter. Eventually WhatsApp logic gets ported in too.
/// </summary>
public sealed class ConversationOrchestrator : IConversationOrchestrator
{
    private readonly AppDbContext _db;
    private readonly IWhatsAppService _whatsApp;
    private readonly IChannelRegistry _channels;
    private readonly IChannelLinkingService _linking;
    private readonly Telegram.ITelegramIntentHandler _telegramIntent;
    private readonly Messenger.IMessengerIntentHandler _messengerIntent;
    private readonly ILogger<ConversationOrchestrator> _logger;

    public ConversationOrchestrator(
        AppDbContext db,
        IWhatsAppService whatsApp,
        IChannelRegistry channels,
        IChannelLinkingService linking,
        Telegram.ITelegramIntentHandler telegramIntent,
        Messenger.IMessengerIntentHandler messengerIntent,
        ILogger<ConversationOrchestrator> logger)
    {
        _db = db;
        _whatsApp = whatsApp;
        _channels = channels;
        _linking = linking;
        _telegramIntent = telegramIntent;
        _messengerIntent = messengerIntent;
        _logger = logger;
    }

    public async Task ProcessInboundAsync(ConversationMessage message, CancellationToken ct = default)
    {
        // 1. Update or create the ContactIdentity row so we know this human is reachable on this
        //    channel. This runs for every channel uniformly. UserId/BusinessId stay null until
        //    onboarding (or backfill, for existing WhatsApp users) wires them up.
        await TouchIdentityAsync(message, ct);

        // 2. Channel-specific dispatch. Phase 1 only Whatsapp is implemented; new channels arrive
        //    in Phase 2/3. Telegram and Messenger fall through to the not-implemented branch
        //    until their handlers ship.
        switch (message.Channel)
        {
            case Channel.Whatsapp:
                await DelegateToLegacyWhatsAppAsync(message);
                break;

            case Channel.Telegram:
                await HandleTelegramAsync(message, ct);
                break;

            case Channel.Messenger:
                await HandleMessengerAsync(message, ct);
                break;

            case Channel.Sms:
                _logger.LogInformation(
                    "Inbound {Channel} message logged but not yet handled (handler not implemented)",
                    message.Channel);
                break;

            default:
                _logger.LogWarning("Unknown channel {Channel} for message {Id}", message.Channel, message.Id);
                break;
        }
    }

    /// <summary>
    /// Phase-2 minimal Telegram handler. Routes:
    ///   - <c>/start &lt;token&gt;</c> → consume the link token, bind chat_id to a User, ack
    ///   - <c>/start</c> (no token, just bot DMed)        → onboarding prompt
    ///   - <c>/help</c>                                   → command list
    ///   - any other message from a linked user           → polite "feature coming soon" reply
    /// Full NL parsing for sale/expense/debt actions is queued for the next phase increment;
    /// Phase 2 establishes the binding + send-reply pipeline so we can validate end-to-end before
    /// porting the WhatsApp NL pipeline over.
    /// </summary>
    private async Task HandleTelegramAsync(ConversationMessage message, CancellationToken ct)
    {
        if (!_channels.TryGet(Channel.Telegram, out var adapter))
        {
            _logger.LogError("Telegram adapter not registered — can't reply");
            return;
        }

        var text = (message.Text ?? string.Empty).Trim();

        // /start [token] — Telegram's onboarding entry point. The token comes from the dashboard's
        // "Connect Telegram" button. Empty token = user opened the bot directly without going
        // through the dashboard flow.
        var startMatch = Regex.Match(text, @"^/start(?:@\w+)?(?:\s+(?<token>\S+))?\s*$", RegexOptions.IgnoreCase);
        if (startMatch.Success)
        {
            var token = startMatch.Groups["token"].Value;
            if (string.IsNullOrEmpty(token))
            {
                await adapter.SendAsync(message.SenderIdentity, new ReplyComposition
                {
                    Text =
                        "*Welcome to Ojunai* 🪬\n\n" +
                        "To connect this Telegram chat to your business, open the dashboard and " +
                        "tap *Settings → Connected Channels → Connect Telegram*. " +
                        "You'll get a link that brings you back here, all set up.\n\n" +
                        "Don't have an Ojunai account yet? Sign up at https://app.ojunai.com/register.",
                }, ct);
                return;
            }

            var bound = await _linking.ConsumeAsync(
                token,
                Channel.Telegram,
                message.SenderIdentity,
                message.SenderDisplayName,
                ct);

            if (bound is null)
            {
                await adapter.SendAsync(message.SenderIdentity, new ReplyComposition
                {
                    Text =
                        "That link is invalid or has expired (links last 30 minutes). " +
                        "Open the dashboard and tap *Connect Telegram* again to get a fresh one.",
                }, ct);
                return;
            }

            await adapter.SendAsync(message.SenderIdentity, new ReplyComposition
            {
                Text =
                    "✅ *Connected!* This Telegram chat is now linked to your Ojunai business.\n\n" +
                    "Reply *help* to see what I can do.",
            }, ct);
            return;
        }

        // /help or "help" — brief command list.
        if (text.Equals("/help", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            await adapter.SendAsync(message.SenderIdentity, new ReplyComposition
            {
                Text =
                    "*Ojunai on Telegram*\n\n" +
                    "Right now I'm in setup mode — I can connect you to your dashboard and acknowledge " +
                    "messages, but full sales/expenses parsing is coming next.\n\n" +
                    "_Available now:_\n" +
                    "• /start <token> — link this chat to your dashboard\n" +
                    "• /help — show this message\n\n" +
                    "_Coming soon:_\n" +
                    "• \"I sold 2 of X for Y\" — record a sale\n" +
                    "• \"I paid 3000 for printing\" — log an expense\n" +
                    "• \"Mary paid 5000\" — record a debt payment\n" +
                    "• PDF receipts delivered straight to this chat",
            }, ct);
            return;
        }

        // Anything else — natural language. Resolve the binding, hand to the intent handler.
        var identity = await _db.ContactIdentities
            .FirstOrDefaultAsync(
                x => x.Channel == Channel.Telegram && x.ChannelIdentityValue == message.SenderIdentity,
                ct);

        if (identity?.UserId is null)
        {
            await adapter.SendAsync(message.SenderIdentity, new ReplyComposition
            {
                Text =
                    "I haven't been connected to your Ojunai account yet. " +
                    "Open https://app.ojunai.com → *Settings → Connected Channels* and tap *Connect Telegram*.",
            }, ct);
            return;
        }

        // Phase 2.5: full NL pipeline. Resolves the message via Claude and dispatches to
        // ISalesService / IExpenseService / ILedgerService through the intent handler.
        await _telegramIntent.HandleAsync(message, identity, ct);
    }

    private async Task TouchIdentityAsync(ConversationMessage message, CancellationToken ct)
    {
        var identity = await _db.ContactIdentities
            .FirstOrDefaultAsync(
                x => x.Channel == message.Channel
                  && x.ChannelIdentityValue == message.SenderIdentity,
                ct);

        if (identity is null)
        {
            // First time we've seen this handle — create an unbound row. Onboarding (or the
            // legacy WhatsApp flow's user-lookup-by-phone) will bind UserId/BusinessId later.
            identity = new ContactIdentity
            {
                Channel = message.Channel,
                ChannelIdentityValue = message.SenderIdentity,
                DisplayName = message.SenderDisplayName,
                LinkedAtUtc = DateTime.UtcNow,
                LastSeenAtUtc = DateTime.UtcNow,
            };
            _db.ContactIdentities.Add(identity);
        }
        else
        {
            identity.LastSeenAtUtc = DateTime.UtcNow;
            // Update the friendly display name if the channel surfaced one we didn't have.
            if (!string.IsNullOrEmpty(message.SenderDisplayName) && identity.DisplayName != message.SenderDisplayName)
                identity.DisplayName = message.SenderDisplayName;
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task DelegateToLegacyWhatsAppAsync(ConversationMessage message)
    {
        // The legacy IWhatsAppService.HandleInboundAsync expects the Twilio "whatsapp:+234..."
        // format because it builds Twilio resources from it directly. Re-prefix at the boundary.
        var legacyFrom = "whatsapp:" + message.SenderIdentity;
        await _whatsApp.HandleInboundAsync(legacyFrom, message.ProviderMessageId, message.Text ?? string.Empty);
    }

    /// <summary>
    /// Phase 3b: Messenger handler with referral (m.me?ref=token) consumption.
    ///
    /// The adapter surfaces three Text shapes:
    ///   - <c>mref:&lt;token&gt;</c> — user reached the bot via an m.me deep link; consume the
    ///     ChannelLinkToken and bind the PSID to the dashboard User.
    ///   - regular text — Phase 3c will route this to a MessengerIntentHandler (sale/expense
    ///     parsing). For now, send a polite "we'll handle this soon" reply.
    ///   - quick-reply / postback payloads (currently unused until Phase 3c) — surface as text.
    /// </summary>
    private async Task HandleMessengerAsync(ConversationMessage message, CancellationToken ct)
    {
        if (!_channels.TryGet(Channel.Messenger, out var adapter))
        {
            _logger.LogError("Messenger adapter not registered — can't reply");
            return;
        }

        var text = (message.Text ?? string.Empty).Trim();

        // Linking flow — `mref:<token>` is the synthetic payload the adapter emits when a referral
        // event arrives. Consume the token, bind the user's PSID, ack via a sent message.
        if (text.StartsWith("mref:", StringComparison.Ordinal))
        {
            var token = text["mref:".Length..];
            var bound = await _linking.ConsumeAsync(
                token,
                Channel.Messenger,
                message.SenderIdentity,
                message.SenderDisplayName,
                ct);

            if (bound is null)
            {
                await adapter.SendAsync(message.SenderIdentity, new ReplyComposition
                {
                    Text =
                        "That link is invalid or has expired (links last 30 minutes). " +
                        "Open the Ojunai dashboard and tap Connect Messenger again to get a fresh one.",
                }, ct);
                return;
            }

            await adapter.SendAsync(message.SenderIdentity, new ReplyComposition
            {
                Text =
                    "✅ Connected! This Messenger chat is now linked to your Ojunai business.\n\n" +
                    "I'll be able to record sales, expenses, and payments via chat shortly — full handling rolls out next.",
            }, ct);
            return;
        }

        // Phase 3c: full NL pipeline. Resolves the binding, hands off to the intent handler.
        var identity = await _db.ContactIdentities
            .FirstOrDefaultAsync(
                x => x.Channel == Channel.Messenger && x.ChannelIdentityValue == message.SenderIdentity,
                ct);

        if (identity?.UserId is null)
        {
            await adapter.SendAsync(message.SenderIdentity, new ReplyComposition
            {
                Text =
                    "I haven't been connected to your Ojunai account yet. " +
                    "Open https://app.ojunai.com → Settings → Connected Channels and tap Connect Messenger.",
            }, ct);
            return;
        }

        await _messengerIntent.HandleAsync(message, identity, ct);
    }
}

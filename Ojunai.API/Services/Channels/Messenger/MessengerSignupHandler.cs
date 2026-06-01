using Ojunai.API.Data;
using Ojunai.API.Models.Messaging;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services.Channels.Messenger;

public interface IMessengerSignupHandler
{
    Task<bool> IsAwaitingSignupAsync(string psid, CancellationToken ct);
    Task StartAsync(string token, ConversationMessage message, IChannelAdapter adapter, CancellationToken ct);
    Task HandlePhoneAsync(ConversationMessage message, IChannelAdapter adapter, CancellationToken ct);
}

/// <summary>
/// Mirror of <see cref="Telegram.TelegramSignupHandler"/> for Messenger. Two-message flow:
///
///   1. User reaches the bot via m.me/{page}?ref=signup_xxx → MessengerAdapter surfaces it as
///      "mref:signup_xxx". <see cref="StartAsync"/> validates the token, claims the chat
///      (ConsumedByIdentity=PSID), and prompts for a phone number.
///   2. User replies with phone → <see cref="HandlePhoneAsync"/> calls AuthService to create
///      the User + Business, then sends a magic link the user opens to set a password.
///
/// Independent of <see cref="MessengerIntentHandler"/> — existing chat behavior for
/// already-linked users is untouched.
/// </summary>
public sealed class MessengerSignupHandler : IMessengerSignupHandler
{
    private readonly AppDbContext _db;
    private readonly AuthService _auth;
    private readonly IConfiguration _config;
    private readonly ILogger<MessengerSignupHandler> _logger;

    public MessengerSignupHandler(
        AppDbContext db,
        IAuthService auth,
        IConfiguration config,
        ILogger<MessengerSignupHandler> logger)
    {
        _db = db;
        _auth = (AuthService)auth;
        _config = config;
        _logger = logger;
    }

    public async Task<bool> IsAwaitingSignupAsync(string psid, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return await _db.SignupChannelTokens.AnyAsync(t =>
            t.Channel == Channel.Messenger
            && t.ConsumedByIdentity == psid
            && t.ConsumedAtUtc == null
            && t.ExpiresAtUtc > now, ct);
    }

    public async Task StartAsync(string token, ConversationMessage message, IChannelAdapter adapter, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var row = await _db.SignupChannelTokens.FirstOrDefaultAsync(t => t.Token == token, ct);

        if (row == null || row.Channel != Channel.Messenger)
        {
            await adapter.SendAsync(message.SenderIdentity, new ReplyComposition
            {
                Text = "That signup link is invalid. Start fresh from https://app.ojunai.com/register.",
            }, ct);
            return;
        }

        if (row.ExpiresAtUtc < now || row.ConsumedAtUtc.HasValue)
        {
            await adapter.SendAsync(message.SenderIdentity, new ReplyComposition
            {
                Text = "That signup link has expired or was already used. Start fresh from https://app.ojunai.com/register.",
            }, ct);
            return;
        }

        if (!string.IsNullOrEmpty(row.ConsumedByIdentity) && row.ConsumedByIdentity != message.SenderIdentity)
        {
            await adapter.SendAsync(message.SenderIdentity, new ReplyComposition
            {
                Text = "That signup link is being used by another chat. Get a fresh link from the dashboard.",
            }, ct);
            return;
        }

        row.ConsumedByIdentity = message.SenderIdentity;
        await _db.SaveChangesAsync(ct);

        await adapter.SendAsync(message.SenderIdentity, new ReplyComposition
        {
            Text =
                "Welcome to Ojunai! 🪬\n\n" +
                "I'll set up your account in two steps.\n\n" +
                "Step 1: Reply with your phone number including country code.\n" +
                "Example: +2348012345678",
        }, ct);
    }

    public async Task HandlePhoneAsync(ConversationMessage message, IChannelAdapter adapter, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var row = await _db.SignupChannelTokens.FirstOrDefaultAsync(t =>
            t.Channel == Channel.Messenger
            && t.ConsumedByIdentity == message.SenderIdentity
            && t.ConsumedAtUtc == null
            && t.ExpiresAtUtc > now, ct);

        if (row == null) return;

        var phoneText = (message.Text ?? string.Empty).Trim();
        var normalized = WhatsAppService.NormalizePhone(phoneText);
        if (string.IsNullOrEmpty(normalized))
        {
            await adapter.SendAsync(message.SenderIdentity, new ReplyComposition
            {
                Text = "That doesn't look like a valid phone number. Try again with country code, like +2348012345678.",
            }, ct);
            return;
        }

        var displayName = string.IsNullOrWhiteSpace(message.SenderDisplayName)
            ? "Owner"
            : message.SenderDisplayName!.Trim();
        var businessName = $"{displayName}'s Business";

        string magicJwt;
        try
        {
            magicJwt = await _auth.CompleteMessengerSignupAsync(
                row.Token, normalized, displayName, businessName, message.SenderIdentity, ct);
        }
        catch (InvalidOperationException ex)
        {
            await adapter.SendAsync(message.SenderIdentity, new ReplyComposition
            {
                Text = $"Couldn't complete signup: {ex.Message}",
            }, ct);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MessengerSignupHandler: CompleteMessengerSignupAsync failed for PSID {Psid}", message.SenderIdentity);
            await adapter.SendAsync(message.SenderIdentity, new ReplyComposition
            {
                Text = "Something went wrong creating your account. Try again from the dashboard.",
            }, ct);
            return;
        }

        var dashboardUrl = _config["App:DashboardUrl"] ?? "https://app.ojunai.com";
        var postSignupLink = $"{dashboardUrl}/post-signup?token={Uri.EscapeDataString(magicJwt)}";

        await adapter.SendAsync(message.SenderIdentity, new ReplyComposition
        {
            Text =
                "✅ Account created!\n\n" +
                $"Phone verified: {normalized}\n" +
                $"Business: {businessName} (you can rename it later)\n\n" +
                "Step 2 (final): Tap the link below to set your password and open the dashboard.\n" +
                $"{postSignupLink}\n\n" +
                "Link works for 30 minutes.",
        }, ct);
    }
}

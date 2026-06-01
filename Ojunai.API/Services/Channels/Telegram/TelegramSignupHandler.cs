using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Models.Messaging;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services.Channels.Telegram;

public interface ITelegramSignupHandler
{
    /// <summary>True if there is an in-flight signup reservation for this chat (set on /start,
    /// cleared on completion or expiry).</summary>
    Task<bool> IsAwaitingSignupAsync(string chatId, CancellationToken ct);

    /// <summary>Validates the signup token and reserves the chat. Sends the next-step prompt.</summary>
    Task StartAsync(string token, ConversationMessage message, IChannelAdapter adapter, CancellationToken ct);

    /// <summary>Treats the inbound text as the phone number and completes signup.</summary>
    Task HandlePhoneAsync(ConversationMessage message, IChannelAdapter adapter, CancellationToken ct);
}

/// <summary>
/// Two-message signup flow inside Telegram. Sequence:
///
///   1. User opens t.me/{bot}?start=signup_xxx → adapter delivers "/start signup_xxx".
///      <see cref="StartAsync"/> validates the token, claims the chat (ConsumedByIdentity=chatId),
///      and prompts for a phone number.
///   2. User replies with phone → <see cref="HandlePhoneAsync"/> calls AuthService to create
///      the User + Business, then sends a magic link the user opens to set a password.
///
/// This handler is independent of <see cref="TelegramIntentHandler"/> — existing chat behavior
/// (sale recording, inventory queries, etc.) for already-linked users is untouched.
/// </summary>
public sealed class TelegramSignupHandler : ITelegramSignupHandler
{
    private readonly AppDbContext _db;
    private readonly AuthService _auth;
    private readonly IConfiguration _config;
    private readonly ILogger<TelegramSignupHandler> _logger;

    public TelegramSignupHandler(
        AppDbContext db,
        IAuthService auth,
        IConfiguration config,
        ILogger<TelegramSignupHandler> logger)
    {
        _db = db;
        _auth = (AuthService)auth;
        _config = config;
        _logger = logger;
    }

    public async Task<bool> IsAwaitingSignupAsync(string chatId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return await _db.SignupChannelTokens.AnyAsync(t =>
            t.Channel == Channel.Telegram
            && t.ConsumedByIdentity == chatId
            && t.ConsumedAtUtc == null
            && t.ExpiresAtUtc > now, ct);
    }

    public async Task StartAsync(string token, ConversationMessage message, IChannelAdapter adapter, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var row = await _db.SignupChannelTokens.FirstOrDefaultAsync(t => t.Token == token, ct);

        if (row == null || row.Channel != Channel.Telegram)
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

        // Reserve the chat against this token. If another chat already claimed it (rare race
        // when the same link is shared), refuse politely.
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
                "*Welcome to Ojunai!* 🪬\n\n" +
                "I'll set up your account in two steps.\n\n" +
                "*Step 1:* Reply with your phone number including country code.\n" +
                "Example: `+2348012345678`",
        }, ct);
    }

    public async Task HandlePhoneAsync(ConversationMessage message, IChannelAdapter adapter, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var row = await _db.SignupChannelTokens.FirstOrDefaultAsync(t =>
            t.Channel == Channel.Telegram
            && t.ConsumedByIdentity == message.SenderIdentity
            && t.ConsumedAtUtc == null
            && t.ExpiresAtUtc > now, ct);

        if (row == null)
        {
            // No pending signup — shouldn't get here from the orchestrator's guard, but be safe.
            return;
        }

        var phoneText = (message.Text ?? string.Empty).Trim();
        var normalized = WhatsAppService.NormalizePhone(phoneText);
        if (string.IsNullOrEmpty(normalized))
        {
            await adapter.SendAsync(message.SenderIdentity, new ReplyComposition
            {
                Text = "That doesn't look like a valid phone number. Try again with country code, like `+2348012345678`.",
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
            magicJwt = await _auth.CompleteTelegramSignupAsync(
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
            _logger.LogError(ex, "TelegramSignupHandler: CompleteTelegramSignupAsync failed for chat {Chat}", message.SenderIdentity);
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
                "✅ *Account created!*\n\n" +
                $"Phone verified: `{normalized}`\n" +
                $"Business: `{businessName}` (you can rename it later)\n\n" +
                "*Step 2 (final):* Tap the link below to set your password and open the dashboard.\n" +
                $"{postSignupLink}\n\n" +
                "_Link works for 30 minutes._",
        }, ct);
    }
}

using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Models.Messaging;
using Ojunai.API.Services.Channels;
using Ojunai.API.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Controllers;

/// <summary>
/// Authenticated endpoints for managing connected channels (Telegram for now, Messenger next).
/// The dashboard's Settings → Connected Channels card calls these to mint deep links the user
/// follows to bind their account to a channel-native identity, list current bindings, and
/// disconnect.
/// </summary>
[Authorize]
[Route("api/channels")]
[ApiController]
public class ChannelsController : OjunaiBaseController
{
    private readonly IChannelLinkingService _linking;
    private readonly AppDbContext _db;
    private readonly ILogger<ChannelsController> _logger;
    private readonly IActivityLogger _activity;

    public ChannelsController(IChannelLinkingService linking, AppDbContext db, ILogger<ChannelsController> logger, IActivityLogger activity)
    {
        _linking = linking;
        _db = db;
        _logger = logger;
        _activity = activity;
    }

    /// <summary>
    /// Returns connection status for each channel the dashboard cares about. Drives the Settings →
    /// Connected Channels UI so users can see what's wired up and what isn't.
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<ApiResponse<ChannelStatusResponse>>> GetStatus()
    {
        // Pull every ContactIdentity row bound to this user. Most users will have at most one per
        // channel; if they have multiple (edge case for businesses with separate work/personal
        // chats), we surface the most-recent one for that channel.
        var identities = await _db.ContactIdentities
            .Where(x => x.UserId == UserId)
            .OrderByDescending(x => x.LinkedAtUtc)
            .ToListAsync();

        ChannelBindingStatus FindStatus(Channel channel)
        {
            var row = identities.FirstOrDefault(x => x.Channel == channel);
            return row is null
                ? new ChannelBindingStatus { Connected = false }
                : new ChannelBindingStatus
                {
                    Connected = true,
                    DisplayName = row.DisplayName,
                    ConnectedAtUtc = row.LinkedAtUtc,
                    LastSeenAtUtc = row.LastSeenAtUtc,
                };
        }

        var response = new ChannelStatusResponse
        {
            Whatsapp = FindStatus(Channel.Whatsapp),
            Telegram = FindStatus(Channel.Telegram),
            Messenger = FindStatus(Channel.Messenger),
        };
        return Ok(ApiResponse<ChannelStatusResponse>.Ok(response));
    }

    /// <summary>
    /// Mints a fresh Telegram link token for the calling user and returns the full deep-link.
    /// The user opens the link, taps "Start" in Telegram, and the bot fires <c>/start &lt;token&gt;</c>
    /// which the orchestrator consumes to bind the chat_id to this user's account.
    /// </summary>
    [HttpPost("telegram/link")]
    public async Task<ActionResult<ApiResponse<TelegramLinkResponse>>> CreateTelegramLink()
    {
        var deepLink = await _linking.CreateLinkAsync(UserId, BusinessId, Channel.Telegram);
        return Ok(ApiResponse<TelegramLinkResponse>.Ok(
            new TelegramLinkResponse { DeepLink = deepLink },
            "Open the link, then tap Start in Telegram."));
    }

    /// <summary>
    /// Unbinds the current user's Telegram identity. The ContactIdentity row stays around so we
    /// don't lose audit history (LastSeenAtUtc, DisplayName, etc.) but UserId/BusinessId are
    /// cleared — the bot will treat that chat as "not linked" again. The user can re-bind any
    /// time by generating a fresh link.
    /// </summary>
    [HttpDelete("telegram")]
    public async Task<ActionResult<ApiResponse<object>>> DisconnectTelegram()
    {
        var rows = await _db.ContactIdentities
            .Where(x => x.UserId == UserId && x.Channel == Channel.Telegram)
            .ToListAsync();

        foreach (var row in rows)
        {
            row.UserId = null;
            row.BusinessId = null;
        }

        if (rows.Count > 0)
            await _activity.LogAsync(BusinessId, "channel.disconnected", "Channel", UserId, "Telegram", "disconnected Telegram");
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(null!, "Telegram disconnected."));
    }

    /// <summary>
    /// Same as the Telegram version but for Messenger. Mints a one-time ref token, returns the
    /// <c>m.me/&lt;page&gt;?ref=&lt;token&gt;</c> deep link. The user opens it in Messenger, the
    /// referral event fires our webhook, the orchestrator consumes the token and binds the PSID.
    /// </summary>
    [HttpPost("messenger/link")]
    public async Task<ActionResult<ApiResponse<MessengerLinkResponse>>> CreateMessengerLink()
    {
        var deepLink = await _linking.CreateLinkAsync(UserId, BusinessId, Channel.Messenger);
        return Ok(ApiResponse<MessengerLinkResponse>.Ok(
            new MessengerLinkResponse { DeepLink = deepLink },
            "Open the link to start a chat with the page in Messenger."));
    }

    /// <summary>Mirror of DisconnectTelegram for Messenger PSIDs.</summary>
    [HttpDelete("messenger")]
    public async Task<ActionResult<ApiResponse<object>>> DisconnectMessenger()
    {
        var rows = await _db.ContactIdentities
            .Where(x => x.UserId == UserId && x.Channel == Channel.Messenger)
            .ToListAsync();

        foreach (var row in rows)
        {
            row.UserId = null;
            row.BusinessId = null;
        }

        if (rows.Count > 0)
            await _activity.LogAsync(BusinessId, "channel.disconnected", "Channel", UserId, "Messenger", "disconnected Messenger");
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(null!, "Messenger disconnected."));
    }
}

public class TelegramLinkResponse
{
    public string DeepLink { get; set; } = string.Empty;
}

public class MessengerLinkResponse
{
    public string DeepLink { get; set; } = string.Empty;
}

public class ChannelStatusResponse
{
    public ChannelBindingStatus Whatsapp { get; set; } = new();
    public ChannelBindingStatus Telegram { get; set; } = new();
    public ChannelBindingStatus Messenger { get; set; } = new();
}

public class ChannelBindingStatus
{
    public bool Connected { get; set; }
    public string? DisplayName { get; set; }
    public DateTime? ConnectedAtUtc { get; set; }
    public DateTime? LastSeenAtUtc { get; set; }
}

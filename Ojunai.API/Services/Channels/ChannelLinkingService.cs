using System.Security.Cryptography;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Models.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services.Channels;

/// <summary>
/// Implementation of <see cref="IChannelLinkingService"/>. Token = 32-byte URL-safe random
/// (44 chars). 30-minute expiry — generous for "click link, tap Start" but tight enough that
/// an intercepted token isn't replayable indefinitely. Single-use enforced by the unique
/// constraint plus the ConsumedAtUtc check.
/// </summary>
public sealed class ChannelLinkingService : IChannelLinkingService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);

    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<ChannelLinkingService> _logger;

    public ChannelLinkingService(AppDbContext db, IConfiguration config, ILogger<ChannelLinkingService> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task<string> CreateLinkAsync(Guid userId, Guid businessId, Channel channel, CancellationToken ct = default)
    {
        var token = GenerateUrlSafeToken();
        var row = new ChannelLinkToken
        {
            UserId = userId,
            BusinessId = businessId,
            Channel = channel,
            Token = token,
            ExpiresAtUtc = DateTime.UtcNow.Add(TokenLifetime),
        };
        _db.ChannelLinkTokens.Add(row);
        await _db.SaveChangesAsync(ct);

        return BuildDeepLink(channel, token);
    }

    public async Task<(Guid UserId, Guid BusinessId)?> ConsumeAsync(
        string token,
        Channel channel,
        string channelIdentity,
        string? displayName,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var row = await _db.ChannelLinkTokens
            .FirstOrDefaultAsync(
                t => t.Token == token && t.Channel == channel,
                ct);

        if (row is null)
        {
            _logger.LogWarning("ConsumeAsync: token not found ({Channel})", channel);
            return null;
        }

        if (row.ConsumedAtUtc is not null)
        {
            _logger.LogWarning("ConsumeAsync: token already consumed at {At}", row.ConsumedAtUtc);
            return null;
        }

        if (row.ExpiresAtUtc < now)
        {
            _logger.LogWarning("ConsumeAsync: token expired at {At}", row.ExpiresAtUtc);
            return null;
        }

        // Bind the identity. Find existing row by (channel, value) — could pre-exist if the user
        // had messaged the bot before clicking the dashboard link, in which case it has UserId=null.
        var identity = await _db.ContactIdentities
            .FirstOrDefaultAsync(
                x => x.Channel == channel && x.ChannelIdentityValue == channelIdentity,
                ct);

        if (identity is null)
        {
            identity = new ContactIdentity
            {
                Channel = channel,
                ChannelIdentityValue = channelIdentity,
            };
            _db.ContactIdentities.Add(identity);
        }

        identity.UserId = row.UserId;
        identity.BusinessId = row.BusinessId;
        identity.DisplayName = displayName ?? identity.DisplayName;
        identity.LinkedAtUtc = now;
        identity.LastSeenAtUtc = now;

        // Mark the token consumed so the same value can't be replayed by a different chat_id.
        row.ConsumedAtUtc = now;
        row.BoundToIdentity = channelIdentity;

        await _db.SaveChangesAsync(ct);
        return (row.UserId, row.BusinessId);
    }

    private string BuildDeepLink(Channel channel, string token) => channel switch
    {
        Channel.Telegram =>
            // Bot username configured in env (e.g. "OjunaiBot"). Fallback is a sensible default but
            // production should always set it explicitly.
            $"https://t.me/{_config["Telegram:BotUsername"] ?? "OjunaiBot"}?start={token}",

        // Messenger ref-token deep links. m.me accepts either a Page username (m.me/ojunai?ref=…)
        // or a numeric Page ID (m.me/61589770371791?ref=…). We use PageUsername when set, falling
        // back to PageId for Pages that haven't earned a custom handle yet. When the user opens
        // the link, Meta launches Messenger; the bot's first interaction fires a messaging_referrals
        // event containing our token in the `ref` field — the orchestrator consumes it to bind
        // the user's PSID to the Ojunai account.
        Channel.Messenger =>
            $"https://m.me/{_config["Messenger:PageUsername"] ?? _config["Messenger:PageId"] ?? throw new InvalidOperationException("Neither Messenger:PageUsername nor Messenger:PageId configured")}?ref={token}",

        _ => throw new NotSupportedException($"Deep link generation not supported for channel {channel}"),
    };

    private static string GenerateUrlSafeToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}

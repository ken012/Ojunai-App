using Ojunai.API.Data;
using Ojunai.API.Models.Messaging;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services.Channels;

/// <summary>
/// Default implementation. Reads <c>User.AlertChannel</c>, resolves the matching
/// <see cref="Models.ContactIdentity"/>, and pushes through that channel's adapter.
///
/// Failure modes:
///   - User has Telegram preference but no Telegram ContactIdentity → fall back to WhatsApp
///   - User has Telegram preference + identity, but Telegram send fails → fall back to WhatsApp
///   - User has no WhatsApp phone either → log error, swallow (notifications are best-effort)
///
/// We never throw — alerts are non-critical. A failing notification shouldn't crash the
/// Hangfire job that triggered it.
/// </summary>
public sealed class NotificationDispatcher : INotificationDispatcher
{
    private readonly AppDbContext _db;
    private readonly IChannelRegistry _channels;
    private readonly IWhatsAppService _whatsAppLegacy; // WhatsApp path still goes through the legacy service for now
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        AppDbContext db,
        IChannelRegistry channels,
        IWhatsAppService whatsAppLegacy,
        ILogger<NotificationDispatcher> logger)
    {
        _db = db;
        _channels = channels;
        _whatsAppLegacy = whatsAppLegacy;
        _logger = logger;
    }

    public async Task<Channel> SendToUserAsync(Guid userId, ReplyComposition reply, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);
        if (user is null)
        {
            _logger.LogWarning("NotificationDispatcher: user {UserId} not found or inactive", userId);
            return Channel.Whatsapp; // nothing sent; report default
        }

        // Resolve preferred channel from User.AlertChannel string. Anything we don't recognize
        // (legacy data, future channels) defaults to WhatsApp — safest backward-compatible behavior.
        var preferred = ParseChannel(user.AlertChannel);

        if (preferred == Channel.Telegram || preferred == Channel.Messenger)
        {
            // Look up the identity for this user on the preferred channel. Multiple is possible
            // but rare; pick the most-recently-active.
            var identity = await _db.ContactIdentities
                .Where(x => x.UserId == userId && x.Channel == preferred)
                .OrderByDescending(x => x.LastSeenAtUtc ?? x.LinkedAtUtc)
                .FirstOrDefaultAsync(ct);

            if (identity is not null && _channels.TryGet(preferred, out var adapter))
            {
                // For Messenger, alerts often arrive outside the user's 24h conversation window
                // (e.g. a daily summary at 8am when the last user message was yesterday afternoon).
                // Default MessageTag = AccountUpdate covers most alert use cases (low-stock notices,
                // billing reminders, daily summaries) without violating Meta's policy — callers can
                // override by setting reply.MessageTag explicitly before calling us.
                var outboundReply = preferred == Channel.Messenger && reply.MessageTag == MessageTag.None
                    ? reply with { MessageTag = MessageTag.AccountUpdate }
                    : reply;

                var result = await adapter.SendAsync(identity.ChannelIdentityValue, outboundReply, ct);
                if (result.Success) return preferred;

                _logger.LogWarning("{Channel} send failed for user {UserId}; falling back to WhatsApp: {Reason}",
                    preferred, userId, result.FailureReason);
            }
            else
            {
                _logger.LogInformation("User {UserId} preferred {Channel} but has no binding; falling back to WhatsApp",
                    userId, preferred);
            }
        }

        // WhatsApp fallback (also the default path when AlertChannel = "whatsapp").
        if (string.IsNullOrEmpty(user.PhoneNumber))
        {
            _logger.LogWarning("Can't send to user {UserId} — no phone number on record", userId);
            return Channel.Whatsapp;
        }

        try
        {
            await _whatsAppLegacy.SendMessageAsync("whatsapp:" + user.PhoneNumber, reply.Text, user.BusinessId, userId);
            return Channel.Whatsapp;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhatsApp send failed for user {UserId}", userId);
            return Channel.Whatsapp;
        }
    }

    public async Task<Channel> SendToPhoneAsync(string phone, ReplyComposition reply, CancellationToken ct = default)
    {
        // For phone-addressed sends we look up the User first so we can honor their preference.
        // Strip any "whatsapp:" prefix the caller may have included.
        var normalized = phone.StartsWith("whatsapp:") ? phone["whatsapp:".Length..] : phone;
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.PhoneNumber == normalized && u.IsActive, ct);
        if (user is not null) return await SendToUserAsync(user.Id, reply, ct);

        // No user matches → straight WhatsApp send (legacy semantics for unknown numbers).
        try
        {
            await _whatsAppLegacy.SendMessageAsync("whatsapp:" + normalized, reply.Text);
            return Channel.Whatsapp;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhatsApp send failed for phone {Phone}", normalized);
            return Channel.Whatsapp;
        }
    }

    public async Task<IReadOnlyList<Channel>> SendToAllUserChannelsAsync(Guid userId, ReplyComposition reply, CancellationToken ct = default)
    {
        var sent = new List<Channel>();
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);
        if (user is null)
        {
            _logger.LogWarning("NotificationDispatcher: user {UserId} not found or inactive", userId);
            return sent;
        }

        // Bot channels — send to each bound identity (skip any the user hasn't connected).
        foreach (var channel in new[] { Channel.Telegram, Channel.Messenger })
        {
            var identity = await _db.ContactIdentities
                .Where(x => x.UserId == userId && x.Channel == channel)
                .OrderByDescending(x => x.LastSeenAtUtc ?? x.LinkedAtUtc)
                .FirstOrDefaultAsync(ct);
            if (identity is null || !_channels.TryGet(channel, out var adapter)) continue;

            // Same 24h-window guard as SendToUserAsync — default Messenger alerts to AccountUpdate.
            var outboundReply = channel == Channel.Messenger && reply.MessageTag == MessageTag.None
                ? reply with { MessageTag = MessageTag.AccountUpdate }
                : reply;
            try
            {
                var result = await adapter.SendAsync(identity.ChannelIdentityValue, outboundReply, ct);
                if (result.Success) sent.Add(channel);
                else _logger.LogWarning("{Channel} send failed for user {UserId}: {Reason}", channel, userId, result.FailureReason);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Channel} send threw for user {UserId}", channel, userId);
            }
        }

        // WhatsApp — the baseline channel; send whenever we have a phone on record.
        if (!string.IsNullOrEmpty(user.PhoneNumber))
        {
            try
            {
                await _whatsAppLegacy.SendMessageAsync("whatsapp:" + user.PhoneNumber, reply.Text, user.BusinessId, userId);
                sent.Add(Channel.Whatsapp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WhatsApp send failed for user {UserId}", userId);
            }
        }

        if (sent.Count == 0)
            _logger.LogWarning("SendToAllUserChannels: no reachable channel for user {UserId}", userId);

        return sent;
    }

    private static Channel ParseChannel(string? raw) => raw?.ToLowerInvariant() switch
    {
        "telegram" => Channel.Telegram,
        "messenger" => Channel.Messenger,
        "sms" => Channel.Sms,
        _ => Channel.Whatsapp,
    };
}

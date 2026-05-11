using System.Security.Cryptography;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services.Channels.Telegram;

public sealed class PendingTelegramActionService : IPendingTelegramActionService
{
    /// <summary>30 minutes is plenty for a "tap Yes/No" decision; tight enough that intercepted
    /// tokens can't be sat on indefinitely.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly AppDbContext _db;
    private readonly ILogger<PendingTelegramActionService> _logger;

    public PendingTelegramActionService(AppDbContext db, ILogger<PendingTelegramActionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<string> CreateAsync(
        Guid businessId,
        Guid userId,
        string chatId,
        string actionType,
        string payloadJson,
        CancellationToken ct = default)
    {
        var token = GenerateToken();
        _db.PendingTelegramActions.Add(new PendingTelegramAction
        {
            BusinessId = businessId,
            UserId = userId,
            ChatId = chatId,
            ActionType = actionType,
            Token = token,
            PayloadJson = payloadJson,
            ExpiresAtUtc = DateTime.UtcNow.Add(Lifetime),
        });
        await _db.SaveChangesAsync(ct);
        return token;
    }

    public async Task<PendingActionConsumeResult?> ConsumeAsync(string token, string chatId, CancellationToken ct = default)
    {
        var row = await _db.PendingTelegramActions
            .FirstOrDefaultAsync(x => x.Token == token, ct);

        if (row is null)
        {
            _logger.LogWarning("PendingTelegramAction not found: {Token}", token);
            return null;
        }
        if (row.ConsumedAtUtc is not null)
        {
            _logger.LogWarning("PendingTelegramAction already consumed: {Token}", token);
            return null;
        }
        if (row.ExpiresAtUtc < DateTime.UtcNow)
        {
            _logger.LogWarning("PendingTelegramAction expired: {Token}", token);
            return null;
        }
        if (row.ChatId != chatId)
        {
            // Cross-chat replay attempt. Refuse and log.
            _logger.LogWarning("PendingTelegramAction chat mismatch: expected {Expected}, got {Got}", row.ChatId, chatId);
            return null;
        }

        row.ConsumedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new PendingActionConsumeResult(row.BusinessId, row.UserId, row.ActionType, row.PayloadJson);
    }

    public async Task CancelAsync(string token, string chatId, CancellationToken ct = default)
    {
        var row = await _db.PendingTelegramActions
            .FirstOrDefaultAsync(x => x.Token == token && x.ChatId == chatId, ct);
        if (row is null) return;
        row.ConsumedAtUtc = DateTime.UtcNow; // treat cancel as consumed so it can't be replayed
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>16 url-safe characters from 12 bytes of randomness. Plenty of entropy for a
    /// 30-minute-lifetime token, fits easily inside Telegram's 64-byte callback_data limit
    /// even with prefixes ("pa:yes:" etc).</summary>
    private static string GenerateToken()
    {
        var bytes = new byte[12];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}

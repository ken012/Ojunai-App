using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Models.Messaging;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ojunai.API.Services;

public interface IInboundDedupService
{
    /// <summary>
    /// Atomically claims an inbound message for processing. Returns <c>true</c> if THIS caller
    /// won the claim and should proceed; <c>false</c> if the message was already claimed (a
    /// provider re-delivery or a Hangfire retry) and should be skipped.
    /// Messages with no provider id can't be deduped — they return <c>true</c> (proceed).
    /// </summary>
    Task<bool> TryClaimAsync(Channel channel, string? providerMessageId, CancellationToken ct = default);
}

/// <summary>
/// DB-backed exactly-once gate for inbound messages. The claim is an INSERT into
/// <see cref="InboundMessageClaim"/> (composite PK = Channel + ProviderMessageId); a duplicate
/// loses on the unique-violation and is told to skip.
///
/// Uses a SHORT-LIVED dedicated DbContext (its own scope) rather than the caller's context so a
/// 23505 on insert can't leave the caller's change-tracker in a poisoned state. The claim is
/// committed before the caller does any real work, so it must be placed BEFORE the paid Claude
/// call — that way a retry of a failed-after-claim job skips re-calling Claude rather than
/// re-billing it.
/// </summary>
public sealed class InboundDedupService : IInboundDedupService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InboundDedupService> _logger;

    public InboundDedupService(IServiceScopeFactory scopeFactory, ILogger<InboundDedupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<bool> TryClaimAsync(Channel channel, string? providerMessageId, CancellationToken ct = default)
    {
        // No id to dedup on — allow through (matches prior behavior, which only skipped on a
        // matching id). Rare for real provider messages; common for synthetic/internal sends.
        if (string.IsNullOrEmpty(providerMessageId))
            return true;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.InboundMessageClaims.Add(new InboundMessageClaim
        {
            Channel = channel,
            ProviderMessageId = providerMessageId,
            ClaimedAtUtc = DateTime.UtcNow,
        });

        try
        {
            await db.SaveChangesAsync(ct);
            return true; // we inserted the claim → we own this message
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _logger.LogInformation(
                "Duplicate inbound {Channel} message {Mid} — already claimed, skipping",
                channel, providerMessageId);
            return false; // someone already claimed it → skip
        }
        // Any OTHER DbUpdateException (transient DB error, etc.) propagates so Hangfire can retry
        // the whole job — the claim wasn't committed, so the retry will re-attempt cleanly.
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

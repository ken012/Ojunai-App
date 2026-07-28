using Ojunai.API.Data;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Common;

/// <summary>
/// Runs a stock-mutating operation in a Serializable transaction with bounded retry, so it serializes against
/// concurrent Serializable writers (notably stock transfers, and other stock ops) on the shared
/// <c>ProductLocationStock</c> row: Postgres SSI aborts one of a conflicting pair with a serialization failure
/// (40001) / deadlock (40P01) / unique-violation race (23505), and this retries it transparently on a cleared
/// change tracker.
///
/// TRANSACTION-AWARE: if the caller is ALREADY inside a transaction (e.g. the WhatsApp bot's bulk-restock path,
/// which opens its own transaction and then loops these services), this simply runs the work inside that
/// ambient transaction — no nested transaction (unsupported), no double-commit, no retry (the owner handles all
/// three). So wrapping a service method in this is safe whether it's called standalone or nested.
///
/// The wrapped <paramref name="work"/> does its own reads + mutations and ends with SaveChangesAsync; this owns
/// the transaction + commit only when it opened one. On EF InMemory (tests) the transaction is a no-op.
/// </summary>
public static class DbRetry
{
    public static async Task<T> SerializableAsync<T>(AppDbContext db, Func<Task<T>> work, int maxAttempts = 4)
    {
        // Already in a transaction → participate; the owner controls isolation/commit/retry.
        if (db.Database.CurrentTransaction != null)
            return await work();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                var result = await work();
                await tx.CommitAsync();
                return result;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsRetryable(ex))
            {
                db.ChangeTracker.Clear();
                await Task.Delay(15 * attempt);
            }
        }
    }

    public static Task SerializableAsync(AppDbContext db, Func<Task> work, int maxAttempts = 4)
        => SerializableAsync<object?>(db, async () => { await work(); return null; }, maxAttempts);

    /// <summary>Concurrency conflicts worth retrying: the Product rowversion optimistic-concurrency conflict, a
    /// Postgres serialization failure (40001) or deadlock (40P01), or a unique-violation from two first-time
    /// destination/PLS rows racing (23505 — the loser retries and finds the row).</summary>
    public static bool IsRetryable(Exception ex)
    {
        for (var e = (Exception?)ex; e != null; e = e.InnerException)
        {
            if (e is DbUpdateConcurrencyException) return true;
            if (e is Npgsql.PostgresException pg && pg.SqlState is "40001" or "40P01" or "23505") return true;
        }
        return false;
    }
}

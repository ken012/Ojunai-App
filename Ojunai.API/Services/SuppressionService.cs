using Microsoft.EntityFrameworkCore;
using Ojunai.API.Data;
using Ojunai.API.Models;

namespace Ojunai.API.Services;

public interface ISuppressionService
{
    /// <summary>
    /// True if this address is on the suppression list. Callers should treat the email as
    /// undeliverable and skip sending — calling SES anyway would harm sender reputation.
    /// </summary>
    Task<bool> IsSuppressedAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// Records an address as undeliverable. Upserts — duplicate notifications are a no-op.
    /// </summary>
    Task SuppressAsync(
        string email,
        string reason,
        string? bounceType = null,
        string? bounceSubType = null,
        string? rawPayload = null,
        CancellationToken ct = default);
}

public class SuppressionService : ISuppressionService
{
    private readonly AppDbContext _db;
    private readonly ILogger<SuppressionService> _logger;

    public SuppressionService(AppDbContext db, ILogger<SuppressionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> IsSuppressedAsync(string email, CancellationToken ct = default)
    {
        var normalized = Normalize(email);
        if (normalized.Length == 0) return false;
        return await _db.SuppressedEmails.AnyAsync(s => s.Email == normalized, ct);
    }

    public async Task SuppressAsync(
        string email,
        string reason,
        string? bounceType = null,
        string? bounceSubType = null,
        string? rawPayload = null,
        CancellationToken ct = default)
    {
        var normalized = Normalize(email);
        if (normalized.Length == 0) return;

        var existing = await _db.SuppressedEmails
            .FirstOrDefaultAsync(s => s.Email == normalized, ct);

        if (existing != null)
        {
            // Re-suppression — refresh the metadata in case the bounce type changed
            // (e.g. transient now turning permanent) but don't reset the original timestamp.
            existing.Reason = reason;
            existing.BounceType = bounceType ?? existing.BounceType;
            existing.BounceSubType = bounceSubType ?? existing.BounceSubType;
            if (!string.IsNullOrEmpty(rawPayload)) existing.RawPayload = rawPayload;
        }
        else
        {
            _db.SuppressedEmails.Add(new SuppressedEmail
            {
                Id = Guid.NewGuid(),
                Email = normalized,
                Reason = reason,
                BounceType = bounceType,
                BounceSubType = bounceSubType,
                RawPayload = rawPayload,
                SuppressedAtUtc = DateTime.UtcNow,
            });
            _logger.LogInformation(
                "Suppressing email {Email} reason={Reason} bounceType={BounceType}",
                normalized, reason, bounceType ?? "n/a");
        }

        await _db.SaveChangesAsync(ct);
    }

    private static string Normalize(string? email)
        => (email ?? string.Empty).Trim().ToLowerInvariant();
}

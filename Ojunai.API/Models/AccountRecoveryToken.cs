namespace Ojunai.API.Models;

/// <summary>
/// One row per phone-loss recovery request. Issued only when a user's verified email matches
/// an inbound recovery request, so the token represents proof of inbox control.
///
/// Lifecycle:
///   1. POST /auth/request-account-recovery → row created if email is verified, link emailed
///   2. POST /auth/recover-account/info     → token validated, info returned (NOT consumed)
///   3. POST /auth/recover-account/* (reset-password OR change-phone) → token consumed, action applied
///
/// On consumption: User.TokenVersion bumped → all existing JWT sessions invalidated.
/// </summary>
public class AccountRecoveryToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    public string HashedToken { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }

    /// <summary>IP that requested the recovery — kept for audit / fraud review.</summary>
    public string? RequestIp { get; set; }

    public User User { get; set; } = null!;
}

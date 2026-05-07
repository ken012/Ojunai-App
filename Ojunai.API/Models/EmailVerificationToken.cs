namespace Ojunai.API.Models;

/// <summary>
/// One row per email-verification request. Tokens are long random URL-safe strings (not 6-digit codes
/// — emails are clicked, not retyped). Stored as BCrypt hashes; never store the raw token.
///
/// Lifecycle:
///   1. POST /auth/request-email-verification (or auto on signup) → row created, link emailed
///   2. GET  /auth/verify-email?token=...    → row consumed, User.EmailVerified set true
///
/// The token is the *recovery channel guarantee* — if a user can produce it, we trust they own
/// the inbox we sent it to.
/// </summary>
public class EmailVerificationToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    /// <summary>BCrypt hash of the token. The raw token only exists in the user's email.</summary>
    public string HashedToken { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }

    public User User { get; set; } = null!;
}

namespace Ojunai.API.Models;

/// <summary>
/// One row per phone-verification request issued during dashboard signup.
///
/// Lifecycle:
///   1. POST /auth/request-phone-verification → row created, OTP sent via WhatsApp
///   2. POST /auth/verify-phone-and-register → row consumed (UsedAtUtc set), User+Business created
///
/// Codes are stored as BCrypt hashes — same approach as PasswordResetCode on the User table.
/// Expired or used rows are kept for a short window for audit, then swept.
/// </summary>
public class PhoneVerificationCode
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>E.164-normalized phone (e.g. +2348012345678).</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>BCrypt hash of the 6-digit code. Never store the raw code.</summary>
    public string HashedCode { get; set; } = string.Empty;

    /// <summary>What this code is for — drives the registration check + WhatsApp copy.</summary>
    public PhoneVerificationPurpose Purpose { get; set; } = PhoneVerificationPurpose.SignupVerification;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }

    /// <summary>How many wrong codes the caller has tried. Locked at 5.</summary>
    public int Attempts { get; set; } = 0;
}

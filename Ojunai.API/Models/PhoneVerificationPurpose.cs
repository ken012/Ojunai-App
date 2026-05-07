namespace Ojunai.API.Models;

/// <summary>
/// Why a phone-verification code was issued. Drives:
/// - Whether the phone must already be registered (PasswordReset = yes; SignupVerification = no)
/// - Rate-limit bucketing (counts kept per purpose, so a signup OTP doesn't block a reset OTP)
/// - WhatsApp message copy
/// </summary>
public enum PhoneVerificationPurpose
{
    /// <summary>New-account signup OR phone-change-during-recovery — both require an unregistered phone.</summary>
    SignupVerification = 1,

    /// <summary>Owner/Admin self-initiated password reset — requires an active registered phone.</summary>
    PasswordReset = 2,
}

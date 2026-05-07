using Ojunai.API.Models;

namespace Ojunai.API.Services.Interfaces;

public interface IPhoneVerificationService
{
    /// <summary>
    /// Issues a 6-digit OTP for the given phone, stores its BCrypt hash, and sends the code via WhatsApp.
    /// Throws if the caller is rate-limited (too many recent requests in the same purpose bucket).
    ///
    /// Purpose drives behavior:
    /// - SignupVerification: rejects if phone IS already registered.
    /// - PasswordReset: rejects if phone is NOT registered (no point sending a reset code to a stranger).
    /// </summary>
    Task<(string NormalizedPhone, DateTime ExpiresAtUtc, int CooldownSeconds)> RequestCodeAsync(
        string phoneNumber, PhoneVerificationPurpose purpose = PhoneVerificationPurpose.SignupVerification);

    /// <summary>
    /// Validates the OTP. On success, marks the row used. Only matches against codes issued
    /// for the same purpose, so a signup code can't accidentally satisfy a reset prompt.
    /// </summary>
    Task ConsumeCodeAsync(
        string phoneNumber, string code, PhoneVerificationPurpose purpose = PhoneVerificationPurpose.SignupVerification);
}

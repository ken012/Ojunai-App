using Ojunai.API.DTOs.Auth;

namespace Ojunai.API.Services.Interfaces;

public interface IAccountRecoveryService
{
    /// <summary>
    /// If a user with this verified email exists, issues a recovery token and emails the link.
    /// Always returns successfully without revealing whether the email exists (anti-enumeration).
    /// </summary>
    Task RequestRecoveryAsync(string email, string? ipAddress);

    /// <summary>
    /// Validates the token without consuming. Used by the recovery UI to populate "you're
    /// recovering account for X" before the user picks an action.
    /// </summary>
    Task<RecoveryTokenInfo> InspectTokenAsync(string rawToken);

    /// <summary>
    /// Completes recovery via password reset. Consumes the token, bumps TokenVersion, returns
    /// fresh auth response so the user is logged in.
    /// </summary>
    Task<AuthResponse> CompletePasswordResetAsync(string rawToken, string newPassword);

    /// <summary>
    /// Step 1 of phone change: validates the recovery token, ensures the new phone is free,
    /// and sends a 6-digit OTP via WhatsApp to the new phone. Does NOT consume the recovery
    /// token — the user still has to verify the OTP.
    /// </summary>
    Task<DateTime> RequestPhoneChangeOtpAsync(string rawToken, string newPhoneNumber);

    /// <summary>
    /// Step 2 of phone change: verifies the OTP and recovery token together, swaps the phone,
    /// consumes the recovery token, bumps TokenVersion, returns fresh auth response.
    /// </summary>
    Task<AuthResponse> CompletePhoneChangeAsync(string rawToken, string newPhoneNumber, string code);
}

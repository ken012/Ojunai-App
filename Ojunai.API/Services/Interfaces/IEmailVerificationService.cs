namespace Ojunai.API.Services.Interfaces;

public interface IEmailVerificationService
{
    /// <summary>
    /// Issues a verification token, stores its BCrypt hash, and emails a clickable link
    /// to the user's address. Throws if the user has no email or the SMTP layer is unconfigured.
    /// Rate-limited per user (60s cooldown, hourly cap).
    /// </summary>
    Task<DateTime> SendVerificationEmailAsync(Guid userId);

    /// <summary>
    /// Validates the token. On success, marks the user's email verified and consumes the token.
    /// Idempotent: replaying a successful token after the user is already verified throws nothing
    /// new — caller can present a generic success page.
    /// </summary>
    Task ConsumeTokenAsync(string rawToken);
}

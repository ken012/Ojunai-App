using System.Security.Cryptography;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Auth;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services;

/// <summary>
/// Phone-loss recovery flow. The user proves they own the inbox we have on file (via a verified
/// email + a clickable token) and from there can either reset the password (keeping their current
/// phone) or change the phone on the account (after OTP-verifying the new phone via WhatsApp).
///
/// Security guardrails:
///   - Email MUST be verified — unverified emails are silently ignored to prevent
///     account-takeover via attacker-supplied emails.
///   - Recovery requests are rate-limited per email (5min cooldown, 3/day cap).
///   - Recovery tokens are 32-byte URL-safe random, BCrypt-hashed at rest, 30-min lifetime, single-use.
///   - On any successful completion the user's TokenVersion is bumped, killing all existing JWTs.
///   - Phone change requires a fresh OTP to the *new* phone. Token validation runs on every step.
///   - Anti-enumeration: RequestRecoveryAsync always succeeds; we don't reveal whether the email matched.
/// </summary>
public class AccountRecoveryService : IAccountRecoveryService
{
    private readonly AppDbContext _db;
    private readonly IEmailService _email;
    private readonly IPhoneVerificationService _phoneVerify;
    private readonly AuthService _auth;
    private readonly IAlertService _alerts;
    private readonly IConfiguration _config;
    private readonly ILogger<AccountRecoveryService> _logger;

    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RequestCooldown = TimeSpan.FromMinutes(5);
    private const int MaxRequestsPerDay = 3;

    public AccountRecoveryService(
        AppDbContext db,
        IEmailService email,
        IPhoneVerificationService phoneVerify,
        IAuthService auth,
        IAlertService alerts,
        IConfiguration config,
        ILogger<AccountRecoveryService> logger)
    {
        _db = db;
        _email = email;
        _phoneVerify = phoneVerify;
        _auth = (AuthService)auth;
        _alerts = alerts;
        _config = config;
        _logger = logger;
    }

    public async Task RequestRecoveryAsync(string email, string? ipAddress)
    {
        var normalized = email.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized)) return; // silently ignore

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == normalized && u.IsActive && u.EmailVerified);

        // Always return without revealing match. Logs help us see real attempts in audit.
        if (user == null)
        {
            _logger.LogInformation("Account recovery requested for unmatched email {Email}", normalized);
            return;
        }

        if (!_email.IsConfigured)
        {
            _logger.LogWarning("Account recovery requested but email is not configured");
            return;
        }

        var now = DateTime.UtcNow;

        // Cooldown — 5 minutes between issuances per user.
        var lastIssued = await _db.AccountRecoveryTokens
            .Where(t => t.UserId == user.Id)
            .OrderByDescending(t => t.CreatedAtUtc)
            .FirstOrDefaultAsync();
        if (lastIssued != null && (now - lastIssued.CreatedAtUtc) < RequestCooldown)
        {
            _logger.LogInformation("Recovery cooldown hit for user {UserId}", user.Id);
            return;
        }

        // Daily cap — 3 per user per day.
        var dayCount = await _db.AccountRecoveryTokens
            .CountAsync(t => t.UserId == user.Id && t.CreatedAtUtc > now.AddDays(-1));
        if (dayCount >= MaxRequestsPerDay)
        {
            _logger.LogWarning("Recovery daily cap hit for user {UserId}", user.Id);
            return;
        }

        var rawToken = GenerateUrlSafeToken(32);
        var row = new AccountRecoveryToken
        {
            UserId = user.Id,
            HashedToken = BCrypt.Net.BCrypt.HashPassword(rawToken),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(TokenLifetime),
            RequestIp = ipAddress,
        };
        _db.AccountRecoveryTokens.Add(row);
        await _db.SaveChangesAsync();

        var dashboardUrl = (_config["Urls:Dashboard"] ?? "https://app.ojunai.com").TrimEnd('/');
        var link = $"{dashboardUrl}/recover?token={rawToken}";
        var maskedPhone = MaskPhone(user.PhoneNumber);

        var html = $@"
<!doctype html><html><body style=""font-family: -apple-system, BlinkMacSystemFont, sans-serif; color: #0F172A; max-width: 560px; margin: 0 auto; padding: 24px;"">
  <h2 style=""color: #0F172A;"">Recover your Ojunai account</h2>
  <p>Hi {WebEncode(user.FullName)},</p>
  <p>You (or someone) requested account recovery for the Ojunai account ending <strong>{maskedPhone}</strong>. If this was you, click the button below to reset your password or change your phone number.</p>
  <p style=""margin: 24px 0;"">
    <a href=""{link}"" style=""display: inline-block; background: #06B6D4; color: white; padding: 12px 24px; border-radius: 8px; text-decoration: none; font-weight: 600;"">Recover account</a>
  </p>
  <p style=""color: #64748B; font-size: 13px;"">Or paste this link into your browser:<br><a href=""{link}"" style=""color: #06B6D4;"">{link}</a></p>
  <p style=""color: #DC2626; font-size: 13px; margin-top: 32px;"">This link expires in 30 minutes. If you didn't request this, ignore the email — your account is safe.</p>
</body></html>";

        var plain = $"Recover your Ojunai account ending {maskedPhone} by visiting:\n\n{link}\n\nLink expires in 30 minutes. If you didn't request this, ignore this email.";

        try
        {
            await _email.SendAsync(user.Email!, user.FullName, "Recover your Ojunai account", html, plain);
            _logger.LogInformation("Recovery email sent for user {UserId} from ip {Ip}", user.Id, ipAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send recovery email for user {UserId}", user.Id);
            // Swallow — we already returned to the caller as 204. Token row stays so the user can
            // request again after the cooldown without being permanently broken.
        }
    }

    public async Task<RecoveryTokenInfo> InspectTokenAsync(string rawToken)
    {
        var (row, user) = await ResolveActiveTokenAsync(rawToken);
        return new RecoveryTokenInfo
        {
            FullName = user.FullName,
            MaskedPhone = MaskPhone(user.PhoneNumber),
            MaskedEmail = MaskEmail(user.Email ?? ""),
            BusinessName = user.Business?.Name ?? "",
        };
    }

    public async Task<AuthResponse> CompletePasswordResetAsync(string rawToken, string newPassword)
    {
        var (pwOk, pwReason) = PasswordPolicy.Validate(newPassword);
        if (!pwOk) throw new InvalidOperationException(pwReason!);

        var (row, user) = await ResolveActiveTokenAsync(rawToken);

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.MustChangePassword = false;
        user.TokenVersion++; // kill all existing sessions
        user.FailedLoginAttempts = 0;
        user.LockoutEndsAtUtc = null;

        row.UsedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Account recovery: password reset for user {UserId}", user.Id);

        await _alerts.CreateAsync(
            user.BusinessId, user.Id,
            AlertType.AccountRecoveryUsed, AlertSeverity.Critical,
            title: "Account recovery used",
            body: "Your password was just reset via email recovery. If this wasn't you, contact support immediately.",
            linkUrl: "/settings#account");

        if (!string.IsNullOrEmpty(user.Email))
        {
            await _email.TrySendSecurityNotificationAsync(
                user.Email, user.FullName,
                action: "Account recovery used (password reset)",
                detail: "Your password was just reset using the email-recovery flow.");
        }

        return _auth.BuildAuthResponsePublic(user, user.Business!, overrideMustChange: false);
    }

    public async Task<DateTime> RequestPhoneChangeOtpAsync(string rawToken, string newPhoneNumber)
    {
        var (row, user) = await ResolveActiveTokenAsync(rawToken);

        var newPhone = WhatsAppService.NormalizePhone(newPhoneNumber);
        if (string.IsNullOrEmpty(newPhone))
            throw new InvalidOperationException("Valid phone number required.");

        if (newPhone == user.PhoneNumber)
            throw new InvalidOperationException("That's already your current phone number. Use \"Reset password\" instead.");

        var conflictingActive = await _db.Users.AnyAsync(u => u.PhoneNumber == newPhone && u.IsActive && u.Id != user.Id);
        if (conflictingActive)
            throw new InvalidOperationException("That phone number belongs to another account.");

        // Free a deactivated holder so the OTP/swap path is unobstructed (mirror of OnboardingService).
        var deactivated = await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == newPhone && !u.IsActive);
        if (deactivated != null)
        {
            deactivated.PhoneNumber = $"x{deactivated.Id.ToString("N")[..18]}";
            await _db.SaveChangesAsync();
        }

        // Defer to PhoneVerificationService — same WhatsApp delivery, same rate limits as signup.
        var (_, expiresAt, _) = await _phoneVerify.RequestCodeAsync(newPhone);
        return expiresAt;
    }

    public async Task<AuthResponse> CompletePhoneChangeAsync(string rawToken, string newPhoneNumber, string code)
    {
        var (row, user) = await ResolveActiveTokenAsync(rawToken);

        var newPhone = WhatsAppService.NormalizePhone(newPhoneNumber);
        if (string.IsNullOrEmpty(newPhone))
            throw new InvalidOperationException("Valid phone number required.");

        // Re-check uniqueness in case state changed since the OTP was requested.
        var conflictingActive = await _db.Users.AnyAsync(u => u.PhoneNumber == newPhone && u.IsActive && u.Id != user.Id);
        if (conflictingActive)
            throw new InvalidOperationException("That phone number belongs to another account.");

        await _phoneVerify.ConsumeCodeAsync(newPhone, code);

        user.PhoneNumber = newPhone;
        user.TokenVersion++; // kill all existing sessions
        user.FailedLoginAttempts = 0;
        user.LockoutEndsAtUtc = null;

        row.UsedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Account recovery: phone changed for user {UserId} to {Phone}", user.Id, MaskPhone(newPhone));

        await _alerts.CreateAsync(
            user.BusinessId, user.Id,
            AlertType.AccountRecoveryUsed, AlertSeverity.Critical,
            title: "Phone number changed via recovery",
            body: $"Your account phone was changed via email recovery to {MaskPhone(newPhone)}. If this wasn't you, contact support immediately.",
            linkUrl: "/settings#account");

        if (!string.IsNullOrEmpty(user.Email))
        {
            await _email.TrySendSecurityNotificationAsync(
                user.Email, user.FullName,
                action: "Phone number changed via recovery",
                detail: $"Your account phone was just changed to {MaskPhone(newPhone)}.");
        }

        return _auth.BuildAuthResponsePublic(user, user.Business!, overrideMustChange: false);
    }

    /// <summary>
    /// Validates a recovery token by scanning unused, unexpired rows and BCrypt-comparing.
    /// Returns the matching row and its user (with Business included). Throws on no match.
    /// </summary>
    private async Task<(AccountRecoveryToken Row, User User)> ResolveActiveTokenAsync(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            throw new UnauthorizedAccessException("Recovery token missing or invalid.");

        var now = DateTime.UtcNow;
        var candidates = await _db.AccountRecoveryTokens
            .Include(t => t.User).ThenInclude(u => u.Business)
            .Where(t => t.UsedAtUtc == null && t.ExpiresAtUtc > now)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Take(50)
            .ToListAsync();

        var match = candidates.FirstOrDefault(t => BCrypt.Net.BCrypt.Verify(rawToken, t.HashedToken));
        if (match == null || !match.User.IsActive)
            throw new UnauthorizedAccessException("This recovery link is invalid or has expired. Please request a new one.");

        return (match, match.User);
    }

    private static string GenerateUrlSafeToken(int byteCount)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteCount);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string MaskPhone(string? phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 4) return "****";
        return new string('*', phone.Length - 4) + phone[^4..];
    }

    private static string MaskEmail(string email)
    {
        if (string.IsNullOrEmpty(email) || !email.Contains('@')) return "****";
        var parts = email.Split('@');
        var local = parts[0].Length <= 2 ? parts[0] : parts[0][0] + new string('*', parts[0].Length - 2) + parts[0][^1];
        var domain = parts[1];
        var dot = domain.IndexOf('.');
        if (dot > 0)
            domain = domain[0] + new string('*', Math.Max(1, dot - 1)) + domain[dot..];
        return $"{local}@{domain}";
    }

    private static string WebEncode(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
}

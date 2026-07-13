using System.Security.Cryptography;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services;

/// <summary>
/// Issues clickable email-verification links and consumes returning tokens. Mirrors the
/// PhoneVerificationService shape but uses long URL-safe random tokens instead of 6-digit
/// codes — emails are clicked, not retyped.
///
/// Verification is required for any email to be usable as the account-recovery channel
/// when a user loses access to their phone. Until verified, the email exists on the user
/// row but isn't trusted for password reset or phone-loss recovery.
/// </summary>
public class EmailVerificationService : IEmailVerificationService
{
    private readonly AppDbContext _db;
    private readonly IEmailService _email;
    private readonly IConfiguration _config;
    private readonly IAlertService _alerts;
    private readonly ILogger<EmailVerificationService> _logger;

    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan HourlyWindow = TimeSpan.FromHours(1);
    private const int MaxRequestsPerHour = 5;

    public EmailVerificationService(
        AppDbContext db,
        IEmailService email,
        IConfiguration config,
        IAlertService alerts,
        ILogger<EmailVerificationService> logger)
    {
        _db = db;
        _email = email;
        _config = config;
        _alerts = alerts;
        _logger = logger;
    }

    public async Task<DateTime> SendVerificationEmailAsync(Guid userId)
    {
        if (!_email.IsConfigured)
            throw new InvalidOperationException("Email is not configured on the server. Verification emails cannot be sent.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive)
            ?? throw new KeyNotFoundException("User not found.");

        if (string.IsNullOrEmpty(user.Email))
            throw new InvalidOperationException("This account has no email on file.");

        if (user.EmailVerified)
            throw new InvalidOperationException("Email is already verified.");

        var now = DateTime.UtcNow;

        // Resend cooldown — 60s between issues per user.
        var lastIssued = await _db.EmailVerificationTokens
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAtUtc)
            .FirstOrDefaultAsync();
        if (lastIssued != null && (now - lastIssued.CreatedAtUtc) < ResendCooldown)
        {
            var waitSec = (int)Math.Ceiling((ResendCooldown - (now - lastIssued.CreatedAtUtc)).TotalSeconds);
            throw new InvalidOperationException($"Please wait {waitSec}s before requesting another email.");
        }

        // Hourly limit — 5 emails per user per hour.
        var hourCount = await _db.EmailVerificationTokens
            .CountAsync(t => t.UserId == userId && t.CreatedAtUtc > now - HourlyWindow);
        if (hourCount >= MaxRequestsPerHour)
            throw new InvalidOperationException("Too many verification emails sent. Please try again in an hour.");

        // 32 bytes = 256 bits of entropy. Plenty for a single-use, expiring token.
        var rawToken = GenerateUrlSafeToken(32);
        var row = new EmailVerificationToken
        {
            UserId = userId,
            HashedToken = BCrypt.Net.BCrypt.HashPassword(rawToken),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(TokenLifetime),
        };
        _db.EmailVerificationTokens.Add(row);
        await _db.SaveChangesAsync();

        var dashboardUrl = (_config["Urls:Dashboard"] ?? "https://app.ojunai.com").TrimEnd('/');
        // Emit "{rowId:N}.{secret}" so ConsumeTokenAsync can load exactly one row by its indexed PK
        // instead of scanning the newest-50 tokens globally (which fails closed once the pending pool
        // exceeds 50 — realistic at scale given 24h token lifetime).
        var link = $"{dashboardUrl}/verify-email?token={row.Id:N}.{rawToken}";

        var html = $@"
<!doctype html><html><body style=""font-family: -apple-system, BlinkMacSystemFont, sans-serif; color: #0F172A; max-width: 560px; margin: 0 auto; padding: 24px;"">
  <h2 style=""color: #0F172A;"">Verify your Ojunai email</h2>
  <p>Hi {WebEncode(user.FullName)},</p>
  <p>Click the button below to verify <strong>{WebEncode(user.Email)}</strong>. This is the recovery channel you'll use if you ever lose access to your phone number.</p>
  <p style=""margin: 24px 0;"">
    <a href=""{link}"" style=""display: inline-block; background: #06B6D4; color: white; padding: 12px 24px; border-radius: 8px; text-decoration: none; font-weight: 600;"">Verify email</a>
  </p>
  <p style=""color: #64748B; font-size: 13px;"">Or paste this link into your browser:<br><a href=""{link}"" style=""color: #06B6D4;"">{link}</a></p>
  <p style=""color: #64748B; font-size: 13px; margin-top: 32px;"">This link expires in 24 hours. If you didn't request this, you can ignore the email.</p>
</body></html>";

        var plain = $"Verify your Ojunai email by visiting:\n\n{link}\n\nThis link expires in 24 hours. If you didn't request this, ignore the email.";

        try
        {
            await _email.SendAsync(user.Email, user.FullName, "Verify your Ojunai email", html, plain);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send verification email to user {UserId}", userId);
            throw new InvalidOperationException("Couldn't send verification email. Please try again in a moment.");
        }

        _logger.LogInformation("Verification email sent for user {UserId}", userId);
        return row.ExpiresAtUtc;
    }

    public async Task ConsumeTokenAsync(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            throw new InvalidOperationException("Verification token missing.");

        var now = DateTime.UtcNow;

        // Fast path: new tokens are "{rowId:N}.{secret}". Load exactly one row by its indexed PK and
        // BCrypt-verify only its hash — no global scan, so it can't fail closed once the pending pool
        // exceeds the scan cap. A malformed/foreign id misses here and drops to the legacy scan.
        EmailVerificationToken? match = null;
        var dot = rawToken.IndexOf('.');
        if (dot > 0 && Guid.TryParse(rawToken[..dot], out var rowId))
        {
            var secret = rawToken[(dot + 1)..];
            var byId = await _db.EmailVerificationTokens
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.Id == rowId && t.UsedAtUtc == null && t.ExpiresAtUtc > now);
            if (byId != null && BCrypt.Net.BCrypt.Verify(secret, byId.HashedToken))
                match = byId;
        }

        // Legacy fallback: pre-selector tokens (whole-token hash, no '.'). BCrypt hashes can't be looked
        // up by equality (per-salt), so scan the newest unused/unexpired rows and compare.
        if (match == null)
        {
            var candidates = await _db.EmailVerificationTokens
                .Include(t => t.User)
                .Where(t => t.UsedAtUtc == null && t.ExpiresAtUtc > now)
                .OrderByDescending(t => t.CreatedAtUtc)
                .Take(50)
                .ToListAsync();
            match = candidates.FirstOrDefault(t => BCrypt.Net.BCrypt.Verify(rawToken, t.HashedToken));
        }

        if (match == null)
            throw new UnauthorizedAccessException("This verification link is invalid or has expired. Please request a new one.");

        match.UsedAtUtc = now;
        match.User.EmailVerified = true;
        match.User.EmailVerifiedAtUtc = now;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Email verified for user {UserId}", match.UserId);

        await _alerts.CreateAsync(
            match.User.BusinessId, match.UserId,
            AlertType.EmailVerified, AlertSeverity.Info,
            title: "Email verified",
            body: $"{match.User.Email} is now verified. You can use it for account recovery if you lose your phone.",
            linkUrl: "/settings#account");
    }

    private static string GenerateUrlSafeToken(int byteCount)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteCount);
        // Base64url: '+' → '-', '/' → '_', strip '='.
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    // Minimal HTML escaping — we control the rest of the template, only user-supplied
    // values need escaping.
    private static string WebEncode(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
}

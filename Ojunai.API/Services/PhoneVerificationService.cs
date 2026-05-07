using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services;

/// <summary>
/// Issues and verifies one-time codes via WhatsApp. Two distinct purposes share this service:
/// SignupVerification (must NOT be a registered phone) and PasswordReset (MUST be registered).
/// Rate-limit counters are bucketed per purpose so the two flows don't starve each other.
///
/// Channel: WhatsApp via the existing IWhatsAppService. The number sending the OTP is the same
/// Twilio sender used for everything else, so production deliverability requires a registered
/// authentication template before the bot can message a user who hasn't opted in.
/// </summary>
public class PhoneVerificationService : IPhoneVerificationService
{
    private readonly AppDbContext _db;
    private readonly IWhatsAppService _whatsApp;
    private readonly ILogger<PhoneVerificationService> _logger;

    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan HourlyWindow = TimeSpan.FromHours(1);
    private const int MaxRequestsPerHour = 5;
    private const int MaxAttemptsPerCode = 5;

    public PhoneVerificationService(AppDbContext db, IWhatsAppService whatsApp, ILogger<PhoneVerificationService> logger)
    {
        _db = db;
        _whatsApp = whatsApp;
        _logger = logger;
    }

    public async Task<(string NormalizedPhone, DateTime ExpiresAtUtc, int CooldownSeconds)> RequestCodeAsync(
        string phoneNumber, PhoneVerificationPurpose purpose = PhoneVerificationPurpose.SignupVerification)
    {
        var phone = WhatsAppService.NormalizePhone(phoneNumber);
        if (string.IsNullOrEmpty(phone))
            throw new InvalidOperationException("Valid phone number required.");

        var isRegistered = await _db.Users.AnyAsync(u => u.PhoneNumber == phone && u.IsActive);
        switch (purpose)
        {
            case PhoneVerificationPurpose.SignupVerification:
                if (isRegistered)
                    throw new InvalidOperationException("This phone number is already registered. Try signing in instead.");
                break;
            case PhoneVerificationPurpose.PasswordReset:
                if (!isRegistered)
                    throw new KeyNotFoundException("No account found with this phone number.");
                break;
        }

        var now = DateTime.UtcNow;

        // Resend cooldown — 60s between codes per phone, scoped to this purpose so a signup-in-progress
        // doesn't block the user from requesting a reset code.
        var lastIssued = await _db.PhoneVerificationCodes
            .Where(v => v.PhoneNumber == phone && v.Purpose == purpose)
            .OrderByDescending(v => v.CreatedAtUtc)
            .FirstOrDefaultAsync();
        if (lastIssued != null && (now - lastIssued.CreatedAtUtc) < ResendCooldown)
        {
            var waitSec = (int)Math.Ceiling((ResendCooldown - (now - lastIssued.CreatedAtUtc)).TotalSeconds);
            throw new InvalidOperationException($"Please wait {waitSec}s before requesting another code.");
        }

        var hourCount = await _db.PhoneVerificationCodes
            .CountAsync(v => v.PhoneNumber == phone && v.Purpose == purpose && v.CreatedAtUtc > now - HourlyWindow);
        if (hourCount >= MaxRequestsPerHour)
            throw new InvalidOperationException("Too many code requests for this number. Please try again later.");

        var code = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999).ToString();

        var row = new PhoneVerificationCode
        {
            PhoneNumber = phone,
            HashedCode = BCrypt.Net.BCrypt.HashPassword(code),
            Purpose = purpose,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(CodeLifetime),
        };
        _db.PhoneVerificationCodes.Add(row);
        await _db.SaveChangesAsync();

        var msg = purpose == PhoneVerificationPurpose.PasswordReset
            ? $"🔒 Your Ojunai password reset code is *{code}*.\n\nExpires in 10 minutes. If you didn't request this, ignore this message."
            : $"Your Ojunai verification code is *{code}*.\n\nIt expires in 10 minutes. Don't share it with anyone.";

        try
        {
            await _whatsApp.SendMessageAsync($"whatsapp:{phone}", msg);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send {Purpose} code to {Phone}", purpose, Redact(phone));
            throw new InvalidOperationException("Couldn't send the verification code. Please try again in a moment.");
        }

        _logger.LogInformation("{Purpose} code issued for {Phone}", purpose, Redact(phone));
        return (phone, row.ExpiresAtUtc, (int)ResendCooldown.TotalSeconds);
    }

    public async Task ConsumeCodeAsync(
        string phoneNumber, string code, PhoneVerificationPurpose purpose = PhoneVerificationPurpose.SignupVerification)
    {
        var phone = WhatsAppService.NormalizePhone(phoneNumber);
        if (string.IsNullOrEmpty(phone))
            throw new InvalidOperationException("Valid phone number required.");

        var now = DateTime.UtcNow;

        var row = await _db.PhoneVerificationCodes
            .Where(v => v.PhoneNumber == phone && v.Purpose == purpose && v.UsedAtUtc == null)
            .OrderByDescending(v => v.CreatedAtUtc)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("No active verification code. Please request a new one.");

        if (now > row.ExpiresAtUtc)
            throw new InvalidOperationException("Verification code expired. Please request a new one.");

        if (row.Attempts >= MaxAttemptsPerCode)
            throw new InvalidOperationException("Too many invalid attempts on this code. Please request a new one.");

        if (!BCrypt.Net.BCrypt.Verify(code, row.HashedCode))
        {
            row.Attempts++;
            await _db.SaveChangesAsync();
            var remaining = MaxAttemptsPerCode - row.Attempts;
            throw new UnauthorizedAccessException(
                remaining > 0
                    ? $"Invalid code. {remaining} attempt{(remaining != 1 ? "s" : "")} left."
                    : "Invalid code. Please request a new one.");
        }

        row.UsedAtUtc = now;
        await _db.SaveChangesAsync();
    }

    private static string Redact(string phone) =>
        phone.Length >= 4 ? new string('*', phone.Length - 4) + phone[^4..] : "****";
}

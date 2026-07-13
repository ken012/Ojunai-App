using Hangfire;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Auth;
using Ojunai.API.Models;
using Ojunai.API.Services;
using Ojunai.API.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Controllers;

[Route("api/auth")]
public class AuthController : OjunaiBaseController
{
    private readonly IAuthService _auth;
    private readonly AuthService _authConcrete;
    private readonly AppDbContext _db;
    private readonly IWhatsAppService _whatsApp;
    private readonly IPhoneVerificationService _phoneVerify;
    private readonly IEmailVerificationService _emailVerify;
    private readonly IEmailService _emailSender;
    private readonly IAccountRecoveryService _recovery;
    private readonly IBackgroundJobClient _jobs;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthController> _logger;
    private readonly IAlertService _alerts;

    public AuthController(
        IAuthService auth,
        AppDbContext db,
        IWhatsAppService whatsApp,
        IPhoneVerificationService phoneVerify,
        IEmailVerificationService emailVerify,
        IEmailService emailSender,
        IAccountRecoveryService recovery,
        IBackgroundJobClient jobs,
        IConfiguration config,
        IAlertService alerts,
        ILogger<AuthController> logger)
    {
        _auth = auth;
        _authConcrete = (AuthService)auth;
        _db = db;
        _whatsApp = whatsApp;
        _phoneVerify = phoneVerify;
        _emailVerify = emailVerify;
        _emailSender = emailSender;
        _recovery = recovery;
        _jobs = jobs;
        _config = config;
        _alerts = alerts;
        _logger = logger;
    }

    // The direct register endpoint bypassed the mandatory phone-OTP signup gate that the dashboard
    // (and verify-phone-and-register) enforce — allowing unverified-phone account creation and phone
    // number squatting. Owner signup MUST prove phone ownership first, so this legacy path is closed.
    // Clients use request-phone-verification → verify-phone-and-register.
    [AllowAnonymous]
    [AuthRateLimit]
    [HttpPost("register")]
    public ActionResult<ApiResponse<AuthResponse>> Register([FromBody] RegisterOwnerRequest request)
    {
        return StatusCode(StatusCodes.Status410Gone, ApiResponse<AuthResponse>.Fail(
            "Direct registration is disabled. Verify your phone via /api/auth/request-phone-verification, then complete signup at /api/auth/verify-phone-and-register."));
    }

    [AllowAnonymous]
    [AuthRateLimit]
    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login([FromBody] LoginRequest request)
    {
        var result = await _auth.LoginAsync(request);
        SetAuthCookie(result.Token, result.ExpiresAt);
        return Ok(ApiResponse<AuthResponse>.Ok(result));
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Append("oj_auth", "", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(-1),
            Path = "/"
        });
        return Ok(ApiResponse<object>.Ok(null!, "Logged out."));
    }

    private void SetAuthCookie(string token, DateTime expiresAt)
    {
        Response.Cookies.Append("oj_auth", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = new DateTimeOffset(expiresAt, TimeSpan.Zero),
            Path = "/"
        });
    }

    [HttpGet("me")]
    public async Task<ActionResult<ApiResponse<UserDto>>> Me()
    {
        var result = await _auth.GetMeAsync(UserId);
        return Ok(ApiResponse<UserDto>.Ok(result));
    }

    [HttpPut("date-of-birth")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateDateOfBirth([FromBody] UpdateDobRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == UserId && u.IsActive)
            ?? throw new KeyNotFoundException("User not found.");
        user.DateOfBirth = request.DateOfBirth;
        await _db.SaveChangesAsync();

        // Surface a personal bell alert so the change is visible/auditable to the user. The body
        // intentionally does NOT include the actual year (the bell persists — don't expose it).
        // Dedupe on (user, year) so a double-save of the same year only alerts once; the key is
        // internal and never displayed.
        var year = request.DateOfBirth.Year;
        await _alerts.CreateAsync(
            user.BusinessId, user.Id, AlertType.ProfileUpdated, AlertSeverity.Info,
            "Birth year updated", "Your birth year was updated.",
            dedupeKey: $"dob-update-{user.Id}-{year}");

        return Ok(ApiResponse<object>.Ok(null!, "Date of birth updated."));
    }

    /// <summary>
    /// Sets which messaging channel the user's outbound alerts/summaries should land on. WhatsApp,
    /// Telegram, and Messenger are all live as of Phase 3e. The dispatcher falls back to WhatsApp
    /// if the chosen channel isn't actually bound — so this is a preference, not a hard constraint.
    /// </summary>
    [HttpPut("alert-channel")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateAlertChannel([FromBody] UpdateAlertChannelRequest request)
    {
        // "none" turns business alerts off; whatsapp/telegram/messenger select a destination.
        var normalized = (request.Channel ?? AlertChannels.None).Trim().ToLowerInvariant();

        if (!AlertChannels.All.Contains(normalized))
            throw new InvalidOperationException($"Unsupported alert channel '{request.Channel}'.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == UserId && u.IsActive)
            ?? throw new KeyNotFoundException("User not found.");
        user.AlertChannel = normalized;
        await _db.SaveChangesAsync();
        var msg = normalized == AlertChannels.None
            ? "Business alerts turned off — pick a channel to start receiving them."
            : $"Alert delivery channel set to {normalized}.";
        return Ok(ApiResponse<object>.Ok(null!, msg));
    }

    /// <summary>
    /// Updates the current user's email. If the email actually changes, EmailVerified is cleared
    /// and a fresh verification email is sent — the new address must be re-proven before it can
    /// be used for account recovery.
    ///
    /// Removing an email entirely is NOT supported via this endpoint — once a user has set one
    /// it stays as their recovery channel until support manually removes it. This prevents
    /// users from accidentally orphaning their own account recovery.
    /// </summary>
    [HttpPut("email")]
    public async Task<ActionResult<ApiResponse<UserDto>>> UpdateEmail([FromBody] UpdateEmailRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == UserId && u.IsActive)
            ?? throw new KeyNotFoundException("User not found.");

        var normalized = request.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized))
            throw new InvalidOperationException(
                "Email can't be blank. To remove your email, please contact support — it's your account recovery channel.");

        var conflict = await _db.Users.AnyAsync(u =>
            u.Email == normalized && u.IsActive && u.Id != UserId);
        if (conflict)
            throw new InvalidOperationException("This email is already registered to another account.");

        var changed = user.Email != normalized;
        // Capture the OLD verified address before mutating so we can send an out-of-band
        // notification — this is the anti-takeover signal: if an attacker silently swapped
        // the email, the legitimate owner gets a heads-up at their original inbox.
        var previousEmail = user.Email;
        var previousEmailVerified = user.EmailVerified;
        user.Email = normalized;
        if (changed)
        {
            user.EmailVerified = false;
            user.EmailVerifiedAtUtc = null;
        }
        await _db.SaveChangesAsync();

        if (changed)
        {
            try { await _emailVerify.SendVerificationEmailAsync(user.Id); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not send verification email after email update for {UserId}", user.Id); }

            if (previousEmailVerified && !string.IsNullOrEmpty(previousEmail))
            {
                var fullName = user.FullName;
                var detail = $"Your Ojunai account email was just changed from {previousEmail} to {normalized}. The new address must be verified before it can be used for recovery.";
                _jobs.Enqueue<IEmailService>(svc => svc.TrySendSecurityNotificationAsync(
                    previousEmail, fullName,
                    "Account email changed",
                    detail));
            }
        }

        return Ok(ApiResponse<UserDto>.Ok(new UserDto
        {
            Id = user.Id,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            Email = user.Email,
            EmailVerified = user.EmailVerified,
            Role = user.Role.ToString(),
            DateOfBirth = user.DateOfBirth,
        }, changed
            ? "Email updated. Check your inbox to verify the new address."
            : "Email is unchanged."));
    }

    [HttpPost("change-password")]
    public async Task<ActionResult<ApiResponse<object>>> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        await _auth.ChangePasswordAsync(UserId, request.CurrentPassword, request.NewPassword);

        // Re-issue JWT cookie with the new TokenVersion so the session doesn't expire
        var user = await _db.Users.Include(u => u.Business).FirstOrDefaultAsync(u => u.Id == UserId);
        if (user != null)
        {
            var response = _authConcrete.BuildAuthResponsePublic(user, user.Business, overrideMustChange: false);
            SetAuthCookie(response.Token!, response.ExpiresAt);
        }

        return Ok(ApiResponse<object>.Ok(null!, "Password changed successfully."));
    }

    [AllowAnonymous]
    [AuthRateLimit]
    [HttpPost("request-reset")]
    public async Task<ActionResult<ApiResponse<object>>> RequestReset([FromBody] RequestResetDto request)
    {
        // RequestPasswordResetAsync delegates to PhoneVerificationService which generates,
        // hashes, stores, AND sends the code over WhatsApp — controller no longer dispatches
        // the message itself. The role gate (Owner/Admin only) lives in the service too.
        await _auth.RequestPasswordResetAsync(request.PhoneNumber);
        return Ok(ApiResponse<object>.Ok(null!, "If that number is registered, a reset code has been sent to its WhatsApp."));
    }

    [AllowAnonymous]
    [AuthRateLimit]
    [HttpPost("verify-reset")]
    public async Task<ActionResult<ApiResponse<object>>> VerifyReset([FromBody] VerifyResetDto request)
    {
        await _auth.VerifyResetAndChangePasswordAsync(request.PhoneNumber, request.Code, request.NewPassword);
        return Ok(ApiResponse<object>.Ok(null!, "Password reset successfully. You can now log in."));
    }

    /// <summary>
    /// Step 1 of phone-verified signup. Issues a 6-digit code, stores its hash, and sends the code
    /// to the phone via WhatsApp. Rate-limited (60s cooldown, 5/hour). Caller polls with /verify-phone-and-register
    /// to actually create the account.
    /// </summary>
    [AllowAnonymous]
    [AuthRateLimit]
    [HttpPost("request-phone-verification")]
    public async Task<ActionResult<ApiResponse<RequestPhoneVerificationResponse>>> RequestPhoneVerification(
        [FromBody] RequestPhoneVerificationRequest request)
    {
        var (phone, expiresAt, cooldown) = await _phoneVerify.RequestCodeAsync(request.PhoneNumber);
        return Ok(ApiResponse<RequestPhoneVerificationResponse>.Ok(new RequestPhoneVerificationResponse
        {
            PhoneNumber = phone,
            ExpiresAtUtc = expiresAt,
            ResendCooldownSeconds = cooldown,
        }, "Verification code sent. Check your WhatsApp."));
    }

    /// <summary>
    /// Step 2 of phone-verified signup. Validates the OTP, then runs the existing register flow.
    /// No User or Business row is created until both the code and registration payload pass.
    /// </summary>
    [AllowAnonymous]
    [AuthRateLimit]
    [HttpPost("verify-phone-and-register")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> VerifyPhoneAndRegister(
        [FromBody] VerifyPhoneAndRegisterRequest request)
    {
        await _phoneVerify.ConsumeCodeAsync(request.PhoneNumber, request.Code);

        var registerRequest = new RegisterOwnerRequest
        {
            FullName = request.FullName,
            PhoneNumber = request.PhoneNumber,
            Email = request.Email,
            Password = request.Password,
            BusinessName = request.BusinessName,
            BusinessType = request.BusinessType,
            State = request.State,
            City = request.City,
            DateOfBirth = request.DateOfBirth,
        };
        var result = await _auth.RegisterOwnerAsync(registerRequest);
        SetAuthCookie(result.Token!, result.ExpiresAt);

        // Fire-and-forget verification email if the user supplied one. Failure (SMTP down,
        // bad config, etc.) must NOT block the registration response — the user can resend
        // from the dashboard banner once they're inside.
        if (!string.IsNullOrEmpty(result.User.Email))
        {
            try { await _emailVerify.SendVerificationEmailAsync(result.User.Id); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not send verification email at signup for {UserId}", result.User.Id); }
        }

        return Ok(ApiResponse<AuthResponse>.Ok(result, "Business registered successfully."));
    }

    /// <summary>
    /// Authenticated endpoint — current user requests a (re-)send of their verification email.
    /// Used by the in-dashboard "Verify your email" banner.
    /// </summary>
    [HttpPost("request-email-verification")]
    public async Task<ActionResult<ApiResponse<RequestEmailVerificationResponse>>> RequestEmailVerification()
    {
        var expiresAt = await _emailVerify.SendVerificationEmailAsync(UserId);
        return Ok(ApiResponse<RequestEmailVerificationResponse>.Ok(
            new RequestEmailVerificationResponse { ExpiresAtUtc = expiresAt },
            "Verification email sent. Check your inbox."));
    }

    /// <summary>
    /// Anonymous endpoint — the link in the verification email POSTs here. On success we mark
    /// the user's email verified; the link is single-use.
    /// </summary>
    [AllowAnonymous]
    [AuthRateLimit]
    [HttpPost("verify-email")]
    public async Task<ActionResult<ApiResponse<object>>> VerifyEmail([FromBody] VerifyEmailRequest request)
    {
        await _emailVerify.ConsumeTokenAsync(request.Token);
        return Ok(ApiResponse<object>.Ok(null!, "Email verified successfully."));
    }

    /// <summary>
    /// Phone-loss recovery, step 1. Always returns 204 — we never reveal whether the email matched.
    /// </summary>
    [AllowAnonymous]
    [AuthRateLimit]
    [HttpPost("request-account-recovery")]
    public async Task<ActionResult<ApiResponse<object>>> RequestAccountRecovery([FromBody] RequestAccountRecoveryRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _recovery.RequestRecoveryAsync(request.Email, ip);
        return Ok(ApiResponse<object>.Ok(null!, "If an account is registered to that email, a recovery link has been sent."));
    }

    /// <summary>
    /// Phone-loss recovery — token validation. Validates without consuming so the UI can show
    /// the redacted account info before the user picks an action.
    /// </summary>
    [AllowAnonymous]
    [AuthRateLimit]
    [HttpPost("recover-account/info")]
    public async Task<ActionResult<ApiResponse<RecoveryTokenInfo>>> InspectRecoveryToken([FromBody] InspectRecoveryTokenRequest request)
    {
        var info = await _recovery.InspectTokenAsync(request.Token);
        return Ok(ApiResponse<RecoveryTokenInfo>.Ok(info));
    }

    /// <summary>
    /// Phone-loss recovery — completion via password reset. Consumes the token and returns a
    /// fresh auth response so the user is logged in immediately.
    /// </summary>
    [AllowAnonymous]
    [AuthRateLimit]
    [HttpPost("recover-account/reset-password")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> RecoverAccountResetPassword([FromBody] RecoverAccountResetPasswordRequest request)
    {
        var result = await _recovery.CompletePasswordResetAsync(request.Token, request.NewPassword);
        SetAuthCookie(result.Token!, result.ExpiresAt);
        return Ok(ApiResponse<AuthResponse>.Ok(result, "Password reset. You're signed in."));
    }

    /// <summary>
    /// Phone-loss recovery — phone change, step 1: send OTP to the new phone.
    /// </summary>
    [AllowAnonymous]
    [AuthRateLimit]
    [HttpPost("recover-account/request-phone-otp")]
    public async Task<ActionResult<ApiResponse<RequestPhoneVerificationResponse>>> RecoverRequestPhoneOtp([FromBody] RecoverAccountRequestPhoneOtpRequest request)
    {
        var expiresAt = await _recovery.RequestPhoneChangeOtpAsync(request.Token, request.NewPhoneNumber);
        return Ok(ApiResponse<RequestPhoneVerificationResponse>.Ok(new RequestPhoneVerificationResponse
        {
            PhoneNumber = request.NewPhoneNumber,
            ExpiresAtUtc = expiresAt,
            ResendCooldownSeconds = 60,
        }, "Verification code sent to your new phone via WhatsApp."));
    }

    /// <summary>
    /// Phone-loss recovery — phone change, step 2: verify OTP and swap phone. Consumes the
    /// recovery token and returns a fresh auth response.
    /// </summary>
    [AllowAnonymous]
    [AuthRateLimit]
    [HttpPost("recover-account/change-phone")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> RecoverChangePhone([FromBody] RecoverAccountChangePhoneRequest request)
    {
        var result = await _recovery.CompletePhoneChangeAsync(request.Token, request.NewPhoneNumber, request.Code);
        SetAuthCookie(result.Token!, result.ExpiresAt);
        return Ok(ApiResponse<AuthResponse>.Ok(result, "Phone changed. You're signed in with the new number."));
    }

    // ─── Phase 3: Channel-native signup ──────────────────────────────────────────

    /// <summary>
    /// Start a signup-via-Telegram flow. Issues a single-use token, returns the deep link
    /// that opens the Telegram bot with the token pre-attached. The flow completes inside
    /// the chat (bot captures phone via request_contact + business/owner name in chat).
    /// </summary>
    [AllowAnonymous]
    [AuthRateLimit]
    [HttpPost("signup-via-telegram/start")]
    public async Task<ActionResult<ApiResponse<SignupViaTelegramStartResponse>>> StartSignupViaTelegram()
    {
        var botUsername = _config["Telegram:BotUsername"];
        if (string.IsNullOrEmpty(botUsername))
            return StatusCode(503, ApiResponse<SignupViaTelegramStartResponse>.Fail(
                "Telegram signup is not configured on this server."));

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var (token, deepLink) = await _authConcrete.StartTelegramSignupAsync(botUsername, ip);

        return Ok(ApiResponse<SignupViaTelegramStartResponse>.Ok(new SignupViaTelegramStartResponse
        {
            DeepLink = deepLink,
            BotUsername = botUsername,
            ExpiresInSeconds = 30 * 60,
        }, "Open the link in Telegram and tap Start."));
    }

    /// <summary>
    /// Start a signup-via-Messenger flow. Same shape as the Telegram variant — issues a
    /// single-use token, returns an m.me deep link that opens the page's bot with the token
    /// as the ref parameter.
    /// </summary>
    [AllowAnonymous]
    [AuthRateLimit]
    [HttpPost("signup-via-messenger/start")]
    public async Task<ActionResult<ApiResponse<SignupViaMessengerStartResponse>>> StartSignupViaMessenger()
    {
        var pageUsername = _config["Messenger:PageUsername"];
        if (string.IsNullOrEmpty(pageUsername))
            return StatusCode(503, ApiResponse<SignupViaMessengerStartResponse>.Fail(
                "Messenger signup is not configured on this server."));

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var (token, deepLink) = await _authConcrete.StartMessengerSignupAsync(pageUsername, ip);

        return Ok(ApiResponse<SignupViaMessengerStartResponse>.Ok(new SignupViaMessengerStartResponse
        {
            DeepLink = deepLink,
            PageUsername = pageUsername,
            ExpiresInSeconds = 30 * 60,
        }, "Open the link in Messenger and tap Get Started."));
    }

    /// <summary>
    /// Consumes the magic-link JWT issued by the Telegram OR Messenger signup handler. Sets
    /// the user's password and returns a normal AuthResponse (real session JWT) so the
    /// dashboard treats the visitor as logged in.
    /// </summary>
    [AllowAnonymous]
    [AuthRateLimit]
    [HttpPost("post-signup")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> CompletePostSignup([FromBody] PostSignupRequest request)
    {
        var result = await _authConcrete.CompletePostSignupAsync(request.PostSignupToken, request.Password);
        SetAuthCookie(result.Token!, result.ExpiresAt);
        return Ok(ApiResponse<AuthResponse>.Ok(result, "Welcome to Ojunai."));
    }
}

public class SignupViaMessengerStartResponse
{
    public string DeepLink { get; set; } = string.Empty;
    public string PageUsername { get; set; } = string.Empty;
    public int ExpiresInSeconds { get; set; }
}

public class SignupViaTelegramStartResponse
{
    public string DeepLink { get; set; } = string.Empty;
    public string BotUsername { get; set; } = string.Empty;
    public int ExpiresInSeconds { get; set; }
}

public class PostSignupRequest
{
    public string PostSignupToken { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class UpdateDobRequest
{
    public DateOnly DateOfBirth { get; set; }
}

public class UpdateAlertChannelRequest
{
    /// <summary>"none" | "whatsapp" | "telegram" | "messenger". Other values throw 400.</summary>
    public string? Channel { get; set; }
}

public class UpdateEmailRequest
{
    [System.ComponentModel.DataAnnotations.EmailAddress]
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string? Email { get; set; }
    // Note: not [Required] at the validator level so we can produce our own friendlier
    // "contact support" message in the controller for blank submissions.
}

public class RequestResetDto
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class VerifyResetDto
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

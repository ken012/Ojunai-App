using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Hangfire;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Auth;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Ojunai.API.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly Services.Interfaces.IPhoneVerificationService _phoneVerify;
    private readonly Services.Interfaces.IAlertService _alerts;
    private readonly IEmailService _email;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        AppDbContext db,
        IConfiguration config,
        Services.Interfaces.IPhoneVerificationService phoneVerify,
        Services.Interfaces.IAlertService alerts,
        IEmailService email,
        IBackgroundJobClient jobs,
        ILogger<AuthService> logger)
    {
        _db = db;
        _config = config;
        _phoneVerify = phoneVerify;
        _alerts = alerts;
        _email = email;
        _jobs = jobs;
        _logger = logger;
    }

    public async Task<AuthResponse> RegisterOwnerAsync(RegisterOwnerRequest request)
    {
        var (pwOk, pwReason) = PasswordPolicy.Validate(request.Password);
        if (!pwOk)
            throw new InvalidOperationException(pwReason!);

        var normalizedPhone = WhatsAppService.NormalizePhone(request.PhoneNumber);
        if (string.IsNullOrEmpty(normalizedPhone))
            throw new InvalidOperationException("Valid phone number required.");

        var phoneExists = await _db.Users.AnyAsync(u => u.PhoneNumber == normalizedPhone && u.IsActive);
        if (phoneExists)
            throw new InvalidOperationException("Phone number already registered.");

        var normalizedEmail = request.Email?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(normalizedEmail))
        {
            var emailExists = await _db.Users.AnyAsync(u => u.Email == normalizedEmail && u.IsActive);
            if (emailExists)
                throw new InvalidOperationException("Email already registered.");
        }

        // If a deactivated user holds this phone (from a previous business), free it by swapping
        // to a placeholder. We keep the row for audit history — their RecordedByUserId references
        // in the old business's sales/expenses stay intact.
        var deactivated = await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == normalizedPhone && !u.IsActive);
        if (deactivated != null)
        {
            deactivated.PhoneNumber = $"x{deactivated.Id.ToString("N")[..18]}";
            await _db.SaveChangesAsync();
        }

        var inferred = Common.CountryLookup.InferFromPhone(normalizedPhone) ?? Common.CountryLookup.Default;

        var business = new Business
        {
            Name = request.BusinessName,
            BusinessType = request.BusinessType,
            State = request.State,
            City = request.City,
            Country = inferred.Name,
            Currency = inferred.Currency,
            Timezone = inferred.Timezone,
            Plan = "starter",
            TrialEndsAt = DateTime.UtcNow.AddDays(30),
            AccountNumber = await Common.AccountNumberGenerator.GenerateUniqueAsync(_db)
        };
        _db.Businesses.Add(business);

        var user = new User
        {
            BusinessId = business.Id,
            FullName = request.FullName,
            PhoneNumber = normalizedPhone,
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = UserRole.Owner,
            DateOfBirth = request.DateOfBirth
        };
        _db.Users.Add(user);

        business.OwnerUserId = user.Id;
        await _db.SaveChangesAsync();

        return BuildAuthResponse(user, business);
    }

    /// <summary>
    /// Authenticates a user by phone or email + password.
    ///
    /// Security:
    ///   - Generic "Invalid credentials" error (doesn't leak whether user exists)
    ///   - Account lockout after 5 failed attempts (15 minutes)
    ///   - Phone normalized to E.164 format, email normalized to lowercase
    ///   - Checks both user.IsActive and business.IsActive (can't log into a deactivated account)
    ///   - Rate limited per IP (see [AuthRateLimit] on LoginController)
    /// </summary>
    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        // Normalize the identifier both ways so users can enter phone in any format or email in any case.
        var normalizedPhone = WhatsAppService.NormalizePhone(request.PhoneOrEmail);
        var normalizedEmail = request.PhoneOrEmail?.Trim().ToLowerInvariant();
        var user = await _db.Users
            .Include(u => u.Business)
            .FirstOrDefaultAsync(u =>
                u.PhoneNumber == normalizedPhone ||
                u.PhoneNumber == request.PhoneOrEmail ||
                (u.Email != null && u.Email == normalizedEmail));

        if (user == null)
            throw new UnauthorizedAccessException("Invalid credentials.");

        // Account lockout: block after 5 failed attempts for 15 minutes
        if (user.LockoutEndsAtUtc.HasValue && user.LockoutEndsAtUtc.Value > DateTime.UtcNow)
        {
            var minutesLeft = (int)Math.Ceiling((user.LockoutEndsAtUtc.Value - DateTime.UtcNow).TotalMinutes);
            throw new UnauthorizedAccessException($"Account temporarily locked. Try again in {minutesLeft} minute{(minutesLeft != 1 ? "s" : "")}.");
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= 5)
            {
                user.LockoutEndsAtUtc = DateTime.UtcNow.AddMinutes(15);
                user.FailedLoginAttempts = 0;

                // Failed login burst — security alert to the affected user.
                await _alerts.CreateAsync(
                    user.BusinessId, user.Id,
                    AlertType.FailedLoginBurst, AlertSeverity.Critical,
                    title: "Multiple failed login attempts on your account",
                    body: "Your account was locked for 15 minutes after 5 failed login attempts. If this wasn't you, change your password immediately.",
                    linkUrl: "/settings#account",
                    dedupeKey: $"failed-login-burst:{user.Id}");
            }
            await _db.SaveChangesAsync();
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        if (!user.IsActive || !user.Business.IsActive)
            throw new UnauthorizedAccessException("Account is inactive.");

        // Successful login — reset failure tracking
        if (user.FailedLoginAttempts > 0 || user.LockoutEndsAtUtc.HasValue)
        {
            user.FailedLoginAttempts = 0;
            user.LockoutEndsAtUtc = null;
            await _db.SaveChangesAsync();
        }

        return BuildAuthResponse(user, user.Business);
    }

    public async Task<UserDto> GetMeAsync(Guid userId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive)
            ?? throw new KeyNotFoundException("User not found.");

        return new UserDto
        {
            Id = user.Id,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            Email = user.Email,
            EmailVerified = user.EmailVerified,
            Role = user.Role.ToString(),
            DateOfBirth = user.DateOfBirth,
            AlertChannel = user.AlertChannel,
        };
    }

    public async Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive)
            ?? throw new KeyNotFoundException("User not found.");

        if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
            throw new UnauthorizedAccessException("Current password is incorrect.");

        var (pwOk, pwReason) = PasswordPolicy.Validate(newPassword);
        if (!pwOk)
            throw new InvalidOperationException(pwReason!);

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.MustChangePassword = false;
        user.TokenVersion++; // Invalidate all existing JWTs
        await _db.SaveChangesAsync();

        await _alerts.CreateAsync(
            user.BusinessId, user.Id,
            AlertType.PasswordChanged, AlertSeverity.Info,
            title: "Password changed",
            body: "Your password was successfully updated. If this wasn't you, contact support immediately.",
            linkUrl: "/settings#account");

        // Out-of-band notification — bell only matters if attacker hasn't logged us out;
        // an email reaches us through a different channel they don't control.
        // Enqueued to Hangfire so a slow SMTP host doesn't block the request hot-path
        // (we hit exactly that bug after switching email providers — connection lingered,
        // browser dropped, UI showed failure despite the password change succeeding).
        if (user.EmailVerified)
        {
            _jobs.Enqueue<IEmailService>(svc => svc.TrySendSecurityNotificationAsync(
                user.Email, user.FullName,
                "Password changed",
                "Your account password was just updated from inside the dashboard."));
        }
    }

    /// <summary>
    /// WhatsApp-OTP self-reset for Owner and Admin only. Sales/Bookkeeper/Viewer must have
    /// their password reset by an Owner/Admin via the staff endpoint instead — this is a
    /// deliberate policy: lower-privilege roles don't get a self-service WhatsApp reset path.
    ///
    /// We surface concrete errors ("no account", "staff role") rather than silently no-op'ing.
    /// The trade-off: a phone-existence enumeration channel exists. We accept that for clearer
    /// UX — users who mistype a phone or are confused about which login path to use see a
    /// helpful message instead of an apparent success that delivers no code.
    /// </summary>
    public async Task<string> RequestPasswordResetAsync(string phoneNumber)
    {
        var normalizedPhone = WhatsAppService.NormalizePhone(phoneNumber);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == normalizedPhone && u.IsActive)
            ?? throw new KeyNotFoundException("No account found with this phone number.");

        if (user.Role != UserRole.Owner && user.Role != UserRole.Admin)
            throw new InvalidOperationException(
                "Staff accounts can't self-reset by WhatsApp. Ask your owner or admin to reset your password from the dashboard.");

        await _phoneVerify.RequestCodeAsync(normalizedPhone, PhoneVerificationPurpose.PasswordReset);

        // Returned for backwards compatibility with the controller signature; the actual code
        // was already sent inside the service. Empty here means "no caller-side delivery needed."
        return string.Empty;
    }

    public async Task VerifyResetAndChangePasswordAsync(string phoneNumber, string code, string newPassword)
    {
        var normalizedPhone = WhatsAppService.NormalizePhone(phoneNumber);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == normalizedPhone && u.IsActive)
            ?? throw new KeyNotFoundException("No account found.");

        if (user.Role != UserRole.Owner && user.Role != UserRole.Admin)
            throw new InvalidOperationException(
                "Staff accounts can't self-reset by WhatsApp. Ask your owner or admin to reset your password from the dashboard.");

        var (pwOk, pwReason) = PasswordPolicy.Validate(newPassword);
        if (!pwOk)
            throw new InvalidOperationException(pwReason!);

        await _phoneVerify.ConsumeCodeAsync(normalizedPhone, code, PhoneVerificationPurpose.PasswordReset);

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.MustChangePassword = false;
        user.TokenVersion++; // Invalidate all existing JWTs
        await _db.SaveChangesAsync();

        await _alerts.CreateAsync(
            user.BusinessId, user.Id,
            AlertType.PasswordChanged, AlertSeverity.Info,
            title: "Password reset",
            body: "Your password was reset via WhatsApp verification. If this wasn't you, contact support immediately.",
            linkUrl: "/settings#account");

        if (user.EmailVerified)
        {
            _jobs.Enqueue<IEmailService>(svc => svc.TrySendSecurityNotificationAsync(
                user.Email, user.FullName,
                "Password reset via WhatsApp",
                "Your password was just reset using a WhatsApp verification code."));
        }
    }

    public AuthResponse BuildAuthResponsePublic(User user, Business business, bool? overrideMustChange = null)
        => BuildAuthResponse(user, business, overrideMustChange);

    // ─── Phase 3: Channel-native signup (Telegram) ────────────────────────────────
    //
    // Visitor on /register opts into "Sign up via Telegram". We issue a single-use token,
    // hand it to the user via a t.me deep link, and wait for the bot to complete signup.
    //
    // Why a separate flow vs. the existing RegisterOwnerAsync:
    //  - No password collected on-screen — set after signup on /post-signup
    //  - Phone proven via Telegram's verified contact-share, not a 6-digit OTP
    //  - Business name + owner name captured in the chat, not on the web form
    //
    // This path is purely additive — RegisterOwnerAsync still backs the web phone-OTP path.

    public async Task<(string token, string deepLink)> StartTelegramSignupAsync(
        string botUsername,
        string? requestIp,
        CancellationToken ct = default)
    {
        // 64 random hex chars + prefix. Prefix distinguishes signup tokens from regular
        // ChannelLinkToken values at the orchestrator's /start dispatch.
        var raw = System.Security.Cryptography.RandomNumberGenerator.GetHexString(64).ToLowerInvariant();
        var token = $"signup_{raw}";

        var row = new SignupChannelToken
        {
            Channel = Models.Messaging.Channel.Telegram,
            Token = token,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
            RequestIp = requestIp,
        };
        _db.SignupChannelTokens.Add(row);
        await _db.SaveChangesAsync(ct);

        // Telegram deep link: tap = opens Telegram client + sends "/start <token>" to the bot.
        var deepLink = $"https://t.me/{botUsername}?start={token}";
        return (token, deepLink);
    }

    /// <summary>
    /// Called by the Telegram signup handler after the bot has captured phone + name. Creates
    /// the User + Business + ContactIdentity, stamps the IDs back on the token row, and returns
    /// a one-time JWT the user redeems on /post-signup to set their password and log in.
    ///
    /// Idempotency: a token can only be consumed once (ConsumedAtUtc check); a re-consume
    /// throws so the caller surfaces a clear error to the user.
    /// </summary>
    public async Task<string> CompleteTelegramSignupAsync(
        string token,
        string phoneNumber,
        string fullName,
        string businessName,
        string telegramChatId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var row = await _db.SignupChannelTokens.FirstOrDefaultAsync(t => t.Token == token, ct)
            ?? throw new KeyNotFoundException("Signup link not found or expired.");

        if (row.ConsumedAtUtc.HasValue)
            throw new InvalidOperationException("This signup link has already been used.");
        if (row.ExpiresAtUtc < now)
            throw new InvalidOperationException("This signup link has expired. Start over from the dashboard.");
        if (row.Channel != Models.Messaging.Channel.Telegram)
            throw new InvalidOperationException("Token channel mismatch.");

        var normalizedPhone = WhatsAppService.NormalizePhone(phoneNumber);
        if (string.IsNullOrEmpty(normalizedPhone))
            throw new InvalidOperationException("Valid phone number required.");

        // Deactivated-user swap mirrors RegisterOwnerAsync — keep audit history intact.
        var phoneExists = await _db.Users.AnyAsync(u => u.PhoneNumber == normalizedPhone && u.IsActive, ct);
        if (phoneExists)
            throw new InvalidOperationException("Phone number already registered.");
        var deactivated = await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == normalizedPhone && !u.IsActive, ct);
        if (deactivated != null)
        {
            deactivated.PhoneNumber = $"x{deactivated.Id.ToString("N")[..18]}";
            await _db.SaveChangesAsync(ct);
        }

        var inferred = Common.CountryLookup.InferFromPhone(normalizedPhone) ?? Common.CountryLookup.Default;
        var business = new Business
        {
            Name = string.IsNullOrWhiteSpace(businessName) ? $"{fullName}'s Business" : businessName,
            Country = inferred.Name,
            Currency = inferred.Currency,
            Timezone = inferred.Timezone,
            Plan = "starter",
            TrialEndsAt = now.AddDays(30),
            AccountNumber = await Common.AccountNumberGenerator.GenerateUniqueAsync(_db)
        };
        _db.Businesses.Add(business);

        var user = new User
        {
            BusinessId = business.Id,
            FullName = string.IsNullOrWhiteSpace(fullName) ? "Owner" : fullName,
            PhoneNumber = normalizedPhone,
            // No password yet — user sets it on /post-signup.
            PasswordHash = string.Empty,
            MustChangePassword = true,
            Role = UserRole.Owner,
        };
        _db.Users.Add(user);
        business.OwnerUserId = user.Id;

        // Bind the Telegram identity right away — they can use the bot from the same chat.
        _db.ContactIdentities.Add(new Models.ContactIdentity
        {
            UserId = user.Id,
            BusinessId = business.Id,
            Channel = Models.Messaging.Channel.Telegram,
            ChannelIdentityValue = telegramChatId,
            DisplayName = fullName,
            LinkedAtUtc = now,
            LastSeenAtUtc = now,
        });

        row.ConsumedAtUtc = now;
        row.ConsumedByIdentity = telegramChatId;
        row.CreatedUserId = user.Id;
        row.CreatedBusinessId = business.Id;

        await _db.SaveChangesAsync(ct);

        // One-time JWT for the magic link. Same signing key as login JWTs — but with a
        // claim that marks it as a "post-signup" token so we can validate it server-side
        // and prevent reuse for arbitrary login.
        return GeneratePostSignupJwt(user, business);
    }

    /// <summary>
    /// Consumes the magic-link JWT on /post-signup. Sets the user's password, returns a normal
    /// AuthResponse so the dashboard treats them as logged in.
    /// </summary>
    public async Task<AuthResponse> CompletePostSignupAsync(string postSignupJwt, string newPassword, CancellationToken ct = default)
    {
        var (pwOk, pwReason) = PasswordPolicy.Validate(newPassword);
        if (!pwOk)
            throw new InvalidOperationException(pwReason!);

        var (userId, businessId) = ValidatePostSignupJwt(postSignupJwt);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct)
            ?? throw new KeyNotFoundException("Signup session not found.");
        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId, ct)
            ?? throw new KeyNotFoundException("Signup session not found.");

        // Only allow if the user genuinely has no password yet — replay guard. A normal
        // logged-in user changing their password goes through ChangePasswordAsync instead.
        if (!string.IsNullOrEmpty(user.PasswordHash))
            throw new InvalidOperationException("Account already has a password. Use the login page.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.MustChangePassword = false;
        user.TokenVersion++;
        await _db.SaveChangesAsync(ct);

        return BuildAuthResponse(user, business);
    }

    private string GeneratePostSignupJwt(User user, Business business)
    {
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:SecretKey"]!));
        var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);
        var claims = new List<System.Security.Claims.Claim>
        {
            new System.Security.Claims.Claim("uid", user.Id.ToString()),
            new System.Security.Claims.Claim("bid", business.Id.ToString()),
            new System.Security.Claims.Claim("typ", "post_signup"),
        };
        // Short-lived: 30 min — long enough to read the chat, click the link, set a password.
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "Ojunai",
            audience: _config["Jwt:Audience"] ?? "Ojunai",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds);
        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private (Guid userId, Guid businessId) ValidatePostSignupJwt(string token)
    {
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:SecretKey"]!));
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(token,
            new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _config["Jwt:Issuer"] ?? "Ojunai",
                ValidateAudience = true,
                ValidAudience = _config["Jwt:Audience"] ?? "Ojunai",
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
            }, out _);

        var typ = principal.FindFirst("typ")?.Value;
        if (!string.Equals(typ, "post_signup", StringComparison.Ordinal))
            throw new InvalidOperationException("Wrong token type.");

        var uid = Guid.Parse(principal.FindFirst("uid")!.Value);
        var bid = Guid.Parse(principal.FindFirst("bid")!.Value);
        return (uid, bid);
    }

    private AuthResponse BuildAuthResponse(User user, Business business, bool? overrideMustChange = null)
    {
        var token = GenerateJwt(user, business.Id);
        var expiryHours = int.Parse(_config["Jwt:ExpiryHours"] ?? "24");

        return new AuthResponse
        {
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddHours(expiryHours),
            MustChangePassword = overrideMustChange ?? user.MustChangePassword,
            User = new UserDto
            {
                Id = user.Id,
                FullName = user.FullName,
                PhoneNumber = user.PhoneNumber,
                Email = user.Email,
                EmailVerified = user.EmailVerified,
                Role = user.Role.ToString(),
                AlertChannel = user.AlertChannel,
            },
            Business = new BusinessDto
            {
                Id = business.Id,
                Name = business.Name,
                BusinessType = business.BusinessType,
                Currency = business.Currency,
                State = business.State,
                City = business.City,
                Country = business.Country,
                Plan = business.Plan,
                SubscribedPlan = business.SubscribedPlan,
                TrialEndsAt = business.TrialEndsAt,
                LargeSaleThreshold = business.LargeSaleThreshold,
                CustomCategories = string.IsNullOrEmpty(business.CustomCategories)
                    ? new List<string>()
                    : System.Text.Json.JsonSerializer.Deserialize<List<string>>(business.CustomCategories) ?? new List<string>(),
                AlertLowStock = business.AlertLowStock,
                AlertDailySummary = business.AlertDailySummary,
                AlertLargeSale = business.AlertLargeSale,
                AlertDashboardLowStock = business.AlertDashboardLowStock,
                AlertDashboardDailySummary = business.AlertDashboardDailySummary,
                AlertDashboardLargeSale = business.AlertDashboardLargeSale,
                AlertDashboardAgedReceivable = business.AlertDashboardAgedReceivable,
                AlertDashboardStaffChanges = business.AlertDashboardStaffChanges,
                DailySalesGoal = business.DailySalesGoal,
                BackgroundImageUrl = string.IsNullOrEmpty(business.BackgroundImageFileName)
                    ? null
                    : $"/uploads/businesses/{business.Id:N}/{business.BackgroundImageFileName}",
                BackgroundImageOpacity = business.BackgroundImageOpacity,
                IsActive = business.IsActive,
                AccountNumber = business.AccountNumber
            }
        };
    }

    private string GenerateJwt(User user, Guid businessId)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiryHours = int.Parse(_config["Jwt:ExpiryHours"] ?? "24");

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim("businessId", businessId.ToString()),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("tokenVersion", user.TokenVersion.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(expiryHours),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

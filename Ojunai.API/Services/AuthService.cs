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

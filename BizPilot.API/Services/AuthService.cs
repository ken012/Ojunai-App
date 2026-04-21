using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BizPilot.API.Data;
using BizPilot.API.DTOs.Auth;
using BizPilot.API.Models;
using BizPilot.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace BizPilot.API.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public AuthService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<AuthResponse> RegisterOwnerAsync(RegisterOwnerRequest request)
    {
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
            Role = UserRole.Owner
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
            Role = user.Role.ToString()
        };
    }

    public async Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive)
            ?? throw new KeyNotFoundException("User not found.");

        if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
            throw new UnauthorizedAccessException("Current password is incorrect.");

        if (newPassword.Length < 8)
            throw new InvalidOperationException("New password must be at least 8 characters.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.MustChangePassword = false;
        user.TokenVersion++; // Invalidate all existing JWTs
        await _db.SaveChangesAsync();
    }

    public async Task<string> RequestPasswordResetAsync(string phoneNumber)
    {
        var normalizedPhone = WhatsAppService.NormalizePhone(phoneNumber);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == normalizedPhone && u.IsActive);
        if (user == null)
            throw new KeyNotFoundException("No account found with this phone number.");

        // Generate 6-digit code using cryptographically secure RNG
        var code = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        user.PasswordResetCode = BCrypt.Net.BCrypt.HashPassword(code);
        user.PasswordResetCodeExpiresAtUtc = DateTime.UtcNow.AddMinutes(10);
        await _db.SaveChangesAsync();

        return code; // Caller sends this via WhatsApp
    }

    public async Task VerifyResetAndChangePasswordAsync(string phoneNumber, string code, string newPassword)
    {
        var normalizedPhone = WhatsAppService.NormalizePhone(phoneNumber);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == normalizedPhone && u.IsActive)
            ?? throw new KeyNotFoundException("No account found.");

        if (user.PasswordResetCode == null || user.PasswordResetCodeExpiresAtUtc == null)
            throw new InvalidOperationException("No reset code requested. Please request a new one.");

        if (DateTime.UtcNow > user.PasswordResetCodeExpiresAtUtc.Value)
            throw new InvalidOperationException("Reset code has expired. Please request a new one.");

        if (!BCrypt.Net.BCrypt.Verify(code, user.PasswordResetCode))
            throw new UnauthorizedAccessException("Invalid reset code.");

        if (newPassword.Length < 8)
            throw new InvalidOperationException("New password must be at least 8 characters.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.MustChangePassword = false;
        user.PasswordResetCode = null;
        user.PasswordResetCodeExpiresAtUtc = null;
        user.TokenVersion++; // Invalidate all existing JWTs
        await _db.SaveChangesAsync();
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
                Role = user.Role.ToString()
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

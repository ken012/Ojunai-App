using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Auth;
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

    public AuthController(IAuthService auth, AppDbContext db, IWhatsAppService whatsApp)
    {
        _auth = auth;
        _authConcrete = (AuthService)auth;
        _db = db;
        _whatsApp = whatsApp;
    }

    [AllowAnonymous]
    [AuthRateLimit]
    [HttpPost("register")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Register([FromBody] RegisterOwnerRequest request)
    {
        var result = await _auth.RegisterOwnerAsync(request);
        SetAuthCookie(result.Token, result.ExpiresAt);
        return Ok(ApiResponse<AuthResponse>.Ok(result, "Business registered successfully."));
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
        Response.Cookies.Append("bp_auth", "", new CookieOptions
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
        Response.Cookies.Append("bp_auth", token, new CookieOptions
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
        return Ok(ApiResponse<object>.Ok(null!, "Date of birth updated."));
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
        var code = await _auth.RequestPasswordResetAsync(request.PhoneNumber);

        // Send code via WhatsApp
        var normalizedPhone = Services.WhatsAppService.NormalizePhone(request.PhoneNumber);
        await _whatsApp.SendMessageAsync(
            $"whatsapp:{normalizedPhone}",
            $"🔒 Your Ojunai password reset code is: *{code}*\n\nThis code expires in 10 minutes. If you didn't request this, ignore this message."
        );

        return Ok(ApiResponse<object>.Ok(null!, "Reset code sent to your WhatsApp."));
    }

    [AllowAnonymous]
    [AuthRateLimit]
    [HttpPost("verify-reset")]
    public async Task<ActionResult<ApiResponse<object>>> VerifyReset([FromBody] VerifyResetDto request)
    {
        await _auth.VerifyResetAndChangePasswordAsync(request.PhoneNumber, request.Code, request.NewPassword);
        return Ok(ApiResponse<object>.Ok(null!, "Password reset successfully. You can now log in."));
    }
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

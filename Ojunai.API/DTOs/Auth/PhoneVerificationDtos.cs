using System.ComponentModel.DataAnnotations;

namespace Ojunai.API.DTOs.Auth;

public class RequestPhoneVerificationRequest
{
    [Required, MaxLength(20)] public string PhoneNumber { get; set; } = string.Empty;
}

public class RequestPhoneVerificationResponse
{
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public int ResendCooldownSeconds { get; set; }
}

/// <summary>
/// Sent when the user enters the OTP from their WhatsApp message. Server verifies the code,
/// then runs the existing RegisterOwnerAsync flow with the provided registration fields.
/// </summary>
public class VerifyPhoneAndRegisterRequest
{
    [Required, MaxLength(20)] public string PhoneNumber { get; set; } = string.Empty;
    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "Code must be 6 digits.")]
    public string Code { get; set; } = string.Empty;

    // Same fields as RegisterOwnerRequest — server reuses the existing register flow after the OTP
    // checks out. Phone here must match the phone the OTP was issued for.
    [Required, MinLength(2), MaxLength(100)] public string FullName { get; set; } = string.Empty;
    [EmailAddress, MaxLength(200)] public string? Email { get; set; }
    [Required, MinLength(10), MaxLength(100)] public string Password { get; set; } = string.Empty;
    [Required, MinLength(2), MaxLength(200)] public string BusinessName { get; set; } = string.Empty;
    [MaxLength(100)] public string? BusinessType { get; set; }
    [MaxLength(100)] public string? State { get; set; }
    [MaxLength(100)] public string? City { get; set; }
    public DateOnly? DateOfBirth { get; set; }
}

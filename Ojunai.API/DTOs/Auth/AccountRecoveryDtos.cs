using System.ComponentModel.DataAnnotations;

namespace Ojunai.API.DTOs.Auth;

public class RequestAccountRecoveryRequest
{
    [Required, EmailAddress, MaxLength(200)] public string Email { get; set; } = string.Empty;
}

public class InspectRecoveryTokenRequest
{
    [Required, MaxLength(500)] public string Token { get; set; } = string.Empty;
}

/// <summary>
/// Returned to the recovery UI so it can show "you're recovering account for X" without revealing
/// the full phone or email (we only confirm to someone who already has the token + the inbox).
/// </summary>
public class RecoveryTokenInfo
{
    public string FullName { get; set; } = string.Empty;
    public string MaskedPhone { get; set; } = string.Empty;
    public string MaskedEmail { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
}

public class RecoverAccountResetPasswordRequest
{
    [Required, MaxLength(500)] public string Token { get; set; } = string.Empty;
    [Required, MinLength(10), MaxLength(100)] public string NewPassword { get; set; } = string.Empty;
}

public class RecoverAccountRequestPhoneOtpRequest
{
    [Required, MaxLength(500)] public string Token { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string NewPhoneNumber { get; set; } = string.Empty;
}

public class RecoverAccountChangePhoneRequest
{
    [Required, MaxLength(500)] public string Token { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string NewPhoneNumber { get; set; } = string.Empty;
    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "Code must be 6 digits.")]
    public string Code { get; set; } = string.Empty;
}

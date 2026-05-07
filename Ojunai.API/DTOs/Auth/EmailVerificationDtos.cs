using System.ComponentModel.DataAnnotations;

namespace Ojunai.API.DTOs.Auth;

public class VerifyEmailRequest
{
    [Required, MaxLength(500)] public string Token { get; set; } = string.Empty;
}

public class RequestEmailVerificationResponse
{
    public DateTime ExpiresAtUtc { get; set; }
}

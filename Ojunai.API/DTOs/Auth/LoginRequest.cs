using System.ComponentModel.DataAnnotations;

namespace Ojunai.API.DTOs.Auth;

public class LoginRequest
{
    [Required, MaxLength(200)] public string PhoneOrEmail { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string Password { get; set; } = string.Empty;
}

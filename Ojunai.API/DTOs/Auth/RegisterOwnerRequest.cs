using System.ComponentModel.DataAnnotations;

namespace Ojunai.API.DTOs.Auth;

public class RegisterOwnerRequest
{
    [Required, MinLength(2), MaxLength(100)] public string FullName { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string PhoneNumber { get; set; } = string.Empty;
    [EmailAddress, MaxLength(200)] public string? Email { get; set; }
    [Required, MinLength(8), MaxLength(100)] public string Password { get; set; } = string.Empty;
    [Required, MinLength(2), MaxLength(200)] public string BusinessName { get; set; } = string.Empty;
    [MaxLength(100)] public string? BusinessType { get; set; }
    [MaxLength(100)] public string? State { get; set; }
    [MaxLength(100)] public string? City { get; set; }
    public DateOnly? DateOfBirth { get; set; }
}

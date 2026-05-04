using System.ComponentModel.DataAnnotations;

namespace Ojunai.API.DTOs.Auth;

public class AddStaffRequest
{
    [Required, MinLength(2), MaxLength(100)] public string FullName { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string PhoneNumber { get; set; } = string.Empty;
    [Required, MinLength(8), MaxLength(100)] public string Password { get; set; } = string.Empty;
    [EmailAddress, MaxLength(200)] public string? Email { get; set; }
    [Required, MaxLength(20)] public string Role { get; set; } = "Sales"; // Admin, Sales, Bookkeeper, Viewer
}

public class StaffDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public List<string> Permissions { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; }
}

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
    /// <summary>Multi-location: the location ids this user is explicitly assigned to (raw <c>UserLocation</c>
    /// rows). Empty = unassigned ⇒ default-location-only by policy. Ignored for Owner/Admin (all-access).</summary>
    public List<Guid> AssignedLocationIds { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Multi-location: replace a staff member's location assignments with this set (empty = unassign →
/// default-location-only). Only meaningful for restricted roles; Owner/Admin are all-access.</summary>
public class UpdateStaffLocationsRequest
{
    public List<Guid> LocationIds { get; set; } = new();
}

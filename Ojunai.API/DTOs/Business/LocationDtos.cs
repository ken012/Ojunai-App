using System.ComponentModel.DataAnnotations;

namespace Ojunai.API.DTOs.Business;

/// <summary>A business location (branch/warehouse) as returned to the dashboard.</summary>
public class LocationDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>"branch" or "warehouse".</summary>
    public string Type { get; set; } = "branch";
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    // Per-branch contact details shown on this branch's receipts (null = falls back to the business value,
    // except Phone which has no business fallback).
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Phone { get; set; }
}

public class CreateLocationRequest
{
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    /// <summary>"branch" (default) or "warehouse".</summary>
    [MaxLength(20)] public string? Type { get; set; }
}

public class UpdateLocationRequest
{
    [MaxLength(200)] public string? Name { get; set; }
    /// <summary>Deactivate/reactivate a location. The default location cannot be deactivated.</summary>
    public bool? IsActive { get; set; }
    // Per-branch contact details for receipts. A non-null empty/blank value clears the field (→ business
    // fallback); leave null to not touch it.
    [MaxLength(300)] public string? Address { get; set; }
    [MaxLength(100)] public string? City { get; set; }
    [MaxLength(100)] public string? State { get; set; }
    [MaxLength(40)] public string? Phone { get; set; }
}

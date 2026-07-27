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
}

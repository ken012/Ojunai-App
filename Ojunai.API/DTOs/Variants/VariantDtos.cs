using System.ComponentModel.DataAnnotations;

namespace Ojunai.API.DTOs.Variants;

public class VariantAxisInput
{
    [Required, MaxLength(50)] public string Name { get; set; } = string.Empty;
    /// <summary>Option values for this axis, e.g. ["S","M","L"].</summary>
    [MinLength(1)] public List<string> Values { get; set; } = new();
}

public class CreateVariantGroupRequest
{
    [Required, MinLength(1), MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(100)] public string? Category { get; set; }
    [MaxLength(50)] public string? Unit { get; set; }
    [MinLength(1)] public List<VariantAxisInput> Axes { get; set; } = new();
    /// <summary>Applied to every generated variant (each editable afterwards).</summary>
    [Range(0, 999999999)] public decimal? BaseSellingPrice { get; set; }
    [Range(0, 999999999)] public decimal? BaseCostPrice { get; set; }
    [Range(0, 999999)] public decimal LowStockThreshold { get; set; } = 5;
}

public class AddVariantRequest
{
    /// <summary>Option values for the new variant, keyed by axis name, e.g. {"Size":"XL","Color":"Red"}.</summary>
    [Required] public Dictionary<string, string> Options { get; set; } = new();
    [Range(0, 999999999)] public decimal? SellingPrice { get; set; }
    [Range(0, 999999999)] public decimal? CostPrice { get; set; }
    [MaxLength(100)] public string? SKU { get; set; }
    [MaxLength(64)] public string? Barcode { get; set; }
    [Range(0, 999999)] public decimal LowStockThreshold { get; set; } = 5;
}

public class VariantAxisDto
{
    public string Name { get; set; } = string.Empty;
    public List<string> Values { get; set; } = new();
}

public class VariantDto
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Options { get; set; } = new();
    public string? SKU { get; set; }
    public string? Barcode { get; set; }
    public string Unit { get; set; } = "unit";
    public decimal? SellingPrice { get; set; }
    public decimal? CostPrice { get; set; }
    public decimal CurrentStock { get; set; }
    public decimal LowStockThreshold { get; set; }
    public bool IsLowStock { get; set; }
}

public class VariantGroupDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public List<VariantAxisDto> Axes { get; set; } = new();
    public int VariantCount { get; set; }
    public decimal TotalStock { get; set; }
    /// <summary>How many variants in this group are at or below their low-stock threshold.
    /// Lets the inventory list flag a style as low without shipping every variant row.</summary>
    public int LowStockCount { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<VariantDto> Variants { get; set; } = new();
}

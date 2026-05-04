using System.ComponentModel.DataAnnotations;

namespace Ojunai.API.DTOs.Products;

public class CreateProductRequest
{
    [Required, MinLength(1), MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(100)] public string? SKU { get; set; }
    [MaxLength(50)] public string? Unit { get; set; }
    [Range(0, 999999999)] public decimal? CostPrice { get; set; }
    [Range(0, 999999999)] public decimal? SellingPrice { get; set; }
    [Range(0, 999999)] public decimal InitialStock { get; set; } = 0;
    [Range(0, 999999)] public decimal LowStockThreshold { get; set; } = 5;
    [MaxLength(100)] public string? Category { get; set; }
    [MaxLength(100)] public string? Subcategory { get; set; }
}

public class UpdateProductRequest
{
    [MinLength(1), MaxLength(200)] public string? Name { get; set; }
    [MaxLength(50)] public string? Unit { get; set; }
    [Range(0, 999999999)] public decimal? CostPrice { get; set; }
    [Range(0, 999999999)] public decimal? SellingPrice { get; set; }
    [Range(0, 999999)] public decimal? LowStockThreshold { get; set; }
    [MaxLength(100)] public string? Category { get; set; }
    [MaxLength(100)] public string? Subcategory { get; set; }
    public bool? IsActive { get; set; }
    public List<string>? Aliases { get; set; }
    [MaxLength(500)] public string? VoiceDescription { get; set; }
}

public class UpdateStockRequest
{
    [Required, Range(0.001, 999999)] public decimal Quantity { get; set; }
    [Required, MaxLength(50)] public string Type { get; set; } = string.Empty;
    [MaxLength(500)] public string? Notes { get; set; }
    [Range(0, 999999999)] public decimal? UnitCost { get; set; }
}

public class UpdatePriceRequest
{
    [Range(0, 999999999)] public decimal? SellingPrice { get; set; }
    [Range(0, 999999999)] public decimal? CostPrice { get; set; }
}

public class ProductDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? SKU { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal? CostPrice { get; set; }
    public decimal? SellingPrice { get; set; }
    public decimal CurrentStock { get; set; }
    public decimal LowStockThreshold { get; set; }
    public bool IsLowStock { get; set; }
    public bool IsActive { get; set; }
    public string? Category { get; set; }
    public string? Subcategory { get; set; }
    public string? Source { get; set; }
    public string? RecordedByName { get; set; }
    public List<string>? Aliases { get; set; }
    public string? VoiceDescription { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

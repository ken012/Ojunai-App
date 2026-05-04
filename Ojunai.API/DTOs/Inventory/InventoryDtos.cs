using System.ComponentModel.DataAnnotations;

namespace Ojunai.API.DTOs.Inventory;

public class StockInRequest
{
    [Required] public Guid ProductId { get; set; }
    [Range(0.001, 999999)] public decimal Quantity { get; set; }
    [Range(0, 999999999)] public decimal? UnitCost { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
}

public class StockOutRequest
{
    [Required] public Guid ProductId { get; set; }
    [Range(0.001, 999999)] public decimal Quantity { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
}

public class AdjustmentRequest
{
    [Required] public Guid ProductId { get; set; }
    [Required, Range(0, 999999)] public decimal NewQuantity { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
}

public class DamagedRequest
{
    [Required] public Guid ProductId { get; set; }
    [Range(0.001, 999999)] public decimal Quantity { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
}

public class ReturnRequest
{
    [Required] public Guid ProductId { get; set; }
    [Range(0.001, 999999)] public decimal Quantity { get; set; }
    [MaxLength(200)] public string? CustomerName { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
}

public class InventoryTransactionDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal? UnitCost { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

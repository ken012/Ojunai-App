using System.ComponentModel.DataAnnotations;

namespace BizPilot.API.DTOs.Inventory;

public class CreateHoldRequest
{
    [Required] public Guid ProductId { get; set; }
    [Required] public string ContactName { get; set; } = string.Empty;
    [Range(0.001, double.MaxValue)] public decimal Quantity { get; set; }
    public string? Notes { get; set; }
}

public class StockHoldDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Source { get; set; } = "Manual";
    public DateTime CreatedAtUtc { get; set; }
}

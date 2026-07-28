using System.ComponentModel.DataAnnotations;

namespace Ojunai.API.DTOs.Inventory;

public class CreateStockTransferRequest
{
    [Required] public Guid ProductId { get; set; }
    [Required] public Guid FromLocationId { get; set; }
    [Required] public Guid ToLocationId { get; set; }
    [Range(typeof(decimal), "0.0001", "999999999")] public decimal Quantity { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
}

public class StockTransferDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public Guid FromLocationId { get; set; }
    public string FromLocationName { get; set; } = string.Empty;
    public Guid ToLocationId { get; set; }
    public string ToLocationName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
    public string? RecordedByName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

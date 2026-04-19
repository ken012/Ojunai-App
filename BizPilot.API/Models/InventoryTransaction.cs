namespace BizPilot.API.Models;

public class InventoryTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    public Guid ProductId { get; set; }
    public InventoryTransactionType Type { get; set; }
    public decimal Quantity { get; set; }
    public decimal? UnitCost { get; set; }
    public string? Notes { get; set; }
    public Guid? RecordedByUserId { get; set; }
    public string? RecordedByName { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
    public Product Product { get; set; } = null!;
}

public enum InventoryTransactionType
{
    StockIn = 1,
    StockOut = 2,
    Adjustment = 3,
    Damaged = 4,
    Wastage = 5
}

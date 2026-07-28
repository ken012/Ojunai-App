namespace Ojunai.API.Models;

public class InventoryTransaction : ILocationScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    public Guid ProductId { get; set; }
    /// <summary>Location this stock movement affected (multi-location; null = default/legacy). Additive Phase 0.</summary>
    public Guid? LocationId { get; set; }
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
    Wastage = 5,
    // Multi-location transfers: the two legs of a StockTransfer, each stamped with its own LocationId (source
    // for Out, destination for In). Informational movement-log rows — the stock move itself is done on the
    // ProductLocationStock rows, and Product.CurrentStock is unchanged.
    TransferOut = 6,
    TransferIn = 7
}

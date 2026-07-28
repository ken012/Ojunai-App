namespace Ojunai.API.Models;

/// <summary>
/// A completed stock movement of one product from one location to another WITHIN a business (instant, not
/// in-transit). Decrements the source <see cref="ProductLocationStock"/> and increments the destination's,
/// leaving <see cref="Product.CurrentStock"/> (the business-wide roll-up) UNCHANGED — a transfer moves stock,
/// it doesn't change the total, so SUM(per-location) == Product.CurrentStock still holds. Recorded for the
/// Transfers history; the service also emits a TransferOut + TransferIn <see cref="InventoryTransaction"/> so
/// the move shows in each location's movement feed. Additive — nothing else depends on it. Plain Guid FK
/// columns (no navigations) to stay migration-simple and avoid multiple-cascade-path issues.
/// </summary>
public class StockTransfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    public Guid ProductId { get; set; }
    public Guid FromLocationId { get; set; }
    public Guid ToLocationId { get; set; }
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
    public Guid? RecordedByUserId { get; set; }
    public string? RecordedByName { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

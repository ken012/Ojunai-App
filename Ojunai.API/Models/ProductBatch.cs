namespace Ojunai.API.Models;

/// <summary>
/// A received lot of a batch-tracked product, with an expiry date. Created on stock-in when the
/// product has <see cref="Product.TracksBatches"/> = true. Powers the expiry report and write-offs.
///
/// V1 is a lot register: quantity is the received amount (informational for expiry visibility), not a
/// live-synced count. Writing off a lot records a wastage that reduces the product's stock. Automatic
/// FEFO depletion on every sale (keeping quantity live) is a V2 enhancement.
/// </summary>
public class ProductBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    public Guid ProductId { get; set; }

    public decimal Quantity { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? LotNumber { get; set; }
    public decimal? CostPrice { get; set; }

    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Set when the lot is written off (expired/discarded). Written-off lots are hidden from the register.</summary>
    public DateTime? WrittenOffAtUtc { get; set; }

    public Product Product { get; set; } = null!;
}

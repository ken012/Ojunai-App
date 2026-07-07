namespace Ojunai.API.Models;

/// <summary>
/// A purchase order: an intent to buy stock from a supplier. Ties supplier + items + costs into
/// one record and tracks "ordered vs received". Receiving a PO updates product stock + cost and
/// (if a supplier is set) creates a payable ledger entry — atomically. Additive feature; nothing
/// else in the system depends on it.
/// </summary>
public class PurchaseOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }

    /// <summary>Supplier this PO is for — a Contact (Supplier/Both). App-level reference, no DB FK.
    /// Nullable for a quick ad-hoc order. <see cref="SupplierName"/> snapshots the name for display.</summary>
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }

    /// <summary>Human-friendly reference, e.g. "PO-0007". Not globally unique (per-business, best-effort).</summary>
    public string PoNumber { get; set; } = string.Empty;

    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public string Currency { get; set; } = "NGN";

    /// <summary>Sum of ordered line totals (Quantity × UnitCost). Snapshot; recomputed on edit.</summary>
    public decimal TotalAmount { get; set; }

    public string? Notes { get; set; }
    public DateTime? ExpectedAtUtc { get; set; }

    public Guid? RecordedByUserId { get; set; }
    public string? RecordedByName { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentAtUtc { get; set; }
    public DateTime? ReceivedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }

    /// <summary>The payable ledger entry created when this PO was received (if a supplier was set).</summary>
    public Guid? PayableLedgerEntryId { get; set; }

    public Business Business { get; set; } = null!;
    public ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();
}

public class PurchaseOrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PurchaseOrderId { get; set; }

    /// <summary>Catalog product, if known. App-level reference (no DB FK). Null = not-yet-created item
    /// captured by name (the receive flow can still add stock once the product exists / is matched).</summary>
    public Guid? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Unit { get; set; } = "unit";

    public decimal QuantityOrdered { get; set; }
    public decimal QuantityReceived { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }

    public PurchaseOrder PurchaseOrder { get; set; } = null!;
}

public enum PurchaseOrderStatus
{
    Draft = 1,
    Sent = 2,
    PartiallyReceived = 3,
    Received = 4,
    Cancelled = 5
}

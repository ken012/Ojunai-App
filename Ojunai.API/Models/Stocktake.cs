namespace Ojunai.API.Models;

/// <summary>
/// A physical stock count session. Snapshots system stock per product, lets the user enter counted
/// quantities, shows the variance, and on completion reconciles each counted product to its counted
/// quantity via an Adjustment inventory transaction (same mechanic as a manual adjust — just batched
/// and auditable). Additive feature; the only stock mutation happens on Complete, transactionally.
/// </summary>
public class Stocktake
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }

    /// <summary>Human reference, e.g. "SC-0003". Per-business, best-effort (not globally unique).</summary>
    public string Reference { get; set; } = string.Empty;

    public StocktakeStatus Status { get; set; } = StocktakeStatus.Draft;

    /// <summary>What was counted, for display — "All products" or "Category: Drinks".</summary>
    public string? Scope { get; set; }
    public string? Notes { get; set; }

    public Guid? RecordedByUserId { get; set; }
    public string? RecordedByName { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }

    public Business Business { get; set; } = null!;
    public ICollection<StocktakeItem> Items { get; set; } = new List<StocktakeItem>();
}

public class StocktakeItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StocktakeId { get; set; }

    /// <summary>App-level reference to the product (no DB FK, consistent with the rest of the schema).</summary>
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Unit { get; set; } = "unit";

    /// <summary>System stock snapshotted when the count was opened (for variance display).</summary>
    public decimal SystemQuantity { get; set; }

    /// <summary>What the user physically counted. Null = not counted yet (left untouched on complete).</summary>
    public decimal? CountedQuantity { get; set; }

    /// <summary>Cost used to value the variance (shrinkage cost). Snapshot of product cost.</summary>
    public decimal UnitCost { get; set; }

    public Stocktake Stocktake { get; set; } = null!;
}

public enum StocktakeStatus
{
    Draft = 1,
    Completed = 2,
    Cancelled = 3
}

namespace Ojunai.API.Models;

/// <summary>
/// Per-location stock for a product. The product CATALOG stays business-wide (one <see cref="Product"/>
/// row — name/SKU/price/variant/bundle unchanged); only the QUANTITY is per-location here.
/// <see cref="Product.CurrentStock"/> is kept as a maintained business-wide roll-up (the sum of
/// CurrentStock across a product's locations), so every existing business-wide query keeps returning the
/// right number. ADDITIVE Phase 0 — nothing reads or writes this yet. See docs/multi-location-spec.md.
/// </summary>
public class ProductLocationStock
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    public Guid ProductId { get; set; }
    public Guid LocationId { get; set; }

    public decimal CurrentStock { get; set; } = 0;

    /// <summary>Per-location override; null falls back to <see cref="Product.LowStockThreshold"/>.</summary>
    public decimal? LowStockThreshold { get; set; }

    // NOTE: intentionally NO optimistic-concurrency token in Phase 1. This row is a passive mirror of the
    // authoritative Product.CurrentStock (whose own rowversion already serializes concurrent stock writes),
    // so the mirror's update is last-write-wins and can never throw DbUpdateConcurrencyException — keeping
    // the best-effort dual-write from ever failing a primary sale/inventory operation. Phase 2, when the
    // per-location row becomes independently authoritative, adds proper concurrency control here.

    public Product Product { get; set; } = null!;
    public Location Location { get; set; } = null!;
}

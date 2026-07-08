namespace Ojunai.API.Models;

public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? SKU { get; set; }
    public string Unit { get; set; } = "unit";
    public decimal? CostPrice { get; set; }
    public decimal? SellingPrice { get; set; }
    public decimal CurrentStock { get; set; } = 0;
    public decimal LowStockThreshold { get; set; } = 5;
    public string? Category { get; set; }
    public string? Subcategory { get; set; }
    public string? Aliases { get; set; } // JSON array: ["alias1", "alias2"]
    public string? VoiceDescription { get; set; } // Short factual description for Voice AI LLM context
    public bool IsActive { get; set; } = true;

    // ── Purchasing / sourcing (Tier 1, additive) ──────────────────────────────
    // Preferred supplier to reorder this product from — a Contact (Supplier/Both). App-level
    // reference (no DB FK) so deleting a contact never blocks; validated when set.
    public Guid? SupplierId { get; set; }
    // Typical days from placing an order to stock arriving — helps time reorders.
    public int? LeadTimeDays { get; set; }
    // Barcode / EAN / UPC for scan-to-lookup. Not unique (merchants legitimately share codes).
    public string? Barcode { get; set; }

    // Bundle/kit: when true, selling this product depletes its BundleComponents' stock instead of
    // its own. Its own CurrentStock is not tracked and it's excluded from low-stock views.
    public bool IsBundle { get; set; } = false;

    // ── Variants (Tier 2, additive) ───────────────────────────────────────────
    // When set, this product is one variant within a VariantGroup (a "style"). It remains a full,
    // sellable/stockable product; the group is only a display/management grouping. Null = standalone.
    public Guid? VariantGroupId { get; set; }
    // JSON of this variant's option values, e.g. {"Size":"M","Color":"Red"}.
    public string? VariantOptions { get; set; }

    // Expiry/batch tracking (Tier 3, opt-in per product). When true, stock-ins can record a lot with
    // an expiry date; expiring lots surface in the expiry report + can be written off. Off by default;
    // products without it behave exactly as before.
    public bool TracksBatches { get; set; } = false;

    public string Source { get; set; } = "Manual";
    public Guid? ImportBatchId { get; set; }
    public Guid? RecordedByUserId { get; set; }
    public string? RecordedByName { get; set; }
    public uint Version { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
    public ICollection<SaleItem> SaleItems { get; set; } = new List<SaleItem>();
    public ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();
}

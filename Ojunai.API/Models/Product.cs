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

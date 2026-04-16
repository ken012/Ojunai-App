namespace BizPilot.API.Models;

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
    public bool IsActive { get; set; } = true;
    public string Source { get; set; } = "Manual";
    public Guid? RecordedByUserId { get; set; }
    public string? RecordedByName { get; set; }
    public uint Version { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
    public ICollection<SaleItem> SaleItems { get; set; } = new List<SaleItem>();
    public ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();
}

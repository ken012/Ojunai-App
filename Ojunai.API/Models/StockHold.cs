namespace Ojunai.API.Models;

public class StockHold : ILocationScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }
    public Guid ProductId { get; set; }
    /// <summary>Location the stock is held at (multi-location; null = default/legacy). Additive Phase 0.</summary>
    public Guid? LocationId { get; set; }
    public string ContactName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
    public HoldStatus Status { get; set; } = HoldStatus.Active;
    public string Source { get; set; } = "Manual";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReleasedAtUtc { get; set; }
    public uint Version { get; set; }

    public Business Business { get; set; } = null!;
    public Product Product { get; set; } = null!;
}

public enum HoldStatus
{
    Active = 1,
    Released = 2,
    Converted = 3
}

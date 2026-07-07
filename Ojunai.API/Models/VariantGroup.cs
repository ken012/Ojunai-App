namespace Ojunai.API.Models;

/// <summary>
/// A "style" that groups several product variants (e.g. "Classic Tee" → Red/S, Red/M, Blue/L…).
/// Purely a grouping + display layer: each variant is a full <see cref="Product"/> (own stock, price,
/// SKU, barcode) with <see cref="Product.VariantGroupId"/> pointing here. Nothing in the sale, stock,
/// receipt, PO, stocktake, or bundle paths depends on this — variants ARE products.
/// </summary>
public class VariantGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>JSON array of option axis names, e.g. ["Size","Color"].</summary>
    public string Axes { get; set; } = "[]";

    public string? Category { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
}

namespace Ojunai.API.Models;

/// <summary>
/// One component of a bundle/kit product. When a product with <see cref="Product.IsBundle"/> = true
/// is sold, each of its components' stock is decremented by (Quantity × bundle quantity sold) instead
/// of the bundle's own stock. App-level product references (no DB FK), consistent with the schema.
/// Components are expected to be regular products (nesting bundles is not supported).
/// </summary>
public class BundleComponent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }

    /// <summary>The product whose IsBundle = true.</summary>
    public Guid BundleProductId { get; set; }

    /// <summary>A regular product consumed when the bundle sells.</summary>
    public Guid ComponentProductId { get; set; }

    /// <summary>Units of the component per one bundle.</summary>
    public decimal Quantity { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

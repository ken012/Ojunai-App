namespace Ojunai.API.Models;

/// <summary>
/// One active à la carte add-on attached to a business. Add-ons are dashboard-only (WBOS bakes
/// equivalents into its tiers). Each add-on has a config row in <c>PricingV2.AddOnCatalog</c>
/// holding price-per-currency and feature grants. The feature gates check both the plan tier
/// AND active add-ons when deciding access.
///
/// Stackable add-ons (e.g. Extra User) use <see cref="Quantity"/>. Most are 1.
/// </summary>
public class BusinessAddOn
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }

    /// <summary>Catalog code, e.g. <c>addon.receipts_pro</c>, <c>addon.branded_pdf</c>.</summary>
    public string AddOnCode { get; set; } = string.Empty;

    /// <summary>active | past_due | cancelled</summary>
    public string Status { get; set; } = "active";

    /// <summary>How many units of this add-on are active (only meaningful for stackable codes like extra_user).</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>Monthly price the business is paying right now, in their billing currency, captured at activation.</summary>
    public decimal BilledAmount { get; set; }

    public string BilledCurrency { get; set; } = "NGN";

    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CancelledAtUtc { get; set; }
    public DateTime? NextBillingAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

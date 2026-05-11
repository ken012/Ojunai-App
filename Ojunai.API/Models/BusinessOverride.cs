namespace Ojunai.API.Models;

/// <summary>
/// Per-business overrides applied on top of the standard plan/add-on logic. Used for:
///   - Grandfathering a customer to old pricing when we change rates
///   - Manual sales comps (free Scale month for a vetted lead)
///   - Manual trials extended beyond the standard window
///   - Locking a price (enterprise contracts)
///
/// Multiple overrides can be active simultaneously (e.g. price-lock + extended trial). The pricing
/// engine evaluates them in priority order: price_lock &gt; sales_comp &gt; manual_trial &gt; grandfather.
///
/// Always created by an admin; <see cref="CreatedByUserId"/> identifies the staff member for audit.
/// </summary>
public class BusinessOverride
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }

    /// <summary>grandfather | manual_trial | sales_comp | price_lock</summary>
    public string OverrideType { get; set; } = string.Empty;

    /// <summary>Original tier the business was on (for grandfather rows). E.g. "shop", "pro".</summary>
    public string? LegacyTier { get; set; }

    /// <summary>Original price kept (for grandfather and price_lock).</summary>
    public decimal? LegacyPriceAmount { get; set; }

    /// <summary>ISO 4217 of <see cref="LegacyPriceAmount"/>.</summary>
    public string? LegacyPriceCurrency { get; set; }

    /// <summary>Override expires after this UTC instant. Null = no expiry.</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    /// <summary>Free-form note from the admin who created the override.</summary>
    public string? ReasonNote { get; set; }

    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAtUtc { get; set; }
}

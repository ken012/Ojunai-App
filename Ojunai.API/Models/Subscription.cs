namespace Ojunai.API.Models;

/// <summary>
/// One active subscription for one product line for one business. Replaces the denormalized
/// fields on <see cref="Business"/> (Plan, SubscriptionStatus, etc.) — those stay in place
/// during the Phase-0/Phase-1 transition and become legacy/derived once cutover is complete.
///
/// Concurrency: a business can have at most ONE active subscription per ProductLine. Cancelled
/// or expired rows stay for audit history. Enforce via partial unique index in EF config.
/// </summary>
public class Subscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }

    /// <summary>Which product this subscription is for. See <see cref="Models.ProductLine"/>.</summary>
    public ProductLine ProductLine { get; set; }

    /// <summary>
    /// Tier within the product line. For Dashboard: starter|lite|operator|professional|scale|enterprise.
    /// For WBOS: solo|pro|scale. For VoiceAi: tiers TBD when Voice AI plan ladder finalizes.
    /// </summary>
    public string Tier { get; set; } = string.Empty;

    /// <summary>active | trialing | past_due | cancelled | expired</summary>
    public string Status { get; set; } = "active";

    /// <summary>monthly | annual</summary>
    public string BillingCycle { get; set; } = "monthly";

    /// <summary>ISO 4217 — currency this subscription bills in (NGN, USD, GHS, KES, ZAR, UGX, GBP).</summary>
    public string BillingCurrency { get; set; } = "NGN";

    /// <summary>paystack | flutterwave | stripe (future) | manual (sales-comp)</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>External provider's subscription identifier (Paystack code, Flutterwave id, etc.).</summary>
    public string? ProviderSubscriptionId { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CurrentPeriodStartsAtUtc { get; set; }
    public DateTime? CurrentPeriodEndsAtUtc { get; set; }
    public DateTime? TrialEndsAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public bool IsAutoRenew { get; set; } = true;

    /// <summary>
    /// Configurable dunning grace window — when payment fails, features stay on for this many days.
    /// Default 2 per current ops policy; admins can override per-business via <see cref="BusinessOverride"/>.
    /// Used by the lapse state machine in Phase 1+.
    /// </summary>
    public int GraceDays { get; set; } = 2;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

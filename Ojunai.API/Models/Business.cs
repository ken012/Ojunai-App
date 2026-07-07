namespace Ojunai.API.Models;

public class Business
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? BusinessType { get; set; }
    public string Currency { get; set; } = "NGN";
    public string Country { get; set; } = "Nigeria";
    public string Timezone { get; set; } = "Africa/Lagos";
    public string? State { get; set; }
    public string? City { get; set; }
    public Guid OwnerUserId { get; set; }
    public string Plan { get; set; } = "starter";
    public string? SubscribedPlan { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public bool IsBillable { get; set; } = true;
    public string? PaystackCustomerCode { get; set; }
    public string? PaystackSubscriptionCode { get; set; }
    public string? PaystackPlanCode { get; set; }
    public string? FlutterwaveSubscriptionId { get; set; }
    public string? FlutterwaveCustomerId { get; set; }
    public string BillingProvider { get; set; } = "paystack";
    public string BillingCycle { get; set; } = "monthly";
    public string BillingCurrency { get; set; } = "NGN";
    public bool IsAutoRenew { get; set; } = true;
    public string SubscriptionStatus { get; set; } = "none";
    public string? PaymentMethod { get; set; }
    public DateTime? SubscriptionEndsAt { get; set; }
    public string? PendingPlanChange { get; set; }
    public decimal LargeSaleThreshold { get; set; } = 100000;
    public string? CustomCategories { get; set; } // JSON array: ["Category1", "Category2"]
    public bool AlertLowStock { get; set; } = true;
    public bool AlertDailySummary { get; set; } = true;
    public bool AlertLargeSale { get; set; } = true;
    // Large-sale confirmation gate, per messaging channel. When enabled, the bot asks the owner
    // to confirm before recording a sale at/above the threshold. ConfirmLargeSales(+Threshold) is
    // the WhatsApp one (kept for back-compat); Telegram/Messenger have their own independent gates.
    public bool ConfirmLargeSales { get; set; } = false;
    public decimal ConfirmLargeSaleThreshold { get; set; } = 0;
    public bool ConfirmLargeSalesTelegram { get; set; } = false;
    public decimal ConfirmLargeSaleThresholdTelegram { get; set; } = 0;
    public bool ConfirmLargeSalesMessenger { get; set; } = false;
    public decimal ConfirmLargeSaleThresholdMessenger { get; set; } = 0;

    // Opt-in flag for product variants (styles with size/color etc.). Off by default; when on, the
    // dashboard surfaces the variant manager. Variants are ordinary products under a VariantGroup,
    // so nothing downstream depends on this flag.
    public bool VariantsEnabled { get; set; } = false;

    // ── Per-source large-sale alert toggles ────────────────────────────────
    // Owners can turn large-sale alerts on/off independently by source so a quiet sales
    // channel doesn't get the same treatment as their main channel. Default all true to
    // preserve existing behavior — owner enabled large-sale alerts globally, they want
    // alerts everywhere. Untoggling per-source narrows the scope.
    public bool LargeSaleAlertWhatsApp { get; set; } = true;
    public bool LargeSaleAlertTelegram { get; set; } = true;
    public bool LargeSaleAlertMessenger { get; set; } = true;
    public bool LargeSaleAlertDashboard { get; set; } = true;

    // ── Dashboard alert toggles ────────────────────────────────────────────
    // Mirror of the WhatsApp toggles above but for in-app notifications. Security
    // and billing alerts (login from new device, payment failed, trial ending,
    // password changed, account recovery) are always-on and not toggleable —
    // those are safety/compliance signals.
    public bool AlertDashboardLowStock { get; set; } = true;
    public bool AlertDashboardDailySummary { get; set; } = true;
    public bool AlertDashboardLargeSale { get; set; } = true;
    public bool AlertDashboardAgedReceivable { get; set; } = true;
    public bool AlertDashboardStaffChanges { get; set; } = true;
    /// <summary>
    /// Daily revenue target. When set, a dashboard alert fires once a day the
    /// moment cumulative sales cross this number. Null = goal feature off.
    /// </summary>
    public decimal? DailySalesGoal { get; set; }

    // ── Custom dashboard background image (Pro + Business plans only) ─────
    /// <summary>UUID-based filename of the saved background image. Null = no custom background.</summary>
    public string? BackgroundImageFileName { get; set; }
    /// <summary>0.0 (image fully visible) → 1.0 (overlay fully opaque, image hidden). Default 0.85 keeps text legible.</summary>
    public decimal BackgroundImageOpacity { get; set; } = 0.85m;
    // ── OjunaiVoice (standalone product, two-tier) ──────────
    public bool VoiceAIEnabled { get; set; } = false;
    public string VoiceAIPlanStatus { get; set; } = "inactive";
    public bool VoiceAIInternalOverride { get; set; } = false;
    public DateTime? VoiceAIEnabledAt { get; set; }
    public DateTime? VoiceAITrialEndsAt { get; set; }
    public string? VoiceAISubscriptionId { get; set; }
    public DateTime? VoiceAISubscriptionEndsAt { get; set; }
    public Guid? VoiceAIBusinessId { get; set; }
    /// <summary>"starter" or "pro". Null while the merchant is on trial or before they've picked a tier.</summary>
    public string? VoiceAITier { get; set; }
    /// <summary>Inbound minutes consumed during the free trial. When this reaches VoiceAITrialMinutes, the trial ends.</summary>
    public int VoiceAITrialMinutesUsed { get; set; } = 0;
    /// <summary>Inbound minutes consumed in the current paid billing cycle. Resets on renewal.</summary>
    public int VoiceAICycleMinutesUsed { get; set; } = 0;

    public bool IsActive { get; set; } = true;
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>
    /// Per-business kill switch for the new pricing/gating engine. Phase 0 lands schema; Phase 1+ flips
    /// this on per business so we can roll out safely. When false, all reads and gating still go through
    /// the legacy <c>PlanLimits</c>/<c>Business.Plan</c> path.
    /// </summary>
    public bool PricingV2Enabled { get; set; } = false;

    // ── Receipts ─────────────────────────────────────
    public string? Address { get; set; }                       // Single-line business address printed on receipts
    public string? ReceiptPrefix { get; set; }                 // Auto-derived from Name on first generation, e.g. "GD" for "Glow Daddy"
    public int NextReceiptNumber { get; set; } = 1;            // Atomic sequence counter
    public bool VatEnabled { get; set; } = false;              // Show VAT line on receipts + default ON for new sales
    public decimal VatRate { get; set; } = 7.5m;               // Nigeria standard
    public string? TaxId { get; set; }                         // TIN — printed on receipts if set
    public string? ReceiptHeaderText { get; set; }             // Optional override for business name on receipts (rare; defaults to Name)
    public string? ReceiptFooterText { get; set; }             // Override default footer thank-you message
    public string? ReceiptAccentColor { get; set; }            // Hex color for receipt accents (default cyan-500 #06b6d4)

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
    public ICollection<Contact> Contacts { get; set; } = new List<Contact>();
    public ICollection<DailySummary> DailySummaries { get; set; } = new List<DailySummary>();
}

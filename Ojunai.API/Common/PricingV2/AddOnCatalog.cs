namespace Ojunai.API.Common.PricingV2;

/// <summary>
/// À la carte add-ons that attach to a <c>Dashboard</c> subscription. Each add-on has a
/// catalog code (e.g. <c>addon.receipts_pro</c>), a price-per-currency table, and a set of
/// feature flags it grants when active.
///
/// Note on terminology: these are "add-on codes," not "SKUs" — the existing Product table
/// owns "SKU" semantics for the user's inventory items.
///
/// PHASE-0 ADDITIVE — sits parallel to the legacy Voice-AI add-on flow on Business. Phase-2
/// wires the purchase + lifecycle flows for these.
///
/// Voice AI appears here as <c>addon.voice_ai</c> AND as a standalone product line. A business
/// may pick one path or the other; the billing engine blocks duplicate activation.
/// </summary>
public static class AddOnCatalog
{
    public sealed record AddOn(
        string Code,
        string DisplayName,
        bool Stackable,           // Extra User can be bought N times; everything else is 1
        IReadOnlyDictionary<string, decimal> MonthlyPrice,  // currency code → amount
        IReadOnlySet<string> Grants                          // feature flags this turns on
    );

    /// <summary>
    /// Single source of truth. Prices match the marketing-site copy; if a customer reports a
    /// mismatch, the marketing site is the tiebreaker — update here, not there.
    /// </summary>
    public static readonly Dictionary<string, AddOn> Items = new(StringComparer.OrdinalIgnoreCase)
    {
        ["addon.receipts_pro"] = new(
            Code: "addon.receipts_pro",
            DisplayName: "Receipts Pro",
            Stackable: false,
            MonthlyPrice: PriceMatrix.AddOnPrice("receipts_pro"),
            Grants: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "receipt.advanced_email", "receipt.advanced_pdf",
            }),

        ["addon.branded_pdf"] = new(
            Code: "addon.branded_pdf",
            DisplayName: "Branded PDF",
            Stackable: false,
            MonthlyPrice: PriceMatrix.AddOnPrice("branded_pdf"),
            Grants: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pdf.branded" }),

        ["addon.audit_logs_pro"] = new(
            Code: "addon.audit_logs_pro",
            DisplayName: "Audit Logs Pro",
            Stackable: false,
            MonthlyPrice: PriceMatrix.AddOnPrice("audit_logs_pro"),
            Grants: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "audit_log_extended" }),

        ["addon.operations_pack"] = new(
            Code: "addon.operations_pack",
            DisplayName: "Operations Pro Pack",
            Stackable: false,
            MonthlyPrice: PriceMatrix.AddOnPrice("operations_pack"),
            // Bundle: receipts_pro + branded_pdf + audit_logs_pro at $14.99 vs. $23.97 individually.
            Grants: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "receipt.advanced_email", "receipt.advanced_pdf",
                "pdf.branded",
                "audit_log_extended",
            }),

        ["addon.advanced_alerts"] = new(
            Code: "addon.advanced_alerts",
            DisplayName: "Advanced Alerts",
            Stackable: false,
            MonthlyPrice: PriceMatrix.AddOnPrice("advanced_alerts"),
            Grants: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alerts.advanced" }),

        ["addon.bulk_announce"] = new(
            Code: "addon.bulk_announce",
            DisplayName: "Bulk Announcements",
            Stackable: false,
            MonthlyPrice: PriceMatrix.AddOnPrice("bulk_announce"),
            Grants: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "wa.broadcast" }),

        ["addon.custom_dashboards"] = new(
            Code: "addon.custom_dashboards",
            DisplayName: "Custom Dashboards",
            Stackable: false,
            MonthlyPrice: PriceMatrix.AddOnPrice("custom_dashboards"),
            Grants: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dashboard.custom" }),

        ["addon.export_hub"] = new(
            Code: "addon.export_hub",
            DisplayName: "Advanced Export Hub",
            Stackable: false,
            MonthlyPrice: PriceMatrix.AddOnPrice("export_hub"),
            Grants: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "export.advanced" }),

        ["addon.multi_location"] = new(
            Code: "addon.multi_location",
            DisplayName: "Multi-location",
            Stackable: false,
            MonthlyPrice: PriceMatrix.AddOnPrice("multi_location"),
            Grants: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "multi_location" }),

        ["addon.extra_user"] = new(
            Code: "addon.extra_user",
            DisplayName: "Extra User",
            Stackable: true,  // can be bought N times; quantity tracked on BusinessAddOn
            MonthlyPrice: PriceMatrix.AddOnPrice("extra_user"),
            Grants: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "user.seat" }),

        ["addon.voice_ai"] = new(
            Code: "addon.voice_ai",
            DisplayName: "Voice AI (Add-on)",
            Stackable: false,
            MonthlyPrice: PriceMatrix.AddOnPrice("voice_ai"),
            // The same feature flag is set when Voice AI is bought as a standalone Subscription.
            // Mutual-exclusion check at activation time prevents double-activation.
            Grants: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "voice_ai" }),
    };

    public static AddOn? Get(string? code) => code is null ? null : Items.GetValueOrDefault(code);
}

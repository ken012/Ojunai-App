namespace Ojunai.API.Common.PricingV2;

/// <summary>
/// Source of truth for v2 plan tiers. Two product lines defined here:
///   - Dashboard ladder: <c>starter, lite, operator, professional, scale, enterprise</c>
///   - WBOS ladder:      <c>solo, pro, scale</c>
///
/// PHASE-0 ADDITIVE ONLY — this lives parallel to legacy <see cref="PlanLimits"/>. Nothing reads
/// from it yet. Phase-1 wires the gating engine to consult this catalog when a business has
/// <c>PricingV2Enabled = true</c>.
///
/// Every feature flag the gating engine checks must appear in <see cref="DashboardTier.Features"/>
/// or <see cref="WbosTier.Features"/> below — otherwise the gate silently passes (fail-open is the
/// safer default for new flags during rollout; switch to fail-closed once the catalog is stable).
/// </summary>
public static class PlanCatalog
{
    // ─── Dashboard product line ────────────────────────────────────────────────

    /// <summary>
    /// Dashboard tier definition. <see cref="Features"/> is the canonical feature-flag bag the
    /// gating engine consults; <see cref="WaActionsPerWeek"/> drives the weekly cap counter.
    /// Trial config is intentional per-tier per ops policy: Lite gets a generous 30-day trial,
    /// Operator and Professional get 5 days, Scale and Enterprise are sales-led.
    /// </summary>
    public sealed record DashboardTier(
        string Code,
        string DisplayName,
        int UserSeats,
        int WaActionsPerWeek,
        int TrialDays,
        int TrialActionCap,
        bool RequiresSalesContact,
        IReadOnlySet<string> Features
    );

    public static readonly Dictionary<string, DashboardTier> Dashboard = new(StringComparer.OrdinalIgnoreCase)
    {
        ["starter"] = new(
            Code: "starter",
            DisplayName: "Starter",
            UserSeats: 1,
            WaActionsPerWeek: 0,
            TrialDays: 0,
            TrialActionCap: 0,
            RequiresSalesContact: false,
            Features: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "inventory", "sales", "expenses", "basic_reports",
            }),

        ["lite"] = new(
            Code: "lite",
            DisplayName: "Lite",
            UserSeats: 1,
            WaActionsPerWeek: 10,
            TrialDays: 30,
            TrialActionCap: 30,
            RequiresSalesContact: false,
            Features: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "inventory", "sales", "expenses", "basic_reports",
                "customer_records", "debt_tracking", "receipt.basic_email",
            }),

        ["operator"] = new(
            Code: "operator",
            DisplayName: "Operator",
            UserSeats: 1,
            WaActionsPerWeek: 35,
            TrialDays: 5,
            TrialActionCap: 30,
            RequiresSalesContact: false,
            Features: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "inventory", "sales", "expenses", "basic_reports",
                "customer_records", "debt_tracking", "receipt.basic_email",
                "manual_payment_tracking",
            }),

        ["professional"] = new(
            Code: "professional",
            DisplayName: "Professional",
            UserSeats: 3,
            WaActionsPerWeek: 95,
            TrialDays: 5,
            TrialActionCap: 30,
            RequiresSalesContact: false,
            Features: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "inventory", "sales", "expenses", "basic_reports",
                "customer_records", "debt_tracking", "receipt.basic_email",
                "manual_payment_tracking",
                "profit_insights", "advanced_analytics", "product_performance",
                "data_export", "pdf.basic",
            }),

        ["scale"] = new(
            Code: "scale",
            DisplayName: "Scale",
            UserSeats: 6,
            WaActionsPerWeek: 250,
            TrialDays: 0,
            TrialActionCap: 0,
            RequiresSalesContact: false,
            Features: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "inventory", "sales", "expenses", "basic_reports",
                "customer_records", "debt_tracking", "receipt.basic_email",
                "manual_payment_tracking",
                "profit_insights", "advanced_analytics", "product_performance",
                "data_export", "pdf.basic",
                "receipt.branded_email", "pdf.branded",
                "custom_branding", "multi_user", "multi_location",
                "audit_log_90d", "csv_import", "bulk_price_updates",
                "automation_alerts", "support.priority",
            }),

        ["enterprise"] = new(
            Code: "enterprise",
            DisplayName: "Enterprise",
            UserSeats: int.MaxValue,
            WaActionsPerWeek: int.MaxValue, // negotiated
            TrialDays: 0,
            TrialActionCap: 0,
            RequiresSalesContact: true,
            Features: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "inventory", "sales", "expenses", "basic_reports",
                "customer_records", "debt_tracking", "receipt.basic_email",
                "manual_payment_tracking",
                "profit_insights", "advanced_analytics", "product_performance",
                "data_export", "pdf.basic",
                "receipt.branded_email", "pdf.branded",
                "custom_branding", "multi_user", "multi_location",
                "audit_log_extended", "csv_import", "bulk_price_updates",
                "automation_alerts", "support.dedicated",
                "dedicated_onboarding", "api_access", "custom_integrations",
            }),
    };

    // ─── WBOS product line ─────────────────────────────────────────────────────

    public sealed record WbosTier(
        string Code,
        string DisplayName,
        int UserSeats,
        int ActionsPerMonth,
        int SkuLimit,
        int PdfsPerMonth,
        int HistoryDays,
        bool RequiresSalesContact,
        IReadOnlySet<string> Features
    );

    /// <summary>
    /// Sentinel meaning "unlimited" for WBOS tier limits. We use int.MaxValue rather than -1
    /// so cap-check arithmetic doesn't need a sentinel branch.
    /// </summary>
    public const int Unlimited = int.MaxValue;

    public static readonly Dictionary<string, WbosTier> Wbos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["solo"] = new(
            Code: "solo",
            DisplayName: "WBOS Solo",
            UserSeats: 1,
            ActionsPerMonth: 1_200,
            SkuLimit: 100,
            PdfsPerMonth: 50,
            HistoryDays: 30,
            RequiresSalesContact: false,
            Features: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "wbos.core",
            }),

        ["pro"] = new(
            Code: "pro",
            DisplayName: "WBOS Pro",
            UserSeats: 3,
            ActionsPerMonth: 6_000,
            SkuLimit: Unlimited,
            PdfsPerMonth: Unlimited,
            HistoryDays: 90,
            RequiresSalesContact: false,
            Features: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "wbos.core", "wbos.branded_pdf", "wbos.bulk_announce",
            }),

        ["scale"] = new(
            Code: "scale",
            DisplayName: "WBOS Scale",
            UserSeats: 6,
            ActionsPerMonth: Unlimited,
            SkuLimit: Unlimited,
            PdfsPerMonth: Unlimited,
            HistoryDays: 365,
            RequiresSalesContact: true,
            Features: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "wbos.core", "wbos.branded_pdf", "wbos.bulk_announce",
                // wbos.custom_pdf_templates is bookable but NOT BUILT yet — exposed in catalog so
                // sales can sell it; gating returns "coming soon" until the templating engine ships.
                "wbos.custom_pdf_templates",
                "wbos.bulk_segmentation",
            }),
    };

    // ─── Lookups ───────────────────────────────────────────────────────────────

    public static DashboardTier? GetDashboard(string? code)
        => code is null ? null : Dashboard.GetValueOrDefault(code);

    public static WbosTier? GetWbos(string? code)
        => code is null ? null : Wbos.GetValueOrDefault(code);

    public static readonly string[] DashboardCodes = { "starter", "lite", "operator", "professional", "scale", "enterprise" };
    public static readonly string[] WbosCodes = { "solo", "pro", "scale" };
}

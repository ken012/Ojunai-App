namespace Ojunai.API.Common.PricingV2;

/// <summary>
/// 7-currency fixed local pricing — every supported plan, WBOS tier, and add-on.
/// NOT live FX. Each price is set deliberately for the local market, mirroring the
/// canonical priceMatrix on the marketing site at ojunai.com/pricing.
///
/// If a price here drifts from the marketing site, the marketing site is the
/// tiebreaker — fix here, deploy. Any consumer of these prices uses
/// <see cref="GetPrice"/> / <see cref="GetWbosPrice"/> / <see cref="AddOnPrice"/>;
/// no live conversion ever happens.
///
/// Currencies (ISO 4217): USD, NGN, GHS, KES, ZAR, UGX, GBP.
/// Provider routing (Phase-1+):
///   NGN → Paystack
///   GHS, KES, ZAR, UGX → Flutterwave
///   USD, GBP → Flutterwave (per current ops decision)
/// </summary>
public static class PriceMatrix
{
    public static readonly string[] SupportedCurrencies =
        { "USD", "NGN", "GHS", "KES", "ZAR", "UGX", "GBP" };

    // ─── Dashboard plan prices ─────────────────────────────────────────────────
    // Both monthly and yearly are explicit values from the canonical marketing-site
    // priceMatrix at github.com/ken012/Ojunai-Website/src/pages/pricing.astro.
    // Yearly is "≈ 2 months free" but rounded to clean local-currency amounts
    // (NOT mechanically monthly × 10), so we store both.

    public sealed record TierPrices(
        IReadOnlyDictionary<string, decimal> Monthly,
        IReadOnlyDictionary<string, decimal> Yearly
    );

    private static readonly Dictionary<string, TierPrices> DashboardPrices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lite"] = new(
            Monthly: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["USD"] = 12.99m, ["NGN"] = 18_999, ["GHS"] = 149,
                ["KES"] = 1_699, ["ZAR"] = 229, ["UGX"] = 48_999, ["GBP"] = 9.99m,
            },
            Yearly: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["USD"] = 129, ["NGN"] = 189_999, ["GHS"] = 1_499,
                ["KES"] = 16_999, ["ZAR"] = 2_289, ["UGX"] = 489_999, ["GBP"] = 99.99m,
            }),

        ["operator"] = new(
            Monthly: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["USD"] = 29, ["NGN"] = 42_000, ["GHS"] = 349,
                ["KES"] = 3_800, ["ZAR"] = 529, ["UGX"] = 109_000, ["GBP"] = 22.99m,
            },
            Yearly: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["USD"] = 290, ["NGN"] = 429_999, ["GHS"] = 3_499,
                ["KES"] = 37_999, ["ZAR"] = 5_299, ["UGX"] = 1_089_999, ["GBP"] = 229,
            }),

        ["professional"] = new(
            Monthly: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["USD"] = 79, ["NGN"] = 115_000, ["GHS"] = 949,
                ["KES"] = 10_500, ["ZAR"] = 1_449, ["UGX"] = 295_000, ["GBP"] = 62.99m,
            },
            Yearly: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["USD"] = 790, ["NGN"] = 1_149_999, ["GHS"] = 9_499,
                ["KES"] = 104_999, ["ZAR"] = 14_499, ["UGX"] = 2_949_999, ["GBP"] = 629.99m,
            }),

        ["scale"] = new(
            Monthly: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["USD"] = 199, ["NGN"] = 289_000, ["GHS"] = 2_399,
                ["KES"] = 26_500, ["ZAR"] = 3_649, ["UGX"] = 745_000, ["GBP"] = 159,
            },
            Yearly: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["USD"] = 1_990, ["NGN"] = 2_889_999, ["GHS"] = 23_999,
                ["KES"] = 264_999, ["ZAR"] = 36_499, ["UGX"] = 7_449_999, ["GBP"] = 1_599,
            }),

        // starter = free; enterprise = custom — no rows here.
    };

    /// <summary>
    /// Look up the price for a (tier, cycle, currency). <paramref name="cycle"/> accepts
    /// "monthly" or "yearly" (alias: "annual"). Returns null when the combination isn't priced
    /// (Starter / Enterprise / unsupported currency).
    /// </summary>
    public static decimal? GetPrice(string tier, string cycle, string currency)
    {
        if (!DashboardPrices.TryGetValue(tier, out var prices)) return null;
        var table = NormalizeCycle(cycle) == "yearly" ? prices.Yearly : prices.Monthly;
        return table.TryGetValue(currency, out var amount) ? amount : null;
    }

    // ─── WBOS tier prices ──────────────────────────────────────────────────────

    private static readonly Dictionary<string, TierPrices> WbosPrices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["solo"] = new(
            Monthly: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["USD"] = 49.99m, ["NGN"] = 74_999, ["GHS"] = 599,
                ["KES"] = 6_499, ["ZAR"] = 899, ["UGX"] = 187_499, ["GBP"] = 39.99m,
            },
            Yearly: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["USD"] = 499, ["NGN"] = 749_999, ["GHS"] = 5_999,
                ["KES"] = 64_999, ["ZAR"] = 8_999, ["UGX"] = 1_874_999, ["GBP"] = 399.99m,
            }),

        ["pro"] = new(
            Monthly: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["USD"] = 145.99m, ["NGN"] = 219_999, ["GHS"] = 1_749,
                ["KES"] = 18_999, ["ZAR"] = 2_649, ["UGX"] = 547_499, ["GBP"] = 116.99m,
            },
            Yearly: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["USD"] = 1_459, ["NGN"] = 2_189_999, ["GHS"] = 17_499,
                ["KES"] = 189_999, ["ZAR"] = 26_499, ["UGX"] = 5_474_999, ["GBP"] = 1_159.99m,
            }),

        // wbos.scale = custom — no row here, sales-led.
    };

    public static decimal? GetWbosPrice(string tier, string cycle, string currency)
    {
        if (!WbosPrices.TryGetValue(tier, out var prices)) return null;
        var table = NormalizeCycle(cycle) == "yearly" ? prices.Yearly : prices.Monthly;
        return table.TryGetValue(currency, out var amount) ? amount : null;
    }

    /// <summary>Normalizes "annual" → "yearly" so callers from either dialect work. Anything else is "monthly".</summary>
    public static string NormalizeCycle(string? cycle)
    {
        if (string.IsNullOrWhiteSpace(cycle)) return "monthly";
        var c = cycle.Trim().ToLowerInvariant();
        if (c == "annual" || c == "yearly" || c == "yr" || c == "year") return "yearly";
        return "monthly";
    }

    // ─── Add-on prices (monthly) ───────────────────────────────────────────────
    //
    // Add-ons are billed in the business's billing currency. Marketing site quotes
    // them in USD; local conversions are fixed (matching the same-percentage uplift
    // we use for plans). When a price here doesn't cover a currency, the add-on
    // can't be sold in that currency until a row is added — fail-closed.

    private static readonly Dictionary<string, Dictionary<string, decimal>> AddOnsMonthly = new(StringComparer.OrdinalIgnoreCase)
    {
        ["receipts_pro"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 6.99m, ["NGN"] = 9_999, ["GHS"] = 79,
            ["KES"] = 899, ["ZAR"] = 119, ["UGX"] = 25_999, ["GBP"] = 5.49m,
        },
        ["branded_pdf"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 8.99m, ["NGN"] = 12_999, ["GHS"] = 99,
            ["KES"] = 1_199, ["ZAR"] = 159, ["UGX"] = 33_999, ["GBP"] = 6.99m,
        },
        ["audit_logs_pro"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 7.99m, ["NGN"] = 11_499, ["GHS"] = 89,
            ["KES"] = 1_049, ["ZAR"] = 139, ["UGX"] = 29_999, ["GBP"] = 6.29m,
        },
        ["operations_pack"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 14.99m, ["NGN"] = 21_999, ["GHS"] = 169,
            ["KES"] = 1_999, ["ZAR"] = 269, ["UGX"] = 56_999, ["GBP"] = 11.99m,
        },
        ["advanced_alerts"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 5.99m, ["NGN"] = 8_499, ["GHS"] = 65,
            ["KES"] = 759, ["ZAR"] = 99, ["UGX"] = 21_999, ["GBP"] = 4.79m,
        },
        ["bulk_announce"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 9.99m, ["NGN"] = 14_499, ["GHS"] = 109,
            ["KES"] = 1_299, ["ZAR"] = 179, ["UGX"] = 36_999, ["GBP"] = 7.99m,
        },
        ["custom_dashboards"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 7.99m, ["NGN"] = 11_499, ["GHS"] = 89,
            ["KES"] = 1_049, ["ZAR"] = 139, ["UGX"] = 29_999, ["GBP"] = 6.29m,
        },
        ["export_hub"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 10, ["NGN"] = 14_499, ["GHS"] = 109,
            ["KES"] = 1_299, ["ZAR"] = 179, ["UGX"] = 36_999, ["GBP"] = 7.99m,
        },
        ["multi_location"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 10.99m, ["NGN"] = 15_999, ["GHS"] = 119,
            ["KES"] = 1_429, ["ZAR"] = 199, ["UGX"] = 40_999, ["GBP"] = 8.79m,
        },
        ["extra_user"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 7.99m, ["NGN"] = 11_499, ["GHS"] = 89,
            ["KES"] = 1_049, ["ZAR"] = 139, ["UGX"] = 29_999, ["GBP"] = 6.29m,
        },
        ["voice_ai"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 39, ["NGN"] = 56_999, ["GHS"] = 449,
            ["KES"] = 5_199, ["ZAR"] = 729, ["UGX"] = 145_999, ["GBP"] = 30.99m,
        },
    };

    /// <summary>Returns the per-currency price table for one add-on, used by AddOnCatalog.</summary>
    public static IReadOnlyDictionary<string, decimal> AddOnPrice(string addOnKey)
        => AddOnsMonthly.TryGetValue(addOnKey, out var prices)
            ? prices
            : new Dictionary<string, decimal>();

    public static bool IsCurrencySupported(string? currency)
        => !string.IsNullOrWhiteSpace(currency)
           && Array.Exists(SupportedCurrencies, c => c.Equals(currency, StringComparison.OrdinalIgnoreCase));

    // ─── Usage packs (one-time top-ups) ────────────────────────────────────────

    /// <summary>
    /// Starter WhatsApp packs — one-time monthly purchases for free Starter users to gain WhatsApp
    /// access without upgrading. USD list price; localized via the same currency table.
    /// Spec: $6 / $10 / $15 → 50 / 120 / 250 actions per month.
    /// </summary>
    public static readonly (int Actions, decimal UsdPrice)[] StarterPacks =
    {
        (50, 6m),
        (120, 10m),
        (250, 15m),
    };

    /// <summary>
    /// One-time top-up packs for any paid dashboard tier — bumps current-period cap, doesn't
    /// expire, but resets to zero if the underlying subscription is cancelled.
    /// Spec: $5 / $10 / $13 / $18 / $30 → 50 / 150 / 200 / 300 / 600 actions.
    /// </summary>
    public static readonly (int Actions, decimal UsdPrice)[] TopUpPacks =
    {
        (50, 5m),
        (150, 10m),
        (200, 13m),
        (300, 18m),
        (600, 30m),
    };
}

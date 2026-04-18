namespace BizPilot.API.Common;

/// <summary>
/// Central billing configuration. Fixed localized prices for all plans, currencies, and billing cycles.
/// No live FX conversion — every price is pre-set. Provider routing is currency-driven:
/// NGN → Paystack, everything else → Flutterwave.
/// </summary>
public static class BillingConfig
{
    public enum BillingProvider { Paystack, Flutterwave }
    public enum BillingCycle { Monthly, Annual }

    public static readonly string[] SupportedCurrencies = { "NGN", "GHS", "USD", "GBP", "KES", "ZAR" };

    public static readonly Dictionary<string, CurrencyMeta> CurrencyMetadata = new()
    {
        ["NGN"] = new("₦", "Nigerian Naira", "NGN"),
        ["GHS"] = new("GH₵", "Ghanaian Cedi", "GHS"),
        ["USD"] = new("$", "US Dollar", "USD"),
        ["GBP"] = new("£", "British Pound", "GBP"),
        ["KES"] = new("KSh", "Kenyan Shilling", "KES"),
        ["ZAR"] = new("R", "South African Rand", "ZAR"),
    };

    public record CurrencyMeta(string Symbol, string Name, string Code);

    // Annual discount percentages per plan (for display badges)
    public static readonly Dictionary<string, int> AnnualDiscountPercent = new()
    {
        ["starter"] = 10,
        ["shop"] = 15,
        ["pro"] = 17,
        ["business"] = 20,
    };

    /// <summary>
    /// Fixed localized pricing. [plan][cycle][currency] → amount in the currency's smallest displayable unit.
    /// These are NOT converted at runtime. Each price is manually set for the market.
    /// </summary>
    private static readonly Dictionary<string, Dictionary<BillingCycle, Dictionary<string, decimal>>> Prices = new()
    {
        ["starter"] = new()
        {
            [BillingCycle.Monthly] = new()
            {
                ["NGN"] = 3500, ["GHS"] = 32, ["USD"] = 3.49m, ["GBP"] = 2.99m, ["KES"] = 480, ["ZAR"] = 50
            },
            [BillingCycle.Annual] = new()
            {
                ["NGN"] = 37800, ["GHS"] = 346, ["USD"] = 37.69m, ["GBP"] = 32.29m, ["KES"] = 5184, ["ZAR"] = 540
            }
        },
        ["shop"] = new()
        {
            [BillingCycle.Monthly] = new()
            {
                ["NGN"] = 7500, ["GHS"] = 65, ["USD"] = 6.99m, ["GBP"] = 5.99m, ["KES"] = 1000, ["ZAR"] = 95
            },
            [BillingCycle.Annual] = new()
            {
                ["NGN"] = 76500, ["GHS"] = 663, ["USD"] = 71.30m, ["GBP"] = 61.10m, ["KES"] = 10200, ["ZAR"] = 969
            }
        },
        ["pro"] = new()
        {
            [BillingCycle.Monthly] = new()
            {
                ["NGN"] = 12500, ["GHS"] = 115, ["USD"] = 11.99m, ["GBP"] = 9.99m, ["KES"] = 1650, ["ZAR"] = 160
            },
            [BillingCycle.Annual] = new()
            {
                ["NGN"] = 124500, ["GHS"] = 1145, ["USD"] = 119.42m, ["GBP"] = 99.50m, ["KES"] = 16434, ["ZAR"] = 1594
            }
        },
        ["business"] = new()
        {
            [BillingCycle.Monthly] = new()
            {
                ["NGN"] = 30000, ["GHS"] = 270, ["USD"] = 24.99m, ["GBP"] = 19.99m, ["KES"] = 3900, ["ZAR"] = 380
            },
            [BillingCycle.Annual] = new()
            {
                ["NGN"] = 288000, ["GHS"] = 2592, ["USD"] = 239.90m, ["GBP"] = 191.90m, ["KES"] = 37440, ["ZAR"] = 3648
            }
        }
    };

    /// <summary>Get the fixed price for a plan + cycle + currency. Returns null if the combination is invalid.</summary>
    public static decimal? GetPrice(string plan, BillingCycle cycle, string currency)
    {
        if (Prices.TryGetValue(plan.ToLower(), out var cycles)
            && cycles.TryGetValue(cycle, out var currencies)
            && currencies.TryGetValue(currency.ToUpper(), out var price))
            return price;
        return null;
    }

    /// <summary>Get the monthly equivalent of an annual price (for "≈ $X/mo billed yearly" display).</summary>
    public static decimal? GetMonthlyEquivalent(string plan, string currency)
    {
        var annual = GetPrice(plan, BillingCycle.Annual, currency);
        return annual.HasValue ? Math.Round(annual.Value / 12, 2) : null;
    }

    /// <summary>
    /// Route to the correct billing provider based on currency.
    /// NGN → Paystack (primary Nigerian provider). Everything else → Flutterwave.
    /// </summary>
    public static BillingProvider GetProvider(string currency)
        => currency.ToUpper() == "NGN" ? BillingProvider.Paystack : BillingProvider.Flutterwave;

    /// <summary>Get the currency symbol for a currency code. Covers all supported African currencies.</summary>
    public static string Symbol(string? currency) => (currency?.ToUpper() ?? "NGN") switch
    {
        "NGN" => "₦", "GHS" => "GH₵", "USD" => "$", "GBP" => "£", "KES" => "KSh",
        "ZAR" => "R", "TZS" => "TSh", "UGX" => "USh", "RWF" => "RF", "XAF" => "FCFA",
        "XOF" => "CFA", "EGP" => "E£", "ETB" => "Br", "CDF" => "FC", "AOA" => "Kz",
        "MZN" => "MT", "ZMW" => "ZK", "BWP" => "P", "NAD" => "N$", "MWK" => "MK",
        "EUR" => "€", "CAD" => "C$", "SLE" => "Le", "LRD" => "L$", "GMD" => "D",
        var c => c ?? "₦"
    };

    /// <summary>Format a price with the correct currency symbol.</summary>
    public static string FormatPrice(decimal amount, string currency)
    {
        var meta = CurrencyMetadata.GetValueOrDefault(currency.ToUpper());
        var symbol = meta?.Symbol ?? currency;
        // Whole-number currencies (NGN, GHS, KES, ZAR) don't need decimals
        var format = amount == Math.Floor(amount) ? $"{symbol}{amount:N0}" : $"{symbol}{amount:N2}";
        return format;
    }

    /// <summary>Validate that a plan + cycle + currency combination is billable.</summary>
    public static bool IsValidCombination(string plan, string cycle, string currency)
    {
        if (!Enum.TryParse<BillingCycle>(cycle, true, out var bc)) return false;
        return GetPrice(plan, bc, currency).HasValue;
    }

    /// <summary>Get all prices for a plan across all currencies and cycles (for API responses).</summary>
    public static object GetPlanPricing(string plan)
    {
        var p = plan.ToLower();
        if (!Prices.ContainsKey(p)) return new { };
        return new
        {
            monthly = Prices[p][BillingCycle.Monthly],
            annual = Prices[p][BillingCycle.Annual],
            annualDiscount = AnnualDiscountPercent.GetValueOrDefault(p, 0)
        };
    }

    /// <summary>Get all pricing data for the frontend pricing page.</summary>
    public static object GetAllPricing()
    {
        var result = new Dictionary<string, object>();
        foreach (var plan in Prices.Keys)
        {
            result[plan] = GetPlanPricing(plan);
        }
        return new { plans = result, currencies = SupportedCurrencies };
    }
}

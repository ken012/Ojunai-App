namespace Ojunai.API.Common;

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

    // Annual discount percentages per plan (for display badges) — 2 months free = ~17%.
    public static readonly Dictionary<string, int> AnnualDiscountPercent = new()
    {
        ["starter"] = 0,
        ["lite"] = 17,
        ["operator"] = 17,
        ["pro"] = 17,
        ["scale"] = 17,
    };

    /// <summary>
    /// Fixed localized pricing. [plan][cycle][currency] → amount in the currency's smallest displayable unit.
    /// These are NOT converted at runtime. PPP-adjusted (not strict FX) so the African market doesn't
    /// see USD-equivalent prices that don't match local purchasing power. Annual = monthly × 10
    /// ("2 months free"). WhatsApp is sold separately via BillingConfig.WhatsAppPackPrices — these
    /// tier prices cover dashboard + Telegram + Messenger only.
    /// </summary>
    private static readonly Dictionary<string, Dictionary<BillingCycle, Dictionary<string, decimal>>> Prices = new()
    {
        ["starter"] = new()
        {
            [BillingCycle.Monthly] = new()
            {
                ["NGN"] = 0, ["GHS"] = 0, ["USD"] = 0, ["GBP"] = 0, ["KES"] = 0, ["ZAR"] = 0
            },
            [BillingCycle.Annual] = new()
            {
                ["NGN"] = 0, ["GHS"] = 0, ["USD"] = 0, ["GBP"] = 0, ["KES"] = 0, ["ZAR"] = 0
            }
        },
        ["lite"] = new()
        {
            [BillingCycle.Monthly] = new()
            {
                ["NGN"] = 12500, ["GHS"] = 125, ["USD"] = 11.99m, ["GBP"] = 9.99m, ["KES"] = 1099, ["ZAR"] = 199
            },
            [BillingCycle.Annual] = new()
            {
                ["NGN"] = 125000, ["GHS"] = 1250, ["USD"] = 119.90m, ["GBP"] = 99.90m, ["KES"] = 10990, ["ZAR"] = 1990
            }
        },
        ["operator"] = new()
        {
            [BillingCycle.Monthly] = new()
            {
                ["NGN"] = 29999, ["GHS"] = 299, ["USD"] = 28.99m, ["GBP"] = 22.99m, ["KES"] = 2599, ["ZAR"] = 499
            },
            [BillingCycle.Annual] = new()
            {
                ["NGN"] = 299990, ["GHS"] = 2990, ["USD"] = 289.90m, ["GBP"] = 229.90m, ["KES"] = 25990, ["ZAR"] = 4990
            }
        },
        ["pro"] = new()
        {
            [BillingCycle.Monthly] = new()
            {
                ["NGN"] = 72500, ["GHS"] = 729, ["USD"] = 69.99m, ["GBP"] = 55.99m, ["KES"] = 6299, ["ZAR"] = 1199
            },
            [BillingCycle.Annual] = new()
            {
                ["NGN"] = 725000, ["GHS"] = 7290, ["USD"] = 699.90m, ["GBP"] = 559.90m, ["KES"] = 62990, ["ZAR"] = 11990
            }
        },
        ["scale"] = new()
        {
            [BillingCycle.Monthly] = new()
            {
                ["NGN"] = 155000, ["GHS"] = 1549, ["USD"] = 149.99m, ["GBP"] = 119.99m, ["KES"] = 13499, ["ZAR"] = 2549
            },
            [BillingCycle.Annual] = new()
            {
                ["NGN"] = 1550000, ["GHS"] = 15490, ["USD"] = 1499.90m, ["GBP"] = 1199.90m, ["KES"] = 134990, ["ZAR"] = 25490
            }
        }
    };

    /// <summary>Returns true if billing in this currency is supported (we have prices and a provider for it).</summary>
    public static bool IsCurrencySupported(string? currency)
        => !string.IsNullOrWhiteSpace(currency)
           && Array.Exists(SupportedCurrencies, c => c.Equals(currency, StringComparison.OrdinalIgnoreCase));

    /// <summary>Get the fixed price for a plan + cycle + currency. Returns null if the combination is invalid.</summary>
    public static decimal? GetPrice(string plan, BillingCycle cycle, string currency)
    {
        if (Prices.TryGetValue(plan.ToLower(), out var cycles)
            && cycles.TryGetValue(cycle, out var currencies)
            && currencies.TryGetValue(currency.ToUpper(), out var price))
            return price;
        return null;
    }

    /// <summary>
    /// Get the price, throwing a clear error when the currency isn't supported. Use this in checkout/billing
    /// paths where silently falling back to a default would be wrong (charging the wrong amount in the wrong
    /// currency). Display paths (UI symbols, formatting) can still use the nullable GetPrice.
    /// </summary>
    public static decimal GetPriceOrThrow(string plan, BillingCycle cycle, string currency)
    {
        var price = GetPrice(plan, cycle, currency);
        if (price.HasValue) return price.Value;
        throw new InvalidOperationException(
            $"No price configured for plan '{plan}' / cycle '{cycle}' / currency '{currency}'. " +
            $"Supported currencies: {string.Join(", ", SupportedCurrencies)}.");
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

    // ── Voice AI add-on pricing ──────────────────────────────────────────────

    private static readonly Dictionary<BillingCycle, Dictionary<string, decimal>> VoiceAIPrices = new()
    {
        [BillingCycle.Monthly] = new()
        {
            ["NGN"] = 5000, ["GHS"] = 45, ["USD"] = 5, ["GBP"] = 4, ["KES"] = 700, ["ZAR"] = 70
        },
        [BillingCycle.Annual] = new()
        {
            ["NGN"] = 48000, ["GHS"] = 432, ["USD"] = 48, ["GBP"] = 38, ["KES"] = 6720, ["ZAR"] = 672
        }
    };

    public static decimal? GetVoiceAIPrice(BillingCycle cycle, string currency)
    {
        if (VoiceAIPrices.TryGetValue(cycle, out var currencies)
            && currencies.TryGetValue(currency.ToUpper(), out var price))
            return price;
        return null;
    }

    public static object GetVoiceAIPricing() => new
    {
        monthly = VoiceAIPrices[BillingCycle.Monthly],
        annual = VoiceAIPrices[BillingCycle.Annual],
        annualDiscount = 20
    };

    public static bool IsValidVoiceAICombination(string cycle, string currency)
    {
        if (!Enum.TryParse<BillingCycle>(cycle, true, out var bc)) return false;
        return GetVoiceAIPrice(bc, currency).HasValue;
    }

    // ── WhatsApp pack pricing ───────────────────────────────────────────────
    // WhatsApp is sold separately from the main tier — these packs cover Meta's per-conversation
    // fees + Sent's platform fee. -1 actions = unlimited. Africa-PPP-adjusted vs strict USD
    // conversion (see pricing brief: USD/GBP full price, ZAR ~10% below, NGN/GHS/KES ~30% below).
    // Annual = monthly × 10 (2 months free).

    public static readonly string[] WhatsAppPackCodes = { "start", "grow", "pro", "scale", "unlimited" };

    /// <summary>Pack code → max actions/mo for the WhatsApp meter. -1 = unlimited.</summary>
    public static readonly Dictionary<string, int> WhatsAppPackActions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["start"] = 100,
        ["grow"] = 300,
        ["pro"] = 800,
        ["scale"] = 2000,
        ["unlimited"] = -1,
    };

    /// <summary>Pack code → human-readable display name (used in UI + invoices).</summary>
    public static readonly Dictionary<string, string> WhatsAppPackLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["start"] = "WhatsApp Start",
        ["grow"] = "WhatsApp Grow",
        ["pro"] = "WhatsApp Pro",
        ["scale"] = "WhatsApp Scale",
        ["unlimited"] = "WhatsApp Unlimited",
    };

    private static readonly Dictionary<string, Dictionary<BillingCycle, Dictionary<string, decimal>>> WhatsAppPackPrices = new()
    {
        ["start"] = new()
        {
            [BillingCycle.Monthly] = new()
            {
                ["NGN"] = 9500, ["GHS"] = 95, ["USD"] = 9, ["GBP"] = 7, ["KES"] = 799, ["ZAR"] = 149
            },
            [BillingCycle.Annual] = new()
            {
                ["NGN"] = 95000, ["GHS"] = 950, ["USD"] = 90, ["GBP"] = 70, ["KES"] = 7990, ["ZAR"] = 1490
            }
        },
        ["grow"] = new()
        {
            [BillingCycle.Monthly] = new()
            {
                ["NGN"] = 19500, ["GHS"] = 199, ["USD"] = 19, ["GBP"] = 15, ["KES"] = 1699, ["ZAR"] = 329
            },
            [BillingCycle.Annual] = new()
            {
                ["NGN"] = 195000, ["GHS"] = 1990, ["USD"] = 190, ["GBP"] = 150, ["KES"] = 16990, ["ZAR"] = 3290
            }
        },
        ["pro"] = new()
        {
            [BillingCycle.Monthly] = new()
            {
                ["NGN"] = 39999, ["GHS"] = 399, ["USD"] = 39, ["GBP"] = 31, ["KES"] = 3499, ["ZAR"] = 649
            },
            [BillingCycle.Annual] = new()
            {
                ["NGN"] = 399990, ["GHS"] = 3990, ["USD"] = 390, ["GBP"] = 310, ["KES"] = 34990, ["ZAR"] = 6490
            }
        },
        ["scale"] = new()
        {
            [BillingCycle.Monthly] = new()
            {
                ["NGN"] = 82000, ["GHS"] = 829, ["USD"] = 79, ["GBP"] = 63, ["KES"] = 7199, ["ZAR"] = 1349
            },
            [BillingCycle.Annual] = new()
            {
                ["NGN"] = 820000, ["GHS"] = 8290, ["USD"] = 790, ["GBP"] = 630, ["KES"] = 71990, ["ZAR"] = 13490
            }
        },
        ["unlimited"] = new()
        {
            [BillingCycle.Monthly] = new()
            {
                ["NGN"] = 155000, ["GHS"] = 1549, ["USD"] = 149, ["GBP"] = 119, ["KES"] = 13499, ["ZAR"] = 2549
            },
            [BillingCycle.Annual] = new()
            {
                ["NGN"] = 1550000, ["GHS"] = 15490, ["USD"] = 1490, ["GBP"] = 1190, ["KES"] = 134990, ["ZAR"] = 25490
            }
        }
    };

    public static decimal? GetWhatsAppPackPrice(string packCode, BillingCycle cycle, string currency)
    {
        if (WhatsAppPackPrices.TryGetValue(packCode.ToLower(), out var cycles)
            && cycles.TryGetValue(cycle, out var currencies)
            && currencies.TryGetValue(currency.ToUpper(), out var price))
            return price;
        return null;
    }

    public static decimal GetWhatsAppPackPriceOrThrow(string packCode, BillingCycle cycle, string currency)
    {
        var price = GetWhatsAppPackPrice(packCode, cycle, currency);
        if (price.HasValue) return price.Value;
        throw new InvalidOperationException(
            $"No price for WhatsApp pack '{packCode}' / {cycle} / {currency}.");
    }

    public static bool IsValidWhatsAppPackCombination(string packCode, string cycle, string currency)
    {
        if (!Enum.TryParse<BillingCycle>(cycle, true, out var bc)) return false;
        return GetWhatsAppPackPrice(packCode, bc, currency).HasValue;
    }

    /// <summary>All pack catalog data for the frontend pack picker.</summary>
    public static object GetAllWhatsAppPackPricing()
    {
        var packs = new Dictionary<string, object>();
        foreach (var code in WhatsAppPackCodes)
        {
            packs[code] = new
            {
                code,
                label = WhatsAppPackLabels[code],
                actions = WhatsAppPackActions[code],
                monthly = WhatsAppPackPrices[code][BillingCycle.Monthly],
                annual = WhatsAppPackPrices[code][BillingCycle.Annual]
            };
        }
        return new { packs, currencies = SupportedCurrencies };
    }
}

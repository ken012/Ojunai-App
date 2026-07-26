namespace Ojunai.API.Common;

/// <summary>
/// Static lookup for country → currency, timezone, and phone prefix → country inference.
/// Used by onboarding (WhatsApp + dashboard registration) to auto-detect a business's locale
/// from their phone number, and by settings to auto-set currency/timezone when country changes.
/// </summary>
public static class CountryLookup
{
    public record CountryInfo(string Name, string Currency, string Timezone, string PhonePrefix);

    private static readonly List<CountryInfo> Countries = new()
    {
        new("Nigeria",       "NGN", "Africa/Lagos",          "+234"),
        new("Ghana",         "GHS", "Africa/Accra",          "+233"),
        new("Kenya",         "KES", "Africa/Nairobi",        "+254"),
        new("South Africa",  "ZAR", "Africa/Johannesburg",   "+27"),
        new("Tanzania",      "TZS", "Africa/Dar_es_Salaam",  "+255"),
        new("Uganda",        "UGX", "Africa/Kampala",        "+256"),
        new("Rwanda",        "RWF", "Africa/Kigali",         "+250"),
        new("Cameroon",      "XAF", "Africa/Douala",         "+237"),
        new("Senegal",       "XOF", "Africa/Dakar",          "+221"),
        new("Ivory Coast",   "XOF", "Africa/Abidjan",        "+225"),
        new("Egypt",         "EGP", "Africa/Cairo",          "+20"),
        new("Ethiopia",      "ETB", "Africa/Addis_Ababa",    "+251"),
        new("DR Congo",      "CDF", "Africa/Kinshasa",       "+243"),
        new("Angola",        "AOA", "Africa/Luanda",         "+244"),
        new("Mozambique",    "MZN", "Africa/Maputo",         "+258"),
        new("Zambia",        "ZMW", "Africa/Lusaka",         "+260"),
        new("Zimbabwe",      "USD", "Africa/Harare",         "+263"),
        new("Botswana",      "BWP", "Africa/Gaborone",       "+267"),
        new("Namibia",       "NAD", "Africa/Windhoek",       "+264"),
        new("Malawi",        "MWK", "Africa/Blantyre",       "+265"),
        new("Benin",         "XOF", "Africa/Porto-Novo",     "+229"),
        new("Togo",          "XOF", "Africa/Lome",           "+228"),
        new("Sierra Leone",  "SLE", "Africa/Freetown",       "+232"),
        new("Liberia",       "LRD", "Africa/Monrovia",       "+231"),
        new("Gambia",        "GMD", "Africa/Banjul",         "+220"),
    };

    /// <summary>All supported country names, sorted alphabetically.</summary>
    public static IReadOnlyList<string> AllCountryNames { get; } =
        Countries.OrderBy(c => c.Name).Select(c => c.Name).ToList();

    /// <summary>All country info records, sorted alphabetically.</summary>
    public static IReadOnlyList<CountryInfo> All { get; } =
        Countries.OrderBy(c => c.Name).ToList();

    /// <summary>
    /// Infer country, currency, and timezone from a phone number's international prefix.
    /// Returns null if the prefix doesn't match any known African country.
    /// Matches longest prefix first so +27 (South Africa) doesn't collide with +2xx patterns.
    /// </summary>
    public static CountryInfo? InferFromPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var normalized = phone.TrimStart();
        if (!normalized.StartsWith("+")) return null;

        // Sort by prefix length descending so +254 matches before +25
        return Countries
            .OrderByDescending(c => c.PhonePrefix.Length)
            .FirstOrDefault(c => normalized.StartsWith(c.PhonePrefix));
    }

    /// <summary>Look up country info by name (case-insensitive).</summary>
    public static CountryInfo? GetByName(string? country)
    {
        if (string.IsNullOrWhiteSpace(country)) return null;
        return Countries.FirstOrDefault(c =>
            c.Name.Equals(country.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Get the default info for businesses with no country set.</summary>
    public static CountryInfo Default => Countries[0]; // Nigeria

    // ── Worldwide billing derivation ────────────────────────────────────────────
    // Country → BILLING currency, derived SERVER-SIDE (never trust a client-chosen currency —
    // that would let a merchant in a pricey market pick a cheaper market's PPP-adjusted prices).
    // Only markets with a supported price table map to their local currency; everyone else bills
    // in USD (the global default). Mirrors dashboard/src/lib/geo.ts.
    // NOTE: the four deep-PPP African currencies (NGN/GHS/KES/UGX) are the ones a future gate
    // restricts to country-matched merchants; ZAR/GBP/CAD/EUR are FX-neutral and stay free.

    // The 20 Eurozone member states (all bill in EUR). Names must match the geo.ts / Countries keys.
    private static readonly string[] EurozoneCountries =
    {
        "Austria", "Belgium", "Croatia", "Cyprus", "Estonia", "Finland", "France",
        "Germany", "Greece", "Ireland", "Italy", "Latvia", "Lithuania", "Luxembourg",
        "Malta", "Netherlands", "Portugal", "Slovakia", "Slovenia", "Spain",
    };

    private static readonly Dictionary<string, string> SupportedBillingCurrency = BuildBillingMap();

    private static Dictionary<string, string> BuildBillingMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Nigeria"] = "NGN", ["Ghana"] = "GHS", ["Kenya"] = "KES",
            ["South Africa"] = "ZAR", ["Uganda"] = "UGX", ["United Kingdom"] = "GBP",
            ["Canada"] = "CAD",
        };
        foreach (var c in EurozoneCountries) map[c] = "EUR";
        return map;
    }

    /// <summary>The supported billing currency for a country, or "USD" for every other market.</summary>
    public static string BillingCurrencyFor(string? country)
        => country != null && SupportedBillingCurrency.TryGetValue(country.Trim(), out var c) ? c : "USD";

    // Reasonable default timezone for the major non-African markets (the African ones come from the
    // Countries table above). Falls back to UTC; the merchant can adjust it in Settings.
    private static readonly Dictionary<string, string> ExtraTimezones =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["United States"] = "America/New_York", ["Canada"] = "America/Toronto",
        ["United Kingdom"] = "Europe/London", ["Ireland"] = "Europe/Dublin",
        ["Australia"] = "Australia/Sydney", ["New Zealand"] = "Pacific/Auckland",
        ["India"] = "Asia/Kolkata", ["Pakistan"] = "Asia/Karachi", ["Bangladesh"] = "Asia/Dhaka",
        ["Germany"] = "Europe/Berlin", ["France"] = "Europe/Paris", ["Spain"] = "Europe/Madrid",
        ["Italy"] = "Europe/Rome", ["Netherlands"] = "Europe/Amsterdam", ["Portugal"] = "Europe/Lisbon",
        ["Brazil"] = "America/Sao_Paulo", ["Mexico"] = "America/Mexico_City",
        ["United Arab Emirates"] = "Asia/Dubai", ["Saudi Arabia"] = "Asia/Riyadh",
        ["Philippines"] = "Asia/Manila", ["Indonesia"] = "Asia/Jakarta", ["Singapore"] = "Asia/Singapore",
    };

    /// <summary>Best-effort default timezone for a country (African markets from the table; a curated
    /// set of major markets; UTC otherwise). Editable in Settings.</summary>
    public static string TimezoneFor(string? country)
    {
        if (GetByName(country) is { } info) return info.Timezone;
        return country != null && ExtraTimezones.TryGetValue(country.Trim(), out var tz) ? tz : "Etc/UTC";
    }
}

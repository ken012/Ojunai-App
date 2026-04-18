namespace BizPilot.API.Common;

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
}

namespace Ojunai.API.Common.PricingV2;

/// <summary>
/// Maps a timezone or IP-region hint to one of the 7 supported billing currencies.
/// Order of preference, per spec:
///   1. Explicit user choice (Settings → Currency) — handled at the call site
///   2. Timezone IANA prefix (Africa/Lagos → NGN, Europe/London → GBP, etc.)
///   3. Country code from IP geolocation (passed in as ISO 3166-1 alpha-2)
///   4. USD fallback
///
/// We deliberately don't auto-detect from <c>Accept-Language</c> headers — those are user-agent
/// language, not commerce currency. A Nigerian phone set to en-US should still see NGN prices.
///
/// PHASE-0: this helper exists; nobody calls it yet. Phase-3 wires it into the registration +
/// first-visit flow on the dashboard.
/// </summary>
public static class CurrencyDetector
{
    /// <summary>
    /// Best-effort timezone-to-currency map. Only entries that match one of the seven supported
    /// currencies; everything else falls through to USD. Conservative coverage: African major
    /// markets + UK + US.
    /// </summary>
    private static readonly Dictionary<string, string> TimezoneToCurrency = new(StringComparer.OrdinalIgnoreCase)
    {
        // Nigeria
        ["Africa/Lagos"] = "NGN",

        // Ghana
        ["Africa/Accra"] = "GHS",

        // Kenya
        ["Africa/Nairobi"] = "KES",

        // South Africa (and SAST-aligned neighbours that pay in ZAR)
        ["Africa/Johannesburg"] = "ZAR",
        ["Africa/Maseru"] = "ZAR",         // Lesotho — uses LSL pegged to ZAR; we bill ZAR
        ["Africa/Mbabane"] = "ZAR",        // Eswatini

        // Uganda
        ["Africa/Kampala"] = "UGX",

        // United Kingdom
        ["Europe/London"] = "GBP",

        // United States — USD
        ["America/New_York"] = "USD",
        ["America/Los_Angeles"] = "USD",
        ["America/Chicago"] = "USD",
        ["America/Denver"] = "USD",
        ["America/Phoenix"] = "USD",
        ["America/Anchorage"] = "USD",
        ["Pacific/Honolulu"] = "USD",
    };

    /// <summary>Country code (ISO 3166-1 alpha-2) → currency, used as a secondary signal when TZ is ambiguous.</summary>
    private static readonly Dictionary<string, string> CountryToCurrency = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NG"] = "NGN", ["GH"] = "GHS", ["KE"] = "KES", ["ZA"] = "ZAR",
        ["UG"] = "UGX", ["GB"] = "GBP", ["UK"] = "GBP", ["US"] = "USD",
    };

    /// <summary>
    /// Resolve a billing currency from the strongest signal available. Returns one of the seven
    /// supported currencies; falls back to <c>"USD"</c> if nothing matches.
    /// </summary>
    public static string Detect(string? timezone, string? countryCode)
    {
        if (!string.IsNullOrWhiteSpace(timezone)
            && TimezoneToCurrency.TryGetValue(timezone, out var fromTz))
            return fromTz;

        if (!string.IsNullOrWhiteSpace(countryCode)
            && CountryToCurrency.TryGetValue(countryCode, out var fromCountry))
            return fromCountry;

        return "USD";
    }
}

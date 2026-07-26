using Ojunai.API.Common;
using Xunit;

namespace Ojunai.Tests.Security;

/// <summary>
/// Tests for the billing-currency gate ("gate the 4, free the rest"). The four deep-PPP currencies
/// (NGN/GHS/KES/UGX, ~31-40% below USD list) may only be billed by a store whose country derives to
/// them; the FX-neutral set (USD/GBP/CAD/EUR/ZAR) is always allowed, with USD as the universal escape
/// hatch. Pure logic — no DB. Mirrors the frontend allowedBillingCurrencies() in
/// dashboard/src/lib/pricing.ts (kept in parity by design). See BillingConfig.IsBillingCurrencyAllowed.
/// </summary>
public class BillingCurrencyGateTests
{
    [Theory]
    // A gated currency is allowed ONLY for its own country.
    [InlineData("Nigeria", "NGN", true)]
    [InlineData("Ghana", "GHS", true)]
    [InlineData("Kenya", "KES", true)]
    [InlineData("Uganda", "UGX", true)]
    // A gated currency is rejected for a non-matching country (the arbitrage block).
    [InlineData("Nigeria", "GHS", false)]
    [InlineData("Nigeria", "KES", false)]
    [InlineData("Nigeria", "UGX", false)]
    [InlineData("Uganda", "NGN", false)]
    [InlineData("Kenya", "NGN", false)]
    [InlineData("United States", "NGN", false)]
    [InlineData("Canada", "NGN", false)]
    [InlineData("Germany", "KES", false)]
    // The FX-neutral currencies are always allowed, regardless of country
    // (diaspora / foreign-card / multi-country; USD is the universal escape hatch).
    [InlineData("Nigeria", "USD", true)]
    [InlineData("Uganda", "USD", true)]
    [InlineData("United States", "USD", true)]
    [InlineData("United States", "GBP", true)]
    [InlineData("United States", "EUR", true)]
    [InlineData("United States", "CAD", true)]
    [InlineData("United States", "ZAR", true)] // ZAR is NOT gated (near-FX, ~5% off)
    [InlineData("Canada", "CAD", true)]
    [InlineData("Germany", "EUR", true)]
    // Case-insensitive on both country and currency.
    [InlineData("nigeria", "ngn", true)]
    [InlineData("Nigeria", "ngn", true)]
    // Unknown country → USD default: a gated currency is rejected, a free one allowed.
    [InlineData("Atlantis", "NGN", false)]
    [InlineData("Atlantis", "USD", true)]
    public void IsBillingCurrencyAllowed_MatchesPolicy(string country, string currency, bool expected)
        => Assert.Equal(expected, BillingConfig.IsBillingCurrencyAllowed(currency, country));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsBillingCurrencyAllowed_BlankCurrency_IsRejected(string? currency)
        => Assert.False(BillingConfig.IsBillingCurrencyAllowed(currency, "Nigeria"));

    [Fact]
    public void NullCountry_GatedCurrency_IsRejected()
        => Assert.False(BillingConfig.IsBillingCurrencyAllowed("NGN", null));

    [Fact]
    public void NullCountry_FreeCurrency_IsAllowed()
        => Assert.True(BillingConfig.IsBillingCurrencyAllowed("USD", null));

    [Fact]
    public void GatedCurrencies_AreExactlyTheFourDeepPpp()
        => Assert.Equal(new[] { "NGN", "GHS", "KES", "UGX" }, BillingConfig.GatedCurrencies);

    [Fact]
    public void Zar_IsNotGated() // ZAR is FX-neutral — gating it would add friction for zero revenue.
        => Assert.DoesNotContain("ZAR", BillingConfig.GatedCurrencies);

    // Country → billing currency derivation (the gate reads this; kept in parity with dashboard geo.ts).
    [Theory]
    [InlineData("Nigeria", "NGN")]
    [InlineData("Ghana", "GHS")]
    [InlineData("Kenya", "KES")]
    [InlineData("Uganda", "UGX")]
    [InlineData("South Africa", "ZAR")]
    [InlineData("United Kingdom", "GBP")]
    [InlineData("Canada", "CAD")]
    [InlineData("Germany", "EUR")]
    [InlineData("France", "EUR")]
    [InlineData("Ireland", "EUR")]
    [InlineData("United States", "USD")]
    [InlineData("Tanzania", "USD")] // non-priced market → USD fallback
    [InlineData("Narnia", "USD")]   // unknown → USD (NOT NGN — matches server default)
    public void BillingCurrencyFor_DerivesExpected(string country, string expected)
        => Assert.Equal(expected, CountryLookup.BillingCurrencyFor(country));

    [Fact]
    public void RejectionMessage_NamesCurrency_AndUsdEscapeHatch()
    {
        var msg = BillingConfig.GatedCurrencyRejectionMessage("KES", "United States");
        Assert.Contains("KES", msg);
        Assert.Contains("USD", msg);
    }

    [Fact]
    public void RejectionMessage_ForGatedHomeMarket_OffersTheirLocalCurrency()
    {
        // A Nigerian picking a DIFFERENT gated currency (GHS) — the message should still offer their own NGN.
        var msg = BillingConfig.GatedCurrencyRejectionMessage("GHS", "Nigeria");
        Assert.Contains("NGN", msg);
    }
}

// Central currency + formatting config for the dashboard.
//
// Tier and WhatsApp-pack PRICES are NOT hardcoded here anymore. They come live from the backend
// (GET /subscription/pricing → BillingConfig.GetAllPricing) via usePricing() in @/lib/use-pricing,
// so the in-app billing UI can never drift from what's actually charged. This file keeps only the
// stable, non-price concerns: the supported-currency list, symbols, formatting, and provider routing.

export type BillingCycle = "monthly" | "annual";
export type SupportedCurrency = "NGN" | "GHS" | "USD" | "GBP" | "KES" | "ZAR" | "UGX" | "CAD" | "EUR";

export const SUPPORTED_CURRENCIES: SupportedCurrency[] = ["NGN", "GHS", "USD", "GBP", "KES", "ZAR", "UGX", "CAD", "EUR"];

export const CURRENCY_META: Record<SupportedCurrency, { symbol: string; label: string }> = {
  NGN: { symbol: "₦", label: "NGN" },
  GHS: { symbol: "GH₵", label: "GHS" },
  USD: { symbol: "$", label: "USD" },
  GBP: { symbol: "£", label: "GBP" },
  KES: { symbol: "KSh", label: "KES" },
  ZAR: { symbol: "R", label: "ZAR" },
  UGX: { symbol: "USh", label: "UGX" },
  CAD: { symbol: "C$", label: "CAD" },
  EUR: { symbol: "€", label: "EUR" },
};

// ─── Helpers ─────────────────────────────────────────────

export function formatPrice(amount: number, currency: SupportedCurrency): string {
  const meta = CURRENCY_META[currency];
  // Whole-number currencies don't need decimals
  if (amount === Math.floor(amount)) return `${meta.symbol}${amount.toLocaleString()}`;
  return `${meta.symbol}${amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

export function getProvider(currency: SupportedCurrency): "paystack" | "flutterwave" | "stripe" {
  if (currency === "NGN") return "paystack";
  if (currency === "USD" || currency === "GBP" || currency === "CAD" || currency === "EUR") return "stripe";
  return "flutterwave";
}

// Currencies we accept on signup but don't bill in directly → mapped to the nearest billing currency.
// (Supported billing currencies are handled first in toBillingCurrency, so they never reach this map.)
const CURRENCY_MAP: Record<string, SupportedCurrency> = {
  TZS: "USD", RWF: "USD", XAF: "USD", XOF: "USD",
  EGP: "USD", ETB: "USD", CDF: "USD", AOA: "USD", MZN: "USD",
  ZMW: "USD", BWP: "ZAR", NAD: "ZAR", MWK: "USD",
  SLE: "USD", LRD: "USD", GMD: "USD",
};

export function toBillingCurrency(currency: string | undefined): SupportedCurrency {
  const c = currency?.toUpperCase() ?? "";
  if ((SUPPORTED_CURRENCIES as readonly string[]).includes(c)) return c as SupportedCurrency;
  return CURRENCY_MAP[c] ?? "NGN";
}

// ── Billing-currency gate ("gate the 4, free the rest") ──────────────────────
// The four deep-PPP-discounted currencies are country-gated; the FX-neutral set stays free.
// Mirror of the server BillingConfig.GatedCurrencies / IsBillingCurrencyAllowed.
export const GATED_CURRENCIES: SupportedCurrency[] = ["NGN", "GHS", "KES", "UGX"];

/**
 * Currencies a merchant may bill in, given their store's country currency (the raw geo code, e.g.
 * "NGN" / "TZS" / "CAD"). The FX-neutral currencies (USD/GBP/CAD/EUR/ZAR) are always available; the
 * four deep-PPP currencies only when the currency is the merchant's own country currency. The
 * merchant's own currency is listed first (the natural default). Unknown/missing country → USD,
 * matching the server's BillingCurrencyFor default.
 */
export function allowedBillingCurrencies(countryCurrency: string | undefined): SupportedCurrency[] {
  const own: SupportedCurrency = countryCurrency ? toBillingCurrency(countryCurrency) : "USD";
  const rest = SUPPORTED_CURRENCIES.filter((c) => c !== own && !GATED_CURRENCIES.includes(c));
  return [own, ...rest];
}

export function getDefaultCurrency(): SupportedCurrency {
  if (typeof window === "undefined") return "NGN";
  try {
    const raw = localStorage.getItem("oj_business");
    if (raw) {
      const biz = JSON.parse(raw);
      // Prefer the billing currency (what's charged); fall back to display currency for merchants
      // who've never checked out. Keeps the picker's initial seed consistent with the settings useEffect.
      return toBillingCurrency(biz.billingCurrency ?? biz.currency);
    }
  } catch { /* ignore */ }
  return "NGN";
}

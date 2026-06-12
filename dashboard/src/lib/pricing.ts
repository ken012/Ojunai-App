// Central currency + formatting config for the dashboard.
//
// Tier and WhatsApp-pack PRICES are NOT hardcoded here anymore. They come live from the backend
// (GET /subscription/pricing → BillingConfig.GetAllPricing) via usePricing() in @/lib/use-pricing,
// so the in-app billing UI can never drift from what's actually charged. This file keeps only the
// stable, non-price concerns: the supported-currency list, symbols, formatting, and provider routing.

export type BillingCycle = "monthly" | "annual";
export type SupportedCurrency = "NGN" | "GHS" | "USD" | "GBP" | "KES" | "ZAR" | "UGX";

export const SUPPORTED_CURRENCIES: SupportedCurrency[] = ["NGN", "GHS", "USD", "GBP", "KES", "ZAR", "UGX"];

export const CURRENCY_META: Record<SupportedCurrency, { symbol: string; label: string }> = {
  NGN: { symbol: "₦", label: "NGN" },
  GHS: { symbol: "GH₵", label: "GHS" },
  USD: { symbol: "$", label: "USD" },
  GBP: { symbol: "£", label: "GBP" },
  KES: { symbol: "KSh", label: "KES" },
  ZAR: { symbol: "R", label: "ZAR" },
  UGX: { symbol: "USh", label: "UGX" },
};

// ─── Helpers ─────────────────────────────────────────────

export function formatPrice(amount: number, currency: SupportedCurrency): string {
  const meta = CURRENCY_META[currency];
  // Whole-number currencies don't need decimals
  if (amount === Math.floor(amount)) return `${meta.symbol}${amount.toLocaleString()}`;
  return `${meta.symbol}${amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

export function getProvider(currency: SupportedCurrency): "paystack" | "flutterwave" {
  return currency === "NGN" ? "paystack" : "flutterwave";
}

// Currencies we accept on signup but don't bill in directly → mapped to the nearest billing currency.
// (Supported billing currencies are handled first in toBillingCurrency, so they never reach this map.)
const CURRENCY_MAP: Record<string, SupportedCurrency> = {
  TZS: "USD", RWF: "USD", XAF: "USD", XOF: "USD",
  EGP: "USD", ETB: "USD", CDF: "USD", AOA: "USD", MZN: "USD",
  ZMW: "USD", BWP: "ZAR", NAD: "ZAR", MWK: "USD",
  SLE: "USD", LRD: "USD", GMD: "USD", EUR: "USD", CAD: "USD",
};

export function toBillingCurrency(currency: string | undefined): SupportedCurrency {
  const c = currency?.toUpperCase() ?? "";
  if ((SUPPORTED_CURRENCIES as readonly string[]).includes(c)) return c as SupportedCurrency;
  return CURRENCY_MAP[c] ?? "NGN";
}

export function getDefaultCurrency(): SupportedCurrency {
  if (typeof window === "undefined") return "NGN";
  try {
    const raw = localStorage.getItem("oj_business");
    if (raw) {
      const biz = JSON.parse(raw);
      return toBillingCurrency(biz.currency);
    }
  } catch { /* ignore */ }
  return "NGN";
}

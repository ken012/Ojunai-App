// Central pricing config — mirrors BillingConfig.cs on the backend.
// Fixed localized prices. No FX conversion. Selected currency drives provider routing.

export type BillingCycle = "monthly" | "annual";
export type SupportedCurrency = "NGN" | "GHS" | "USD" | "GBP" | "KES" | "ZAR";

export const SUPPORTED_CURRENCIES: SupportedCurrency[] = ["NGN", "GHS", "USD", "GBP", "KES", "ZAR"];

export const CURRENCY_META: Record<SupportedCurrency, { symbol: string; label: string }> = {
  NGN: { symbol: "₦", label: "NGN" },
  GHS: { symbol: "GH₵", label: "GHS" },
  USD: { symbol: "$", label: "USD" },
  GBP: { symbol: "£", label: "GBP" },
  KES: { symbol: "KSh", label: "KES" },
  ZAR: { symbol: "R", label: "ZAR" },
};

type PlanPrices = Record<SupportedCurrency, number>;

interface PlanPricing {
  monthly: PlanPrices;
  annual: PlanPrices;
  annualDiscount: number;
  features: string[];
  tagline: string;
  badge?: string;
  popular?: boolean;
}

export const PRICING: Record<string, PlanPricing> = {
  starter: {
    monthly: { NGN: 3500, GHS: 32, USD: 3.49, GBP: 2.99, KES: 480, ZAR: 50 },
    annual: { NGN: 37800, GHS: 346, USD: 37.69, GBP: 32.29, KES: 5184, ZAR: 540 },
    annualDiscount: 10,
    tagline: "Best for solo traders just starting out",
    features: [
      "WhatsApp bot access",
      "Up to 30 products",
      "150 messages / month",
      "Daily summaries",
      "Basic web dashboard",
      "Ledger & debt tracking",
    ],
    badge: "Save 10%",
  },
  shop: {
    monthly: { NGN: 7500, GHS: 65, USD: 6.99, GBP: 5.99, KES: 1000, ZAR: 95 },
    annual: { NGN: 76500, GHS: 663, USD: 71.30, GBP: 61.10, KES: 10200, ZAR: 969 },
    annualDiscount: 15,
    tagline: "For growing shops with staff",
    features: [
      "Everything in Starter",
      "Unlimited products",
      "850 messages / month",
      "Stock holds",
      "Up to 4 users",
      "CSV import",
    ],
    badge: "Save 15%",
  },
  pro: {
    monthly: { NGN: 12500, GHS: 115, USD: 11.99, GBP: 9.99, KES: 1650, ZAR: 160 },
    annual: { NGN: 124500, GHS: 1145, USD: 119.42, GBP: 99.50, KES: 16434, ZAR: 1594 },
    annualDiscount: 17,
    tagline: "Full power for serious businesses",
    features: [
      "Everything in Shop",
      "Unlimited messages",
      "Advanced reports & charts",
      "Up to 11 users",
    ],
    badge: "2 months free",
    popular: true,
  },
  business: {
    monthly: { NGN: 30000, GHS: 270, USD: 24.99, GBP: 19.99, KES: 3900, ZAR: 380 },
    annual: { NGN: 288000, GHS: 2592, USD: 239.90, GBP: 191.90, KES: 37440, ZAR: 3648 },
    annualDiscount: 20,
    tagline: "Enterprise-grade for multi-location businesses",
    features: [
      "Everything in Pro",
      "Unlimited staff",
      "Multi-branch support",
      "API access & custom exports",
    ],
    badge: "Best value • Save 20%",
  },
};

export const PLAN_ORDER = ["starter", "shop", "pro", "business"] as const;

export const PLAN_LABELS: Record<string, string> = {
  starter: "Starter",
  shop: "Shop",
  pro: "Pro",
  business: "Business",
};

// ─── Helpers ─────────────────────────────────────────────────

export function getPrice(plan: string, cycle: BillingCycle, currency: SupportedCurrency): number {
  return PRICING[plan]?.[cycle]?.[currency] ?? 0;
}

export function getMonthlyEquivalent(plan: string, currency: SupportedCurrency): number {
  const annual = getPrice(plan, "annual", currency);
  return Math.round((annual / 12) * 100) / 100;
}

export function formatPrice(amount: number, currency: SupportedCurrency): string {
  const meta = CURRENCY_META[currency];
  // Whole-number currencies don't need decimals
  if (amount === Math.floor(amount)) return `${meta.symbol}${amount.toLocaleString()}`;
  return `${meta.symbol}${amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

export function getProvider(currency: SupportedCurrency): "paystack" | "flutterwave" {
  return currency === "NGN" ? "paystack" : "flutterwave";
}

const CURRENCY_MAP: Record<string, SupportedCurrency> = {
  TZS: "USD", UGX: "USD", RWF: "USD", XAF: "USD", XOF: "USD",
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
    const raw = localStorage.getItem("bp_business");
    if (raw) {
      const biz = JSON.parse(raw);
      return toBillingCurrency(biz.currency);
    }
  } catch { /* ignore */ }
  return "NGN";
}

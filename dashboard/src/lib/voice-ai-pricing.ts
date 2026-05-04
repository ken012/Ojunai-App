import type { SupportedCurrency, BillingCycle } from "./pricing";

export const VOICE_AI_PRICING: Record<BillingCycle, Record<SupportedCurrency, number>> = {
  monthly: { NGN: 5000, GHS: 45, USD: 5, GBP: 4, KES: 700, ZAR: 70 },
  annual: { NGN: 48000, GHS: 432, USD: 48, GBP: 38, KES: 6720, ZAR: 672 },
};

export const VOICE_AI_ANNUAL_DISCOUNT = 20;

export function getVoiceAIPrice(cycle: BillingCycle, currency: SupportedCurrency): number {
  return VOICE_AI_PRICING[cycle]?.[currency] ?? 0;
}

export const VOICE_AI_FEATURES = [
  "AI-powered phone receptionist",
  "Handles stock checks, reservations & bookings",
  "Multi-language support (English, Yoruba, Hausa, Igbo)",
  "24/7 availability — never miss a customer call",
  "Call logs and interaction history",
  "Seamless Ojunai inventory integration",
];

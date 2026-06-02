import type { SupportedCurrency, BillingCycle } from "./pricing";

export type VoiceAITier = "starter" | "pro";

export const VOICE_AI_TIER_CODES: VoiceAITier[] = ["starter", "pro"];

export const VOICE_AI_TIER_LABELS: Record<VoiceAITier, string> = {
  starter: "Voice Starter",
  pro: "Voice Pro",
};

export const VOICE_AI_TIER_MINUTES: Record<VoiceAITier, number> = {
  starter: 300,
  pro: 1000,
};

export const VOICE_AI_TIER_CONCURRENT_LINES: Record<VoiceAITier, number> = {
  starter: 1,
  pro: 3,
};

export const VOICE_AI_TRIAL_MINUTES = 10;

export const VOICE_AI_ANNUAL_DISCOUNT = 17;

export const VOICE_AI_TIER_PRICING: Record<VoiceAITier, Record<BillingCycle, Record<SupportedCurrency, number>>> = {
  starter: {
    monthly: { NGN: 39999, GHS: 399, USD: 39, GBP: 31, KES: 3499, ZAR: 649, UGX: 89000 },
    annual: { NGN: 399990, GHS: 3990, USD: 390, GBP: 310, KES: 34990, ZAR: 6490, UGX: 890000 },
  },
  pro: {
    monthly: { NGN: 82000, GHS: 829, USD: 79, GBP: 63, KES: 7199, ZAR: 1349, UGX: 180000 },
    annual: { NGN: 820000, GHS: 8290, USD: 790, GBP: 630, KES: 71990, ZAR: 13490, UGX: 1800000 },
  },
};

export function getVoiceAITierPrice(tier: VoiceAITier, cycle: BillingCycle, currency: SupportedCurrency): number {
  return VOICE_AI_TIER_PRICING[tier]?.[cycle]?.[currency] ?? 0;
}

export const VOICE_AI_TIER_FEATURES: Record<VoiceAITier, string[]> = {
  starter: [
    "300 inbound minutes / mo",
    "1 concurrent line",
    "Dedicated phone number",
    "4 languages: English, Yoruba, Hausa, Igbo",
    "Live dashboard sync",
    "Owner handoff for complex calls",
  ],
  pro: [
    "1,000 inbound minutes / mo",
    "3 concurrent lines",
    "Dedicated phone number",
    "All 4 languages",
    "Live dashboard sync",
    "Priority call queueing + handoff",
    "Weekly call-volume report",
  ],
};

export const VOICE_AI_TIER_TAGLINES: Record<VoiceAITier, string> = {
  starter: "For solo operators answering their own line.",
  pro: "For shops with overlapping callers and busy days.",
};

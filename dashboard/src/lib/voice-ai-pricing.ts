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

// Voice tier PRICES are no longer hardcoded here — they come live from the backend
// (GET /subscription/voice-ai-pricing) via useVoicePricing() in @/lib/use-pricing. This file
// keeps only the frontend-only marketing metadata (labels, minutes, features, taglines).

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

"use client";

import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api";
import type { BillingCycle, SupportedCurrency } from "@/lib/pricing";

/**
 * Live pricing catalog from the backend — the single source of truth.
 * Shape mirrors BillingConfig.GetAllPricing(): plans[tier][cycle][currency] = amount.
 */
export interface PricingCatalog {
  plans: Record<
    string,
    {
      monthly: Record<string, number>;
      annual: Record<string, number>;
      annualDiscount: number;
    }
  >;
  currencies: string[];
}

/**
 * Fetches the live pricing catalog (GET /subscription/pricing → BillingConfig) and returns
 * accessors that read straight from it. The dashboard no longer hardcodes tier prices, so the
 * displayed price can never disagree with what checkout charges. Accessors return 0 / [] while
 * the (cached, rarely-changing) catalog is loading.
 */
export function usePricing() {
  const { data, isLoading } = useQuery<PricingCatalog>({
    queryKey: ["pricing-catalog"],
    queryFn: async () => {
      // This endpoint returns the raw catalog (not wrapped in { data }), but handle both shapes.
      const res = await api.get<PricingCatalog | { data: PricingCatalog }>("/subscription/pricing");
      const body = res.data as PricingCatalog | { data: PricingCatalog };
      return "plans" in body ? body : (body as { data: PricingCatalog }).data;
    },
    staleTime: 5 * 60_000, // prices change rarely; cache aggressively
  });

  function getPrice(plan: string, cycle: BillingCycle, currency: SupportedCurrency): number {
    return data?.plans?.[plan]?.[cycle]?.[currency] ?? 0;
  }

  function getMonthlyEquivalent(plan: string, currency: SupportedCurrency): number {
    const annual = data?.plans?.[plan]?.annual?.[currency] ?? 0;
    return Math.round((annual / 12) * 100) / 100;
  }

  function getAnnualDiscount(plan: string): number {
    return data?.plans?.[plan]?.annualDiscount ?? 0;
  }

  return { catalog: data, isLoading, getPrice, getMonthlyEquivalent, getAnnualDiscount };
}

/** Live OjunaiVoice tier pricing (GET /subscription/voice-ai-pricing → BillingConfig.GetVoiceAIPricing). */
export interface VoicePricingCatalog {
  tiers: Array<{
    code: string;
    label: string;
    minutesIncluded: number;
    concurrentLines: number;
    monthly: Record<string, number>;
    annual: Record<string, number>;
  }>;
  trialMinutes: number;
  annualDiscount: number;
  currencies: string[];
}

/**
 * Fetches live Voice tier prices from the backend — single source of truth, same as usePricing().
 * Marketing copy (features/taglines) stays in the frontend; only the numbers come from here.
 */
export function useVoicePricing() {
  const { data, isLoading } = useQuery<VoicePricingCatalog>({
    queryKey: ["voice-pricing-catalog"],
    queryFn: async () => {
      const res = await api.get<VoicePricingCatalog | { data: VoicePricingCatalog }>("/subscription/voice-ai-pricing");
      const body = res.data as VoicePricingCatalog | { data: VoicePricingCatalog };
      return "tiers" in body ? body : (body as { data: VoicePricingCatalog }).data;
    },
    staleTime: 5 * 60_000,
  });

  function getVoiceTierPrice(tier: string, cycle: BillingCycle, currency: SupportedCurrency): number {
    const t = data?.tiers?.find((x) => x.code === tier);
    return t?.[cycle]?.[currency] ?? 0;
  }

  return { catalog: data, isLoading, getVoiceTierPrice };
}

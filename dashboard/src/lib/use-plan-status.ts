"use client";

import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api";

export type PlanStatus = {
  plan: string;
  subscribedPlan: string | null;
  isSubscriber: boolean;
  trialStatus: string;
  trialDaysLeft: number | null;
  trialEndsAt: string | null;
  pricePerMonth: number;
  maxProducts: number;
  maxMessages: number;
  maxStaff: number;
  hasLedger: boolean;
  hasCsvImport: boolean;
  hasAdvancedReports: boolean;
  hasMonthlyCharts: boolean;
  hasStockHolds: boolean;
  isBillable: boolean;
  hasActiveSubscription: boolean;
  subscriptionEndsAt: string | null;
  isAutoRenew: boolean;
  paymentMethod: string | null;
  subscriptionStatus: string;
  pendingPlanChange: string | null;
};

export function usePlanStatus() {
  return useQuery({
    queryKey: ["plan-status"],
    queryFn: async () => {
      const { data } = await api.get<{ data: PlanStatus }>("/business/plan-status");
      return data.data!;
    },
    staleTime: 60 * 1000,
  });
}

export const FEATURE_UPGRADE_MAP: Record<string, { plan: string; label: string }> = {
  ledger: { plan: "Shop", label: "Ledger (credits & debts)" },
  csvImport: { plan: "Pro", label: "CSV Import" },
  advancedReports: { plan: "Pro", label: "Advanced Reports" },
  monthlyCharts: { plan: "Pro", label: "Insights & Charts" },
  stockHolds: { plan: "Shop", label: "Stock Holds" },
};

"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { MessageSquare, Check, Sparkles } from "lucide-react";
import { api } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { useToast } from "@/components/toast";
import { useBusiness } from "@/lib/data-sync";
import { toBillingCurrency, formatPrice } from "@/lib/pricing";

/**
 * WhatsAppPackPicker — surfaces the 5 WhatsApp pack options + the business's currently active
 * pack. Activation goes through POST /subscription/whatsapp-packs/activate, which is currently
 * an interim no-payment endpoint (Phase 2.5 will replace it with Paystack/Flutterwave checkout).
 *
 * Sits inside Settings → Plan & Billing, below the existing tier picker. Renders nothing while
 * loading; the parent quota meter already gives visible loading feedback.
 */

type PackCatalog = {
  packs: Record<
    string,
    {
      code: string;
      label: string;
      actions: number;
      monthly: Record<string, number>;
      annual: Record<string, number>;
    }
  >;
  currencies: string[];
};

type ActivePack = {
  code: string;
  billedAmount: number;
  billedCurrency: string;
  nextBillingAtUtc: string | null;
  addedAtUtc: string;
} | null;

type WhatsAppPacksResponse = {
  catalog: PackCatalog;
  activePack: ActivePack;
};

const PACK_ORDER = ["start", "grow", "pro", "scale", "unlimited"];

export function WhatsAppPackPicker() {
  const business = useBusiness();
  const qc = useQueryClient();
  const { toast } = useToast();
  const [activating, setActivating] = useState<string | null>(null);

  const { data, isLoading } = useQuery<WhatsAppPacksResponse>({
    queryKey: ["whatsapp-packs"],
    queryFn: async () => {
      const res = await api.get<WhatsAppPacksResponse | { data: WhatsAppPacksResponse }>(
        "/subscription/whatsapp-packs",
      );
      const body = res.data as WhatsAppPacksResponse | { data: WhatsAppPacksResponse };
      return "catalog" in body ? body : (body as { data: WhatsAppPacksResponse }).data;
    },
    staleTime: 30_000,
  });

  async function handleActivate(code: string) {
    setActivating(code);
    try {
      await api.post("/subscription/whatsapp-packs/activate", { code });
      const label = data?.catalog.packs[code]?.label ?? "WhatsApp pack";
      toast.success(`${label} activated`, "Your WhatsApp quota updates next refresh.");
      qc.invalidateQueries({ queryKey: ["whatsapp-packs"] });
      qc.invalidateQueries({ queryKey: ["subscription-quota"] });
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      toast.error(
        "Couldn't activate pack",
        ax.response?.data?.errors?.[0] ?? "Try again or contact support.",
      );
    } finally {
      setActivating(null);
    }
  }

  if (isLoading || !data) return null;

  const currency = toBillingCurrency(business?.currency ?? "USD");
  const activeCode = data.activePack?.code ?? null;

  return (
    <div className="rounded-lg border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 p-5">
      <div className="flex items-center justify-between mb-3">
        <div>
          <h3 className="font-semibold text-sm text-slate-900 dark:text-slate-100 flex items-center gap-1.5">
            <MessageSquare size={14} className="text-emerald-600 dark:text-emerald-400" />
            WhatsApp pack
          </h3>
          <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
            WhatsApp is billed separately from your plan. Pick the pack that matches your monthly volume.
          </p>
        </div>
        {activeCode && (
          <span className="text-[11px] px-2 py-0.5 rounded bg-emerald-100 dark:bg-emerald-900/40 text-emerald-700 dark:text-emerald-300">
            Current: {data.catalog.packs[activeCode]?.label}
          </span>
        )}
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-2 mt-4">
        {PACK_ORDER.map((code) => {
          const pack = data.catalog.packs[code];
          if (!pack) return null;
          const isActive = code === activeCode;
          const priceInCurrency = pack.monthly[currency] ?? pack.monthly.USD;
          const actionsLabel =
            pack.actions === -1 ? "Unlimited" : `${pack.actions.toLocaleString()}/mo`;

          return (
            <div
              key={code}
              className={`rounded-md border p-3 transition-colors ${
                isActive
                  ? "border-emerald-400 bg-emerald-50 dark:border-emerald-600 dark:bg-emerald-900/20"
                  : "border-slate-200 dark:border-slate-800 hover:border-slate-300 dark:hover:border-slate-700"
              }`}
            >
              <div className="flex items-start justify-between">
                <div>
                  <p className="text-sm font-medium text-slate-900 dark:text-slate-100">
                    {pack.label.replace("WhatsApp ", "")}
                    {code === "unlimited" && (
                      <Sparkles size={12} className="inline ml-1 text-amber-500" />
                    )}
                  </p>
                  <p className="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5">
                    {actionsLabel}
                  </p>
                </div>
                {isActive && (
                  <Check size={14} className="text-emerald-600 dark:text-emerald-400" />
                )}
              </div>
              <p className="text-lg font-semibold mt-2 text-slate-900 dark:text-slate-100">
                {formatPrice(priceInCurrency, currency)}
                <span className="text-[11px] font-normal text-slate-400 ml-1">/mo</span>
              </p>
              {isActive ? (
                <Button variant="outline" size="sm" disabled className="w-full mt-2 text-[11px] h-7">
                  Active
                </Button>
              ) : (
                <Button
                  size="sm"
                  variant="outline"
                  disabled={activating === code}
                  onClick={() => handleActivate(code)}
                  className="w-full mt-2 text-[11px] h-7"
                >
                  {activating === code ? "Activating…" : activeCode ? "Switch" : "Activate"}
                </Button>
              )}
            </div>
          );
        })}
      </div>

      <p className="text-[10px] text-slate-400 dark:text-slate-500 mt-3 italic">
        Beta: pack activation is currently free for testing. Real billing via Paystack / Flutterwave coming next.
      </p>
    </div>
  );
}

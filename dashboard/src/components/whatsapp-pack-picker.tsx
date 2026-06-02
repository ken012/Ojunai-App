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
 * pack. Real purchase via POST /subscription/whatsapp-packs/purchase, which routes to Paystack
 * (NGN) or Flutterwave (everything else). One-time charges, not auto-renewing subscriptions.
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
  isAutoRenew: boolean;
} | null;

type WhatsAppPacksResponse = {
  catalog: PackCatalog;
  activePack: ActivePack;
};

type PurchaseInitResult = {
  provider: "paystack" | "flutterwave";
  paymentUrl?: string;
  publicKey?: string;
  txRef?: string;
  amount?: number;
  currency?: string;
  email?: string;
  packCode?: string;
  billingCycle?: string;
  callbackUrl?: string;
  businessId?: string;
  businessName?: string;
};

const PACK_ORDER = ["start", "grow", "pro", "scale", "unlimited"];

/**
 * Lazily loads Flutterwave's checkout script. Idempotent — repeat calls resolve immediately
 * if the script is already on the page.
 */
function loadFlutterwaveScript(): Promise<void> {
  return new Promise((resolve, reject) => {
    if (document.querySelector('script[src*="checkout.flutterwave.com"]')) {
      resolve();
      return;
    }
    const script = document.createElement("script");
    script.src = "https://checkout.flutterwave.com/v3.js";
    script.onload = () => resolve();
    script.onerror = () => reject(new Error("Failed to load Flutterwave SDK"));
    document.head.appendChild(script);
  });
}

export function WhatsAppPackPicker() {
  const business = useBusiness();
  const qc = useQueryClient();
  const { toast } = useToast();
  const [purchasing, setPurchasing] = useState<string | null>(null);
  // Two-step purchase: clicking Buy opens a confirm dialog so the user can choose
  // one-time vs auto-renew before being sent to the payment gateway.
  const [confirming, setConfirming] = useState<{ code: string; autoRenew: boolean } | null>(null);

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

  async function handlePurchase(code: string, autoRenew: boolean) {
    setPurchasing(code);
    setConfirming(null);
    try {
      const { data: resp } = await api.post<{ data: PurchaseInitResult }>(
        "/subscription/whatsapp-packs/purchase",
        { code, autoRenew },
      );
      const result = resp.data!;

      if (result.provider === "paystack") {
        // Paystack — full-page redirect to their hosted checkout
        if (!result.paymentUrl) {
          throw new Error("No paymentUrl returned for Paystack purchase");
        }
        window.location.href = result.paymentUrl;
        return;
      }

      // Flutterwave — inline modal
      await loadFlutterwaveScript();
      const win = window as unknown as {
        FlutterwaveCheckout?: (config: Record<string, unknown>) => void;
      };
      if (!win.FlutterwaveCheckout) {
        toast.error("Payment widget failed to load", "Refresh and try again.");
        setPurchasing(null);
        return;
      }

      win.FlutterwaveCheckout({
        public_key: result.publicKey,
        tx_ref: result.txRef,
        amount: result.amount,
        currency: result.currency,
        redirect_url: result.callbackUrl,
        customer: { email: result.email },
        meta: {
          businessId: result.businessId,
          packCode: result.packCode,
          billingCycle: result.billingCycle,
          currency: result.currency,
        },
        customizations: {
          title: "Ojunai",
          description: `${data?.catalog.packs[code]?.label ?? "WhatsApp Pack"} — ${result.billingCycle}`,
          logo: "https://app.ojunai.com/favicon.ico",
        },
        // Packs are one-time charges so we don't need a payment_plan. All payment_options open.
        payment_options: "card,mobilemoney,banktransfer,ussd",
        callback: async (response: { transaction_id?: string; tx_ref?: string }) => {
          try {
            await api.post("/subscription/verify-flutterwave", {
              transactionId: response.transaction_id?.toString(),
              txRef: response.tx_ref,
            });
            toast.success("WhatsApp pack activated", "Your WhatsApp quota updates next refresh.");
            qc.invalidateQueries({ queryKey: ["whatsapp-packs"] });
            qc.invalidateQueries({ queryKey: ["subscription-quota"] });
          } catch (err: unknown) {
            const ax = err as { response?: { data?: { errors?: string[] } } };
            toast.error(
              "Payment received, verification pending",
              ax.response?.data?.errors?.[0] ?? "We'll activate your pack as soon as Flutterwave confirms.",
            );
          } finally {
            setPurchasing(null);
          }
        },
        onclose: () => setPurchasing(null),
      });
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      toast.error(
        "Couldn't start purchase",
        ax.response?.data?.errors?.[0] ?? "Try again or contact support.",
      );
      setPurchasing(null);
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
          <div className="text-right">
            <span className="text-[11px] px-2 py-0.5 rounded bg-emerald-100 dark:bg-emerald-900/40 text-emerald-700 dark:text-emerald-300">
              Current: {data.catalog.packs[activeCode]?.label}
            </span>
            {data.activePack?.nextBillingAtUtc && (
              <p className="text-[10px] text-slate-500 dark:text-slate-400 mt-1">
                {data.activePack.isAutoRenew ? "Auto-renews on " : "Expires on "}
                {new Date(data.activePack.nextBillingAtUtc).toLocaleDateString(undefined, {
                  month: "short",
                  day: "numeric",
                  year: "numeric",
                })}
              </p>
            )}
          </div>
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
                  disabled={purchasing === code}
                  onClick={() => setConfirming({ code, autoRenew: false })}
                  className="w-full mt-2 text-[11px] h-7"
                >
                  {purchasing === code ? "Starting…" : activeCode ? "Switch" : "Buy"}
                </Button>
              )}
            </div>
          );
        })}
      </div>

      {/* Confirm dialog — opens before checkout so the user can pick one-time vs auto-renew. */}
      {confirming && (() => {
        const pack = data.catalog.packs[confirming.code];
        if (!pack) return null;
        const price = pack.monthly[currency] ?? pack.monthly.USD;
        const isNgn = currency === "NGN";
        return (
          <div
            className="fixed inset-0 z-[9998] bg-slate-900/40 flex items-center justify-center p-4"
            onClick={() => setConfirming(null)}
          >
            <div
              className="relative z-[9999] w-full max-w-md rounded-xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 shadow-xl p-5"
              onClick={(e) => e.stopPropagation()}
            >
              <h3 className="text-base font-semibold text-slate-900 dark:text-slate-100 mb-1">
                Confirm WhatsApp pack purchase
              </h3>
              <p className="text-xs text-slate-500 dark:text-slate-400 mb-4">
                {pack.label} — {formatPrice(price, currency)}/mo
                {pack.actions === -1 ? " · Unlimited actions" : ` · ${pack.actions.toLocaleString()} actions/mo`}
              </p>

              <label className="flex items-start gap-2.5 p-3 rounded-lg border border-slate-200 dark:border-slate-800 cursor-pointer hover:bg-slate-50 dark:hover:bg-slate-800/30 transition-colors">
                <input
                  type="checkbox"
                  checked={confirming.autoRenew}
                  disabled={!isNgn}
                  onChange={(e) =>
                    setConfirming({ ...confirming, autoRenew: e.target.checked })
                  }
                  className="mt-0.5"
                />
                <div className="flex-1">
                  <p className="text-sm font-medium text-slate-900 dark:text-slate-100">
                    Renew automatically each month
                  </p>
                  <p className="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5">
                    {isNgn
                      ? "Charges your card each cycle. Cancel anytime from this page."
                      : "Auto-renew is currently available for NGN payments only. Your purchase will be one-time."}
                  </p>
                </div>
              </label>

              <div className="flex justify-end gap-2 mt-5">
                <Button variant="outline" size="sm" onClick={() => setConfirming(null)}>
                  Cancel
                </Button>
                <Button
                  size="sm"
                  onClick={() => handlePurchase(confirming.code, confirming.autoRenew && isNgn)}
                >
                  Continue to checkout
                </Button>
              </div>
            </div>
          </div>
        );
      })()}
    </div>
  );
}

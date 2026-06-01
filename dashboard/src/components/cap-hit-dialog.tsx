"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { AlertTriangle, Sparkles } from "lucide-react";
import { useQuotaSnapshot } from "@/components/quota-meter";

/**
 * Modal that fires automatically when ANY channel meter hits 100% of its cap. Different copy + CTAs
 * per channel:
 *
 *   - WhatsApp at cap → "buy/upgrade your WhatsApp pack" — no tier upgrade implied
 *   - Telegram + Messenger at cap → "upgrade your plan" — that's where the T+M pool lives
 *
 * Triggers once per session per channel (sessionStorage flag) so a merchant who dismisses doesn't
 * get re-popped on every page refresh. Cleared when the period rolls over (new month).
 */
export function CapHitDialog() {
  const { data } = useQuotaSnapshot();
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [trigger, setTrigger] = useState<"whatsapp" | "messaging" | null>(null);

  useEffect(() => {
    if (!data) return;
    const period = data.periodStartUtc;

    // Telegram + Messenger pool — tier upgrade path
    if (data.messaging.percentUsed >= 100 && !data.messaging.isUnlimited) {
      const key = `cap-hit-messaging-${period}`;
      if (typeof window !== "undefined" && !sessionStorage.getItem(key)) {
        setTrigger("messaging");
        setOpen(true);
        return;
      }
    }

    // WhatsApp pack — pack upgrade path
    if (data.whatsApp.percentUsed >= 100 && !data.whatsApp.isUnlimited) {
      const key = `cap-hit-whatsapp-${period}`;
      if (typeof window !== "undefined" && !sessionStorage.getItem(key)) {
        setTrigger("whatsapp");
        setOpen(true);
      }
    }
  }, [data]);

  function dismiss() {
    if (data && trigger && typeof window !== "undefined") {
      sessionStorage.setItem(`cap-hit-${trigger}-${data.periodStartUtc}`, "1");
    }
    setOpen(false);
    setTrigger(null);
  }

  function goToPlan() {
    dismiss();
    router.push("/settings#plan");
  }

  if (!open || !data || !trigger) return null;

  const isWhatsApp = trigger === "whatsapp";
  const channel = isWhatsApp ? data.whatsApp : data.messaging;

  const title = isWhatsApp
    ? "You've used all your WhatsApp actions this month"
    : "You've hit your Telegram + Messenger limit";

  const body = isWhatsApp ? (
    <>
      <p className="text-sm text-slate-600 dark:text-slate-300">
        Your current WhatsApp pack covers <strong>{channel.cap.toLocaleString()}</strong> actions per
        month, and you&apos;ve used them all. Pick a bigger pack to keep messaging on WhatsApp this month —
        Telegram and Messenger are still working.
      </p>
      <ul className="text-xs text-slate-500 dark:text-slate-400 mt-3 space-y-1 list-disc pl-4">
        <li>Grow ($19/mo) — 300 actions</li>
        <li>Pro ($39/mo) — 800 actions</li>
        <li>Scale ($79/mo) — 2,000 actions</li>
        <li>Unlimited ($149/mo) — no cap</li>
      </ul>
    </>
  ) : (
    <>
      <p className="text-sm text-slate-600 dark:text-slate-300">
        You&apos;ve used all <strong>{channel.cap.toLocaleString()}</strong> of your Telegram + Messenger
        actions for the month. Upgrade your plan for a bigger pool — or get unlimited on Scale.
      </p>
      <ul className="text-xs text-slate-500 dark:text-slate-400 mt-3 space-y-1 list-disc pl-4">
        <li>Lite — 500/mo</li>
        <li>Operator — 1,500/mo</li>
        <li>Pro — 4,000/mo</li>
        <li>Scale — Unlimited</li>
      </ul>
    </>
  );

  const primaryLabel = isWhatsApp ? "Pick a WhatsApp pack" : "Upgrade plan";

  return (
    <Dialog open={open} onOpenChange={(o) => !o && dismiss()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>
            <span className="flex items-center gap-2">
              {isWhatsApp ? (
                <AlertTriangle size={18} className="text-amber-500" />
              ) : (
                <Sparkles size={18} className="text-cyan-500" />
              )}
              {title}
            </span>
          </DialogTitle>
        </DialogHeader>
        <div className="space-y-2">{body}</div>
        <DialogFooter>
          <Button variant="outline" onClick={dismiss}>
            Not now
          </Button>
          <Button onClick={goToPlan}>{primaryLabel}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

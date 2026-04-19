"use client";

import { X } from "lucide-react";
import { useState } from "react";
import { usePlanStatus } from "@/lib/use-plan-status";

const PLAN_LABELS: Record<string, string> = {
  starter: "Starter",
  shop: "Shop",
  pro: "Pro",
  business: "Business",
};

export function TrialBanner() {
  const [dismissed, setDismissed] = useState(false);
  const { data: status } = usePlanStatus();

  if (dismissed || !status) return null;

  const { trialStatus, trialDaysLeft, plan } = status;
  const planLabel = PLAN_LABELS[plan] ?? plan;

  if (trialStatus === "Active" && trialDaysLeft != null && trialDaysLeft <= 7) {
    return (
      <div className="bg-amber-50 border-b border-amber-200 px-4 py-2 flex items-center justify-between text-sm">
        <span className="text-amber-800">
          Your {planLabel} free trial ends in <strong>{trialDaysLeft} day{trialDaysLeft !== 1 ? "s" : ""}</strong>.
          {" "}Subscribe to keep access.
        </span>
        <button onClick={() => setDismissed(true)} className="text-amber-500 hover:text-amber-700 ml-2">
          <X size={14} />
        </button>
      </div>
    );
  }

  if (trialStatus === "GracePeriod") {
    return (
      <div className="bg-red-50 border-b border-red-200 px-4 py-2 flex items-center justify-between text-sm">
        <span className="text-red-800">
          Your {planLabel} free trial has ended. Your account will be restricted soon — <strong>subscribe now</strong> to keep access.
        </span>
        <button onClick={() => setDismissed(true)} className="text-red-400 hover:text-red-600 ml-2">
          <X size={14} />
        </button>
      </div>
    );
  }

  if (trialStatus === "Expired") {
    return (
      <div className="bg-red-50 border-b border-red-200 px-4 py-2 flex items-center justify-between text-sm">
        <span className="text-red-800">
          Your {planLabel} free trial has expired. <strong>Subscribe</strong> to restore access to your plan features.
        </span>
        <button onClick={() => setDismissed(true)} className="text-red-400 hover:text-red-600 ml-2">
          <X size={14} />
        </button>
      </div>
    );
  }

  return null;
}

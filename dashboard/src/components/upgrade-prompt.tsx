"use client";

import { Lock } from "lucide-react";

export function UpgradePrompt({
  feature,
  plan,
  children,
}: {
  feature: string;
  plan: string;
  children?: React.ReactNode;
}) {
  return (
    <div className="rounded-lg border border-dashed border-slate-300 dark:border-slate-700 bg-slate-50 dark:bg-slate-950 p-6 text-center">
      <Lock size={24} className="mx-auto text-slate-400 dark:text-slate-500 mb-2" />
      <p className="text-sm font-medium text-slate-700 dark:text-slate-300">{feature}</p>
      <p className="text-xs text-slate-500 dark:text-slate-400 mt-1">
        Available on the <span className="font-semibold">{plan}</span> plan and above.
      </p>
      {children}
    </div>
  );
}

export function UpgradeInline({
  feature,
  plan,
}: {
  feature: string;
  plan: string;
}) {
  return (
    <div className="flex items-center gap-2 rounded-md border border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-950 px-3 py-2">
      <Lock size={14} className="text-slate-400 dark:text-slate-500 shrink-0" />
      <p className="text-xs text-slate-500 dark:text-slate-400">
        {feature} requires the <span className="font-semibold">{plan}</span> plan.
      </p>
    </div>
  );
}

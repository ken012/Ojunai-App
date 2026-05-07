"use client";

import { Check, Circle } from "lucide-react";
import { passwordChecks } from "@/lib/password-policy";

/**
 * Live checklist of password requirements. Each rule turns green ✓ when met.
 * Render below a password input and pass the current value.
 */
export function PasswordStrengthHint({ password }: { password: string }) {
  const checks = passwordChecks(password);
  return (
    <ul className="text-[11px] space-y-0.5 mt-1.5">
      {checks.map((c, i) => (
        <li
          key={i}
          className={`flex items-center gap-1.5 ${
            c.met
              ? "text-emerald-600 dark:text-emerald-400"
              : "text-slate-400 dark:text-slate-500"
          }`}
        >
          {c.met ? <Check size={12} strokeWidth={3} /> : <Circle size={12} />}
          {c.label}
        </li>
      ))}
    </ul>
  );
}

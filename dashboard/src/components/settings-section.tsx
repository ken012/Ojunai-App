"use client";

import { ReactNode, useEffect } from "react";
import { ChevronDown } from "lucide-react";
import { useStickyState } from "@/lib/sticky-state";
import { cn } from "@/lib/utils";

/**
 * Wraps one section of the Settings page as an accordion item on both mobile AND desktop.
 *
 * Behavior:
 *   - Header bar (always visible) shows title + icon + chevron. Click toggles open.
 *   - Content shows only when open. State is persisted per-section in localStorage.
 *   - URL hash auto-opens: navigating to /settings#account opens the "account" section
 *     (the desktop sidebar updates the hash on click, so clicking a sidebar item
 *     opens + scrolls to that section).
 *
 * Usage:
 *   <SettingsSection id="business" title="Business" icon={<Building2 size={14}/>}>
 *     <Card>...</Card>
 *   </SettingsSection>
 *
 * Tradeoff: the section header bar duplicates the title that the wrapped Card's
 * CardHeader carries. Acceptable — the bar is the click target, the CardHeader
 * holds detail (icon styling, sub-text). Most users won't notice.
 */
export function SettingsSection({
  id,
  title,
  icon,
  children,
  defaultOpen = false,
}: {
  id: string;
  title: string;
  icon?: ReactNode;
  children: ReactNode;
  defaultOpen?: boolean;
}) {
  const [open, setOpen] = useStickyState<boolean>(`settings-${id}-open`, defaultOpen);

  // Auto-open when the URL hash points at this section. Fires on mount AND on
  // hashchange so sidebar clicks (which only update the hash) reliably expand
  // the matching section.
  useEffect(() => {
    if (typeof window === "undefined") return;
    function checkHash() {
      if (window.location.hash === `#${id}`) setOpen(true);
    }
    checkHash();
    window.addEventListener("hashchange", checkHash);
    return () => window.removeEventListener("hashchange", checkHash);
  }, [id, setOpen]);

  return (
    <section id={id} className="scroll-mt-24">
      <button
        type="button"
        onClick={() => setOpen(!open)}
        className="w-full flex items-center justify-between px-4 py-3 mb-2 rounded-lg border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800/60 transition-colors"
        aria-expanded={open}
        aria-controls={`${id}-content`}
      >
        <span className="flex items-center gap-2 text-sm font-semibold text-slate-900 dark:text-slate-100">
          {icon}
          {title}
        </span>
        <ChevronDown
          size={16}
          className={cn(
            "text-slate-500 dark:text-slate-400 transition-transform duration-150",
            open && "rotate-180",
          )}
        />
      </button>

      <div id={`${id}-content`} className={open ? "block" : "hidden"}>
        {children}
      </div>
    </section>
  );
}

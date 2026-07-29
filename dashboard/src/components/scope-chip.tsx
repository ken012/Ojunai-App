"use client";

import { MapPin } from "lucide-react";
import { useBusiness } from "@/lib/data-sync";
import { getSelectedLocation } from "@/lib/location";

/**
 * A read-only pill that states which branch scope the numbers next to it represent, so a branch figure is
 * never mistaken for a company-wide one (and vice-versa). Drop it into a page/section header.
 *
 *  • Renders nothing for single-location businesses (or businesses with ≤1 active location) — zero footprint.
 *  • A specific branch selected → solid coloured pill with the branch name.
 *  • "All branches" selected → outline pill with the branch count.
 *  • `businessWide` forces the "All branches" style regardless of the current selection — use it on a
 *    company-wide section that lives on an otherwise branch-scoped page (e.g. the debt/aging cards on
 *    /contacts, or receivables/payables on the overview) so selecting a branch never appears to filter it.
 *
 * It reads the selection synchronously from the same store the switcher writes; the switcher does a full
 * reload on change, so this re-reads and stays in sync.
 */
export function ScopeChip({
  businessWide = false,
  className = "",
}: {
  businessWide?: boolean;
  className?: string;
}) {
  const business = useBusiness();
  if (!business?.isMultiLocation) return null;

  const active = (business.locations ?? []).filter((l) => l.isActive);
  if (active.length <= 1) return null;

  const selectedId = businessWide ? null : getSelectedLocation();
  const selected = selectedId ? active.find((l) => l.id === selectedId) : null;
  const solid = !!selected;
  const label = selected ? selected.name : `All branches (${active.length})`;

  return (
    <span
      className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium ${
        solid
          ? "border border-cyan-200 bg-cyan-50 text-cyan-700 dark:border-cyan-800 dark:bg-cyan-950 dark:text-cyan-300"
          : "border border-slate-200 text-slate-500 dark:border-slate-700 dark:text-slate-400"
      } ${className}`}
      title={
        businessWide
          ? "This section is company-wide — it includes every branch."
          : solid
            ? `Showing ${selected!.name} only`
            : "Showing all branches combined"
      }
    >
      <MapPin size={12} className={solid ? "text-cyan-500" : "text-slate-400"} />
      {label}
    </span>
  );
}

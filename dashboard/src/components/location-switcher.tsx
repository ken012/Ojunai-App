"use client";

import { useEffect, useState } from "react";
import { usePathname } from "next/navigation";
import { MapPin } from "lucide-react";
import { useBusiness } from "@/lib/data-sync";
import { getSelectedLocation, setSelectedLocation } from "@/lib/location";
import { scopingForPath } from "@/lib/location-scope";

/**
 * Location switcher (multi-location). Scoped to the CURRENT user's accessible locations:
 *  • Owner/Admin (all-access) — every active location plus an "All locations" option.
 *  • Restricted staff — only their assigned locations (or the default only), and NO "All": they're always
 *    scoped to one location (the server enforces this regardless of what the client sends). If they can reach
 *    just one location, we show it as a static badge instead of a dropdown.
 * Single-location businesses (and users with a single accessible location) never send X-Location-Id, so their
 * data stays exactly as before. Switching does a full reload so every view (React Query AND useEffect fetches)
 * refetches with the new header.
 */
export function LocationSwitcher() {
  const business = useBusiness();
  const pathname = usePathname();

  const accessibleIds = business?.accessibleLocationIds ?? null;
  const activeAll = (business?.locations ?? []).filter((l) => l.isActive);
  // Restrict to the user's accessible set when the server sent one; otherwise (older payload) show all active.
  const active = accessibleIds ? activeAll.filter((l) => accessibleIds.includes(l.id)) : activeAll;
  const restricted = !!business?.locationAccessRestricted;
  const enabled = !!business?.isMultiLocation;
  const canSwitch = enabled && active.length > 1;

  const [selected, setSelected] = useState<string>(getSelectedLocation() ?? "all");

  useEffect(() => {
    if (business == null) return; // not loaded yet
    const stored = getSelectedLocation();
    const validStored = stored != null && active.some((l) => l.id === stored);

    if (restricted) {
      // Restricted users are always scoped to one accessible location — never "All".
      const next = validStored ? stored! : active[0]?.id ?? null;
      if (getSelectedLocation() !== next) setSelectedLocation(next);
      setSelected(next ?? "all");
    } else {
      // Owner/Admin: "All" is valid; only clear a selection that's no longer a real accessible location.
      if (stored != null && !validStored) {
        setSelectedLocation(null);
        setSelected("all");
      } else {
        setSelected(stored ?? "all");
      }
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [business]);

  if (!enabled) return null;

  // On pages that don't filter by branch (settings, onboarding, catalog, import, voice-ai), never present an
  // interactive branch filter — that would imply the page is branch-specific when it isn't. Show a locked
  // "All branches" chip instead, so the scope is stated but clearly not switchable here.
  if (scopingForPath(pathname) !== "scoped") {
    return (
      <div
        className="inline-flex items-center gap-1.5 rounded-md border border-slate-200 dark:border-slate-700 px-2 h-8 text-xs font-medium text-slate-400 dark:text-slate-500"
        title="This page isn't branch-specific — it applies to your whole business."
      >
        <MapPin size={14} className="text-slate-400" />
        <span>All branches</span>
      </div>
    );
  }

  if (!canSwitch) {
    // Restricted to a single location → show which one (no switching to do).
    if (restricted && active.length === 1) {
      return (
        <div className="inline-flex items-center gap-1.5 text-xs font-medium text-slate-600 dark:text-slate-300">
          <MapPin size={14} className="text-slate-400" />
          <span>{active[0].name}</span>
        </div>
      );
    }
    return null;
  }

  const value = active.some((l) => l.id === selected) ? selected : restricted ? active[0].id : "all";

  function onChange(next: string) {
    setSelectedLocation(next === "all" ? null : next); // persisted → survives the reload
    if (typeof window !== "undefined") window.location.reload();
  }

  return (
    <div className="inline-flex items-center gap-2">
      <label className="inline-flex items-center gap-1.5 text-xs font-medium text-slate-600 dark:text-slate-300">
        <MapPin size={14} className="text-slate-400" />
        <select
          value={value}
          onChange={(e) => onChange(e.target.value)}
          className="h-8 rounded-md border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 px-2 text-sm text-slate-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-cyan-500"
          aria-label="Location"
        >
          {!restricted && <option value="all">All locations</option>}
          {active.map((l) => (
            <option key={l.id} value={l.id}>{l.name}</option>
          ))}
        </select>
      </label>
      {!restricted && (
        <a href="/settings#locations" className="text-xs text-slate-400 hover:text-slate-600 dark:hover:text-slate-200">Manage</a>
      )}
    </div>
  );
}

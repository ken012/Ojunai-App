"use client";

import { useEffect, useState } from "react";
import { MapPin } from "lucide-react";
import { useBusiness } from "@/lib/data-sync";
import { getSelectedLocation, setSelectedLocation } from "@/lib/location";

/**
 * Location switcher (multi-location Phase 2b). Rendered only for multi-location businesses with more than
 * one active location — single-location businesses never see it, so they never send X-Location-Id and their
 * data stays business-wide. Selecting a location attaches the header (via the axios interceptor) and
 * refetches, so stock lists show that location's stock. "All locations" clears the header (business-wide).
 */
export function LocationSwitcher() {
  const business = useBusiness();
  const [selected, setSelected] = useState<string>(getSelectedLocation() ?? "all");

  const active = (business?.locations ?? []).filter((l) => l.isActive);
  const isMulti = !!business?.isMultiLocation && active.length > 1;

  // Clear a stale selection (e.g. left over from a past multi-location period, or a now-inactive location)
  // so no X-Location-Id leaks onto requests once the business is single-location again.
  useEffect(() => {
    if (business == null) return; // not loaded yet
    const stale = getSelectedLocation();
    if (!isMulti || (stale != null && !active.some((l) => l.id === stale))) {
      if (getSelectedLocation() != null) setSelectedLocation(null);
      setSelected("all");
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [business]);

  if (!isMulti) return null;

  const value = selected !== "all" && !active.some((l) => l.id === selected) ? "all" : selected;

  function onChange(next: string) {
    setSelectedLocation(next === "all" ? null : next); // persisted → survives the reload
    // Full reload so EVERY view refetches with the new X-Location-Id. The dashboard mixes React Query and
    // plain useEffect+api.get fetches, so invalidateQueries alone wouldn't refresh the manual-fetch pages.
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
          <option value="all">All locations</option>
          {active.map((l) => (
            <option key={l.id} value={l.id}>{l.name}</option>
          ))}
        </select>
      </label>
      <a href="/settings#locations" className="text-xs text-slate-400 hover:text-slate-600 dark:hover:text-slate-200">Manage</a>
    </div>
  );
}

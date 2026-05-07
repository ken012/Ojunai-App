"use client";

import { useEffect, useState } from "react";

/**
 * Like useState but persisted to localStorage under a stable key.
 *
 * Usage:
 *   const [tab, setTab] = useStickyState<"all" | "paid">("sales-tab", "all");
 *
 * Notes:
 *  - SSR-safe: initial render uses the default; effect rehydrates from storage.
 *  - Validator is optional but recommended when the value union narrows over time
 *    (so a stale persisted value can be discarded instead of breaking the page).
 */
export function useStickyState<T>(
  key: string,
  defaultValue: T,
  validator?: (v: unknown) => v is T,
): [T, (v: T) => void] {
  const [value, setValue] = useState<T>(defaultValue);

  // Read once on mount
  useEffect(() => {
    if (typeof window === "undefined") return;
    try {
      const raw = window.localStorage.getItem(key);
      if (raw === null) return;
      const parsed = JSON.parse(raw);
      if (!validator || validator(parsed)) {
        setValue(parsed as T);
      }
    } catch { /* ignore */ }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);

  // Persist on change
  useEffect(() => {
    if (typeof window === "undefined") return;
    try { window.localStorage.setItem(key, JSON.stringify(value)); } catch { /* quota */ }
  }, [key, value]);

  return [value, setValue];
}

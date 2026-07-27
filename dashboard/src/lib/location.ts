// Selected-location store (multi-location Phase 2b). Holds the location the user picked in the switcher,
// persisted so it survives reloads. The axios request interceptor reads it synchronously to attach the
// X-Location-Id header; the switcher writes it and refetches. `null` = "All locations" (no header) =
// business-wide, i.e. exactly the single-location behaviour. Only the switcher (shown for multi-location
// businesses) ever sets this, so single-location dashboards never send the header.

const KEY = "oj_location";

let _current: string | null =
  typeof window !== "undefined" ? window.localStorage.getItem(KEY) : null;

export function getSelectedLocation(): string | null {
  return _current;
}

export function setSelectedLocation(id: string | null): void {
  _current = id;
  if (typeof window === "undefined") return;
  if (id) window.localStorage.setItem(KEY, id);
  else window.localStorage.removeItem(KEY);
}

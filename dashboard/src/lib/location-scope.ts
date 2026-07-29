// Per-route location scoping — mirrors the backend: which dashboard pages actually filter their data by the
// selected branch. Drives the LocationSwitcher (interactive dropdown vs a locked "All branches" chip) and the
// header ScopeChip. The rule: a page must never present an interactive branch filter if its numbers don't
// actually move when you switch branch — that's a "phantom filter" and, on the ledger especially, a
// misread-your-own-money bug.
//
//  • "scoped"        — data honours the selected branch (inventory, sales, expenses, reports, activity,
//                      contacts list, and the overview). The switcher is interactive here. Note some of these
//                      pages ALSO show business-wide blocks (e.g. debt aging on /contacts, receivables/payables
//                      on the overview) — those individual blocks wear their own <ScopeChip businessWide />.
//  • "config"        — configuration / business-level metadata with no per-branch data (settings, onboarding,
//                      variants catalog, import, voice-ai). The switcher shows a locked "All branches" chip.
export type LocationScoping = "scoped" | "config";

const RULES: { prefix: string; scoping: LocationScoping }[] = [
  { prefix: "/inventory", scoping: "scoped" },
  { prefix: "/sales", scoping: "scoped" },
  { prefix: "/expenses", scoping: "scoped" },
  { prefix: "/stocktake", scoping: "scoped" },
  { prefix: "/purchasing", scoping: "scoped" },
  { prefix: "/expiring", scoping: "scoped" },
  { prefix: "/reservations", scoping: "scoped" },
  { prefix: "/reports", scoping: "scoped" },
  { prefix: "/export", scoping: "scoped" },
  { prefix: "/activity", scoping: "scoped" },
  { prefix: "/contacts", scoping: "scoped" },
  { prefix: "/settings", scoping: "config" },
  { prefix: "/get-started", scoping: "config" },
  { prefix: "/variants", scoping: "config" },
  { prefix: "/import", scoping: "config" },
  { prefix: "/voice-ai", scoping: "config" },
];

// Longest matching prefix wins. The root "/" (overview) matches no rule and defaults to "scoped" — it has an
// interactive switcher, with its business-wide cards carrying their own "All branches" chip.
export function scopingForPath(pathname: string): LocationScoping {
  let best: LocationScoping = "scoped";
  let bestLen = 0;
  for (const r of RULES) {
    if ((pathname === r.prefix || pathname.startsWith(r.prefix + "/")) && r.prefix.length > bestLen) {
      best = r.scoping;
      bestLen = r.prefix.length;
    }
  }
  return best;
}

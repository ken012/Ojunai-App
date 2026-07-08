// Reads the business currency from localStorage so every page automatically reflects
// the currency set in Settings. Falls back to NGN if not available (server-side rendering,
// not logged in, or currency not set).
function getBusinessTimezone(): string {
  if (typeof window === "undefined") return "Africa/Lagos";
  try {
    const raw = localStorage.getItem("oj_business");
    if (raw) {
      const biz = JSON.parse(raw);
      if (biz.timezone) return biz.timezone;
    }
  } catch { /* ignore */ }
  return "Africa/Lagos";
}

function getBusinessCurrency(): string {
  if (typeof window === "undefined") return "NGN";
  try {
    const raw = localStorage.getItem("oj_business");
    if (raw) {
      const biz = JSON.parse(raw);
      if (biz.currency && biz.currency.length === 3) return biz.currency;
    }
  } catch { /* ignore parse errors */ }
  return "NGN";
}

export function formatNaira(amount: number): string {
  const currency = getBusinessCurrency();
  return new Intl.NumberFormat("en", {
    style: "currency",
    currency,
    minimumFractionDigits: 0,
    maximumFractionDigits: 0,
  }).format(amount);
}

export function formatDate(dateStr: string): string {
  const tz = getBusinessTimezone();
  return new Date(dateStr).toLocaleDateString("en", {
    day: "numeric",
    month: "short",
    year: "numeric",
    timeZone: tz,
  });
}

export function formatDateTime(dateStr: string): string {
  const tz = getBusinessTimezone();
  return new Date(dateStr).toLocaleString("en", {
    day: "numeric",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
    timeZone: tz,
  });
}

export function formatShortDate(dateStr: string): string {
  const tz = getBusinessTimezone();
  return new Date(dateStr).toLocaleDateString("en", {
    day: "numeric",
    month: "short",
    timeZone: tz,
  });
}

// Measurement abbreviations that never take a plural "s" ("10 kg", not "10 kgs").
const INVARIABLE_UNITS = new Set([
  "kg", "g", "mg", "l", "ml", "cl", "oz", "lb", "lbs",
  "m", "cm", "mm", "km", "ft", "in",
]);

// Returns the unit word pluralized for the given quantity: "pack" → "packs",
// "piece" → "pieces", "box" → "boxes", "berry" → "berries". Measurement
// abbreviations (kg, ml, …) and already-plural inputs ("packs") are left as-is,
// and qty of exactly 1 keeps the singular. Returns "" when no unit is set, so
// callers can compose "{qty} {pluralUnit(qty, unit)}" safely.
export function pluralUnit(qty: number, unit?: string | null): string {
  const u = (unit ?? "").trim();
  if (!u || qty === 1) return u;
  const lower = u.toLowerCase();
  if (INVARIABLE_UNITS.has(lower)) return u;
  if (/[^s]s$/i.test(u)) return u;                       // already plural ("packs")
  if (/(s|x|z|ch|sh)$/i.test(u)) return `${u}es`;        // box → boxes, dish → dishes
  if (/[^aeiou]y$/i.test(u)) return `${u.slice(0, -1)}ies`; // berry → berries
  return `${u}s`;
}

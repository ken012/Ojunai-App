// Reads the business currency from localStorage so every page automatically reflects
// the currency set in Settings. Falls back to NGN if not available (server-side rendering,
// not logged in, or currency not set).
function getBusinessTimezone(): string {
  if (typeof window === "undefined") return "Africa/Lagos";
  try {
    const raw = localStorage.getItem("bp_business");
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
    const raw = localStorage.getItem("bp_business");
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

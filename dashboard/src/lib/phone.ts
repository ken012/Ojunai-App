/**
 * Normalize a user-typed phone number to E.164 ("+234...") format.
 *
 * Twilio and the API expect E.164. Users routinely type local Nigerian formats
 * like "08012345678", "0801 234 5678", "(0801) 234-5678", or "234 801 234 5678" —
 * so we strip whitespace/punctuation and convert known Nigerian-local prefixes
 * to a +234 number. For other inputs we keep the digits and prepend a `+` so the
 * server can decide rather than silently rejecting on the client.
 *
 * Returns `null` if the input clearly isn't a phone number (contains `@`, or is
 * too short to be one). Callers can then either error out or pass the raw input
 * through to a phone-or-email field.
 */
export function normalizePhone(input: string): string | null {
  if (!input) return null;
  if (input.includes("@")) return null; // looks like an email
  const trimmed = input.trim();
  const hasPlus = trimmed.startsWith("+");
  const digits = trimmed.replace(/\D/g, "");
  if (digits.length < 7) return null; // too short to be a phone number

  if (hasPlus) return "+" + digits;
  // International dial-out prefix (e.g. "002348012345678") → "+2348012345678"
  if (digits.startsWith("00")) return "+" + digits.slice(2);
  // Already country-coded but missing the plus: "234..." (Nigerian numbers are
  // 13 digits including the 234)
  if (digits.startsWith("234") && digits.length >= 12) return "+" + digits;
  // Nigerian local format with leading zero: 0XXXXXXXXXX → +234XXXXXXXXXX
  if (digits.startsWith("0") && (digits.length === 10 || digits.length === 11)) {
    return "+234" + digits.slice(1);
  }
  // Bare 10-digit (no leading 0) — assume Nigeria.
  if (digits.length === 10) return "+234" + digits;
  // Anything else with enough digits — prepend `+` and let the server validate.
  return "+" + digits;
}

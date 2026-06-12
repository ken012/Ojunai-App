// Mirror of Ojunai.API/Common/PasswordPolicy.cs. Server is authoritative — this exists
// for live UI feedback as the user types. If you change rules here, update both files.

export const MIN_LENGTH = 10;

const BLOCKLIST = new Set([
  "password", "password1", "password12", "password123", "password1234",
  "12345678", "123456789", "1234567890", "qwerty", "qwerty123", "qwerty1234",
  "abc12345", "abcdefgh", "iloveyou", "letmein", "welcome1", "welcome123",
  "passw0rd", "p@ssword", "p@ssw0rd", "admin1234", "administrator",
  "ojunai", "ojunai123", "shopowner", "starter12", "businessowner",
]);

function classCount(pw: string): number {
  let c = 0;
  if (/[a-z]/.test(pw)) c++;
  if (/[A-Z]/.test(pw)) c++;
  if (/[0-9]/.test(pw)) c++;
  if (/[^a-zA-Z0-9]/.test(pw)) c++;
  return c;
}

export function validatePassword(pw: string): { ok: boolean; reason?: string } {
  if (!pw) return { ok: false, reason: `Password is required (min ${MIN_LENGTH} characters).` };
  if (pw.length < MIN_LENGTH) return { ok: false, reason: `Password must be at least ${MIN_LENGTH} characters.` };
  if (new Set(pw).size < 4) return { ok: false, reason: "Password is too repetitive — mix in different characters." };
  if (BLOCKLIST.has(pw.toLowerCase())) return { ok: false, reason: "That's a commonly used password — please choose something stronger." };
  if (classCount(pw) < 4) return { ok: false, reason: "Password must include uppercase, lowercase, digits, and symbols." };
  return { ok: true };
}

export interface PasswordCheck {
  label: string;
  met: boolean;
}

export function passwordChecks(pw: string): PasswordCheck[] {
  return [
    { label: `At least ${MIN_LENGTH} characters`, met: pw.length >= MIN_LENGTH },
    { label: "Mix of upper, lower, digits, symbols", met: classCount(pw) >= 4 },
    { label: "Not a common password", met: pw.length > 0 && !BLOCKLIST.has(pw.toLowerCase()) },
    { label: "Enough unique characters", met: new Set(pw).size >= 4 },
  ];
}

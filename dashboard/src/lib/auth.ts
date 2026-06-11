import { api } from "./api";
import type { AuthResponse, UserDto, BusinessDto } from "./types";

const SESSION_TIMEOUT_MS = 12 * 60 * 60 * 1000; // 12 hours

/**
 * Strip PII before persisting the user to localStorage. Phone number and date
 * of birth aren't needed for any UI render outside the Settings page (which
 * fetches them fresh from /auth/me on mount), so they shouldn't sit at rest in
 * the browser where an XSS would have free access.
 *
 * Whitelist approach — anything new added to UserDto stays out of cache by default.
 */
function cacheableUser(user: UserDto): Partial<UserDto> {
  return {
    id: user.id,
    fullName: user.fullName,
    email: user.email,
    emailVerified: user.emailVerified,
    role: user.role,
  };
}

function storeAuth(auth: AuthResponse) {
  localStorage.setItem("oj_user", JSON.stringify(cacheableUser(auth.user)));
  localStorage.setItem("oj_business", JSON.stringify(auth.business));
  localStorage.setItem("oj_auth_time", Date.now().toString());
}

function isSessionExpired(): boolean {
  const authTime = localStorage.getItem("oj_auth_time");
  if (!authTime) return true;
  const elapsed = Date.now() - parseInt(authTime, 10);
  return elapsed > SESSION_TIMEOUT_MS;
}

export function getStoredUser(): UserDto | null {
  if (typeof window === "undefined") return null;
  if (isSessionExpired()) { clearAuth(); return null; }
  const raw = localStorage.getItem("oj_user");
  if (!raw) return null;
  const parsed = JSON.parse(raw) as UserDto;
  // Self-heal legacy cache entries written before the PII-strip policy: if
  // we find phone/DOB in storage, re-write the cleaned version on read so
  // existing sessions get scrubbed without waiting for next sync.
  if (parsed.phoneNumber || parsed.dateOfBirth) {
    const cleaned = cacheableUser(parsed);
    localStorage.setItem("oj_user", JSON.stringify(cleaned));
    return cleaned as UserDto;
  }
  return parsed;
}

export function getStoredBusiness(): BusinessDto | null {
  if (typeof window === "undefined") return null;
  if (isSessionExpired()) { clearAuth(); return null; }
  const raw = localStorage.getItem("oj_business");
  return raw ? JSON.parse(raw) : null;
}

export function isAuthenticated(): boolean {
  if (typeof window === "undefined") return false;
  if (!localStorage.getItem("oj_user")) return false;
  if (isSessionExpired()) { clearAuth(); return false; }
  return true;
}

export async function login(phoneOrEmail: string, password: string): Promise<AuthResponse> {
  const { data } = await api.post<{ data: AuthResponse }>("/auth/login", { phoneOrEmail, password });
  const auth = data.data!;
  storeAuth(auth);
  return auth;
}

export type RegisterPayload = {
  fullName: string;
  phoneNumber: string;
  email?: string;
  password: string;
  businessName: string;
  businessType?: string;
  state?: string;
  city?: string;
  dateOfBirth?: string;
};

export async function register(payload: RegisterPayload): Promise<AuthResponse> {
  const { data } = await api.post<{ data: AuthResponse }>("/auth/register", payload);
  const auth = data.data!;
  storeAuth(auth);
  return auth;
}

export async function requestPhoneVerification(
  phoneNumber: string,
): Promise<{ phoneNumber: string; expiresAtUtc: string; resendCooldownSeconds: number }> {
  const { data } = await api.post<{ data: { phoneNumber: string; expiresAtUtc: string; resendCooldownSeconds: number } }>(
    "/auth/request-phone-verification",
    { phoneNumber },
  );
  return data.data!;
}

export async function verifyPhoneAndRegister(payload: RegisterPayload & { code: string }): Promise<AuthResponse> {
  const { data } = await api.post<{ data: AuthResponse }>("/auth/verify-phone-and-register", payload);
  const auth = data.data!;
  storeAuth(auth);
  return auth;
}

export async function requestEmailVerification(): Promise<{ expiresAtUtc: string }> {
  const { data } = await api.post<{ data: { expiresAtUtc: string } }>("/auth/request-email-verification", {});
  return data.data!;
}

export async function verifyEmail(token: string): Promise<void> {
  await api.post("/auth/verify-email", { token });
  // Refresh stored user so EmailVerified flag updates everywhere — UserDto is what powers the banner.
  if (typeof window !== "undefined" && localStorage.getItem("oj_user")) {
    try {
      const { data } = await api.get<{ data: UserDto }>("/auth/me");
      const me = data.data!;
      // Cache only the safe subset — same policy as storeAuth.
      localStorage.setItem("oj_user", JSON.stringify(cacheableUser(me)));
    } catch {
      // Best-effort refresh — banner will sync on next page load anyway.
    }
  }
}

// ─── Account recovery (phone-loss) ─────────────────────────────────────────

export interface RecoveryTokenInfo {
  fullName: string;
  maskedPhone: string;
  maskedEmail: string;
  businessName: string;
}

export async function requestAccountRecovery(email: string): Promise<void> {
  await api.post("/auth/request-account-recovery", { email });
}

export async function inspectRecoveryToken(token: string): Promise<RecoveryTokenInfo> {
  const { data } = await api.post<{ data: RecoveryTokenInfo }>("/auth/recover-account/info", { token });
  return data.data!;
}

export async function recoverAccountResetPassword(token: string, newPassword: string): Promise<AuthResponse> {
  const { data } = await api.post<{ data: AuthResponse }>("/auth/recover-account/reset-password", { token, newPassword });
  const auth = data.data!;
  storeAuth(auth);
  return auth;
}

export async function recoverAccountRequestPhoneOtp(
  token: string,
  newPhoneNumber: string,
): Promise<{ phoneNumber: string; expiresAtUtc: string; resendCooldownSeconds: number }> {
  const { data } = await api.post<{ data: { phoneNumber: string; expiresAtUtc: string; resendCooldownSeconds: number } }>(
    "/auth/recover-account/request-phone-otp",
    { token, newPhoneNumber },
  );
  return data.data!;
}

export async function recoverAccountChangePhone(
  token: string,
  newPhoneNumber: string,
  code: string,
): Promise<AuthResponse> {
  const { data } = await api.post<{ data: AuthResponse }>("/auth/recover-account/change-phone", {
    token,
    newPhoneNumber,
    code,
  });
  const auth = data.data!;
  storeAuth(auth);
  return auth;
}

function clearAuth() {
  localStorage.removeItem("oj_user");
  localStorage.removeItem("oj_business");
  localStorage.removeItem("oj_auth_time");
}

/**
 * Drop the service worker's HTML + static caches on logout. The HTML cache
 * holds shell pages (no user data baked in) but evicting it ensures the next
 * user on the device gets a fresh load instead of a possibly-stale Ojunai
 * route. Wrapped in try/catch so cache failures never block sign-out.
 */
async function clearSwCaches() {
  if (typeof window === "undefined" || !("caches" in window)) return;
  try {
    const names = await caches.keys();
    await Promise.all(
      names
        .filter((n) => n.startsWith("ojunai-") || n === "start-url")
        .map((n) => caches.delete(n))
    );
  } catch {
    // best-effort — never let cache cleanup block logout
  }
}

export async function logout() {
  try { await api.post("/auth/logout"); } catch {}
  clearAuth();
  await clearSwCaches();
  window.location.href = "/login";
}

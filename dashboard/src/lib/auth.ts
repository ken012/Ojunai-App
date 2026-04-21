import { api } from "./api";
import type { AuthResponse, UserDto, BusinessDto } from "./types";

const SESSION_TIMEOUT_MS = 12 * 60 * 60 * 1000; // 12 hours

function storeAuth(auth: AuthResponse) {
  localStorage.setItem("bp_user", JSON.stringify(auth.user));
  localStorage.setItem("bp_business", JSON.stringify(auth.business));
  localStorage.setItem("bp_auth_time", Date.now().toString());
}

function isSessionExpired(): boolean {
  const authTime = localStorage.getItem("bp_auth_time");
  if (!authTime) return true;
  const elapsed = Date.now() - parseInt(authTime, 10);
  return elapsed > SESSION_TIMEOUT_MS;
}

export function getStoredUser(): UserDto | null {
  if (typeof window === "undefined") return null;
  if (isSessionExpired()) { clearAuth(); return null; }
  const raw = localStorage.getItem("bp_user");
  return raw ? JSON.parse(raw) : null;
}

export function getStoredBusiness(): BusinessDto | null {
  if (typeof window === "undefined") return null;
  if (isSessionExpired()) { clearAuth(); return null; }
  const raw = localStorage.getItem("bp_business");
  return raw ? JSON.parse(raw) : null;
}

export function isAuthenticated(): boolean {
  if (typeof window === "undefined") return false;
  if (!localStorage.getItem("bp_user")) return false;
  if (isSessionExpired()) { clearAuth(); return false; }
  return true;
}

export async function login(phoneOrEmail: string, password: string): Promise<AuthResponse> {
  const { data } = await api.post<{ data: AuthResponse }>("/auth/login", { phoneOrEmail, password });
  const auth = data.data!;
  storeAuth(auth);
  return auth;
}

export async function register(payload: {
  fullName: string;
  phoneNumber: string;
  email?: string;
  password: string;
  businessName: string;
  businessType?: string;
  state?: string;
  city?: string;
  dateOfBirth?: string;
}): Promise<AuthResponse> {
  const { data } = await api.post<{ data: AuthResponse }>("/auth/register", payload);
  const auth = data.data!;
  storeAuth(auth);
  return auth;
}

function clearAuth() {
  localStorage.removeItem("bp_user");
  localStorage.removeItem("bp_business");
  localStorage.removeItem("bp_auth_time");
}

export async function logout() {
  try { await api.post("/auth/logout"); } catch {}
  clearAuth();
  window.location.href = "/login";
}

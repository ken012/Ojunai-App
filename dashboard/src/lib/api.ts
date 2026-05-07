import axios from "axios";

interface PageEnvelope<T> {
  items: T[];
  totalPages?: number;
  totalCount?: number;
}

const BASE_URL = process.env.NEXT_PUBLIC_API_URL || "http://localhost:5000/api";

/**
 * API origin without the `/api` suffix — used for static assets like uploaded images
 * served by Nginx at `/uploads/...`. Strips a trailing `/api` so the result is the bare
 * scheme+host, ready to prefix any non-API path the server returns.
 */
export const API_ORIGIN = BASE_URL.replace(/\/api\/?$/, "");

/**
 * Resolves a server-relative path (e.g. "/uploads/businesses/.../bg.jpg") to an absolute
 * URL on the API host. Pass-through for null/empty/already-absolute inputs.
 */
export function absoluteApiUrl(path: string | null | undefined): string | null {
  if (!path) return null;
  if (/^https?:\/\//i.test(path)) return path;
  return API_ORIGIN + (path.startsWith("/") ? path : `/${path}`);
}

export const api = axios.create({
  baseURL: BASE_URL,
  headers: { "Content-Type": "application/json" },
  withCredentials: true,
});

// On 401, clear local state and redirect to login
api.interceptors.response.use(
  (res) => res,
  (err) => {
    if (err.response?.status === 401 && typeof window !== "undefined") {
      // Don't redirect during login/register/password-reset flow — let the page show the error
      const path = window.location.pathname;
      const skipRedirect = ["/login", "/register", "/forgot-password", "/change-password", "/admin"];
      const shouldSkip = skipRedirect.some((p) => path.startsWith(p));
      if (!shouldSkip) {
        localStorage.removeItem("bp_user");
        localStorage.removeItem("bp_business");
        localStorage.removeItem("bp_auth_time");
        window.location.href = "/login";
      }
    }
    return Promise.reject(err);
  }
);

/**
 * Fetch every page of a paginated endpoint and return the concatenated items.
 * Use this when a list view needs the full dataset for client-side search/filter.
 *
 * `urlForPage(page, pageSize)` should return the full path including any extra
 * query string the caller wants to forward (filters, business scope, etc.).
 */
export async function fetchAllPaged<T>(
  urlForPage: (page: number, pageSize: number) => string,
  options: { pageSize?: number; maxPages?: number } = {}
): Promise<T[]> {
  const pageSize = options.pageSize ?? 500;
  const maxPages = options.maxPages ?? 50;
  const all: T[] = [];
  let page = 1;
  while (page <= maxPages) {
    const { data } = await api.get<{ data: PageEnvelope<T> }>(urlForPage(page, pageSize));
    const result = data.data!;
    all.push(...result.items);
    if (page >= (result.totalPages || 1) || result.items.length < pageSize) break;
    page++;
  }
  return all;
}

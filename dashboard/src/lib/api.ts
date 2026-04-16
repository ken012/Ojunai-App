import axios from "axios";

const BASE_URL = process.env.NEXT_PUBLIC_API_URL || "http://localhost:5000/api";

export const api = axios.create({
  baseURL: BASE_URL,
  headers: { "Content-Type": "application/json" },
});

// Inject JWT from localStorage on every request
api.interceptors.request.use((config) => {
  if (typeof window !== "undefined") {
    const token = localStorage.getItem("bp_token");
    if (token) config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// On 401, clear token and redirect to login
api.interceptors.response.use(
  (res) => res,
  (err) => {
    if (err.response?.status === 401 && typeof window !== "undefined") {
      // Don't redirect during login/register/password-reset flow — let the page show the error
      const path = window.location.pathname;
      const authPages = ["/login", "/register", "/forgot-password", "/change-password"];
      const isAuthPage = authPages.some((p) => path.startsWith(p));
      if (!isAuthPage) {
        localStorage.removeItem("bp_token");
        localStorage.removeItem("bp_user");
        localStorage.removeItem("bp_business");
        localStorage.removeItem("bp_auth_time");
        window.location.href = "/login";
      }
    }
    return Promise.reject(err);
  }
);

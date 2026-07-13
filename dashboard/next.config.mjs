import withPWAInit from "@ducanh2912/next-pwa";

/** @type {import('next').NextConfig} */
const nextConfig = {
  // Security headers for the dashboard HTML origin. Conservative on purpose: it adds
  // clickjacking (frame-ancestors), base-tag-injection (base-uri) and plugin (object-src)
  // protection plus the standard hardening headers, WITHOUT constraining script-src/connect-src.
  // A full nonce-based script-src CSP is a follow-up that must be validated against the running
  // app (Next.js inline hydration + the inline theme-boot script would otherwise break).
  async headers() {
    return [
      {
        source: "/:path*",
        headers: [
          {
            key: "Content-Security-Policy",
            value: "frame-ancestors 'none'; base-uri 'self'; object-src 'none'; form-action 'self'",
          },
          { key: "X-Frame-Options", value: "DENY" },
          { key: "X-Content-Type-Options", value: "nosniff" },
          { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
          { key: "Permissions-Policy", value: "geolocation=(), microphone=(), camera=()" },
        ],
      },
    ];
  },
};

// ─── Service Worker security model ─────────────────────────────────────────
// Three categories of requests, three strategies. Anything not matched by a
// runtime-cache rule falls through to the browser's default network behavior
// (no persistent SW cache).
//
//   1) /api/* and explicit user-data paths  → NetworkOnly. Never cached.
//      Covers auth, dashboard data, inventory, sales, receipts, exports.
//   2) Authenticated HTML routes            → NetworkFirst, 1-hour TTL.
//      HTML is a Next.js client-component shell with no user data baked in.
//      Client-side auth check redirects to /login on unauth.
//   3) Static fingerprinted assets          → CacheFirst, 30-day TTL.
//      Strict allowlist of OUR build output and brand assets only. User
//      uploads, third-party domains, anything ad-hoc — intentionally not
//      matched, so they never enter the cache.
//
// Auth tokens (cookies) are HttpOnly and never visible to JS or the SW.
// localStorage state (user profile snapshot) is PII-stripped at write time
// (see src/lib/auth.ts) and is independent of the SW cache.
// ───────────────────────────────────────────────────────────────────────────

const withPWA = withPWAInit({
  dest: "public",
  register: true,
  disable: process.env.NODE_ENV === "development",
  // Offline fallback: SW serves /offline whenever a navigation request can't
  // be answered from the network or the runtime cache.
  fallbacks: {
    document: "/offline",
  },
  // Cache the shell HTML during in-app navigation so back/forward and quick
  // re-visits are instant. The HTML is shell-only and the TTL is short, so
  // there's no sensitive-data exposure window. We deliberately do NOT enable
  // `aggressiveFrontEndNavCaching` — too eager for an authenticated app.
  cacheOnFrontEndNav: true,
  reloadOnOnline: true,
  // Don't precache the historical logo backups — old design, no user benefit.
  buildExcludes: [/^_logo-backup\//, /_logo-backup\//],
  workboxOptions: {
    disableDevLogs: true,
    runtimeCaching: [
      // ── 1) NEVER cache: API and any user-data endpoints ────────────────
      {
        urlPattern: ({ url }) => url.pathname.startsWith("/api/"),
        handler: "NetworkOnly",
      },
      // Belt-and-suspenders: explicit deny for paths that obviously carry
      // user data even if a future refactor moves them outside /api/.
      {
        urlPattern: ({ url }) =>
          /\/(receipt|receipts|export|exports|download|downloads)(\/|$)/.test(url.pathname),
        handler: "NetworkOnly",
      },

      // ── 2) NetworkFirst for authenticated HTML routes ──────────────────
      // HTML pages are static shells (Next.js client components). The
      // dashboard's actual data flows through /api/* which is NetworkOnly,
      // so the cached HTML never contains user data. Short TTL keeps the
      // window for serving stale routes minimal.
      {
        urlPattern: ({ request }) => request.destination === "document",
        handler: "NetworkFirst",
        options: {
          cacheName: "ojunai-html",
          networkTimeoutSeconds: 4,
          expiration: { maxEntries: 32, maxAgeSeconds: 60 * 60 }, // 1 hour
        },
      },

      // ── 3) CacheFirst — strict allowlist of safe static assets ─────────
      // Limited to OUR same-origin Next.js build output and brand assets.
      // User uploads, third-party CDN images, etc. are intentionally NOT
      // matched here. They fall through to default network behavior.
      {
        urlPattern: ({ url, request, sameOrigin }) => {
          if (!sameOrigin) return false;
          const p = url.pathname;
          const safePath =
            p.startsWith("/_next/static/") ||
            p.startsWith("/brand/") ||
            p.startsWith("/icons/") ||
            p.startsWith("/splash/") ||
            p === "/favicon.ico" ||
            p === "/favicon.svg" ||
            /^\/favicon-\d+\.png$/.test(p) ||
            p === "/apple-touch-icon.png" ||
            p === "/og-image.png" ||
            p === "/manifest.webmanifest";
          const staticDest =
            request.destination === "image" ||
            request.destination === "font" ||
            request.destination === "style" ||
            request.destination === "script";
          return safePath && staticDest;
        },
        handler: "CacheFirst",
        options: {
          cacheName: "ojunai-static",
          expiration: { maxEntries: 200, maxAgeSeconds: 30 * 24 * 60 * 60 },
        },
      },
    ],
  },
});

export default withPWA(nextConfig);

import withPWAInit from "@ducanh2912/next-pwa";

/** @type {import('next').NextConfig} */
const nextConfig = {};

const withPWA = withPWAInit({
  dest: "public",
  register: true,
  // Hot reload + Next dev cache do not play well with a service worker. Only
  // run the SW in production builds.
  disable: process.env.NODE_ENV === "development",
  // Offline fallback: SW serves /offline whenever a navigation request can't
  // be answered from the network or the runtime cache. Without this, first-
  // time offline visits to uncached routes fail with a blank screen.
  fallbacks: {
    document: "/offline",
  },
  // NetworkFirst for HTML so a fresh deploy reaches users immediately;
  // CacheFirst for fingerprinted static assets. API calls are explicitly
  // excluded so /api/* always hits the network.
  cacheOnFrontEndNav: true,
  aggressiveFrontEndNavCaching: true,
  reloadOnOnline: true,
  workboxOptions: {
    disableDevLogs: true,
    runtimeCaching: [
      {
        urlPattern: ({ request }) => request.destination === "document",
        handler: "NetworkFirst",
        options: {
          cacheName: "ojunai-html",
          networkTimeoutSeconds: 4,
          expiration: { maxEntries: 32, maxAgeSeconds: 24 * 60 * 60 },
        },
      },
      {
        urlPattern: ({ url }) => url.pathname.startsWith("/api/"),
        handler: "NetworkOnly",
      },
      {
        urlPattern: ({ request }) =>
          request.destination === "image" ||
          request.destination === "font" ||
          request.destination === "style" ||
          request.destination === "script",
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

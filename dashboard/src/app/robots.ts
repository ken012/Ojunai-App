import type { MetadataRoute } from "next";

/**
 * robots.txt for app.ojunai.com — Next.js app-router auto-serves this at /robots.txt.
 *
 * Strategy: deny by default, allow only the handful of public pages we want Google
 * to surface. Everything inside the dashboard (today, sales, expenses, etc.) sits
 * behind login and has zero SEO value — surfacing those URLs to bots just wastes
 * crawl budget and risks indexing pages that 401 to anonymous users.
 */
export default function robots(): MetadataRoute.Robots {
  return {
    rules: [
      {
        userAgent: "*",
        // Allow only the public, intentionally-shareable routes. Google's longest-match
        // wins, so these override the global Disallow below for matching paths.
        allow: [
          "/login",
          "/register",
          "/forgot-password",
          "/recover-account",
          "/install",
          "/privacy",
          "/terms",
          "/favicon.ico",
          "/favicon-16.png",
          "/favicon-32.png",
          "/apple-touch-icon.png",
          "/manifest.webmanifest",
          "/brand/",
          "/icons/",
        ],
        disallow: [
          "/",        // catch-all — blocks Today + everything inside (dashboard)
          "/admin/",  // internal admin tooling
          "/api/",    // there are no API routes here, but defensive
          "/recover", // recovery flow, has tokens
          "/verify-email",
          "/change-password",
          "/offline",
        ],
      },
    ],
    sitemap: "https://app.ojunai.com/sitemap.xml",
    host: "https://app.ojunai.com",
  };
}

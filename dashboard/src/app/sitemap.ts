import type { MetadataRoute } from "next";

/**
 * Sitemap for app.ojunai.com — Next.js app-router auto-serves this at /sitemap.xml.
 *
 * Only PUBLIC, indexable pages are listed. Authenticated dashboard routes (sales,
 * inventory, settings, etc.) are intentionally absent — they require login and
 * have no SEO value. They're also blocked in robots.ts.
 */
export default function sitemap(): MetadataRoute.Sitemap {
  const base = "https://app.ojunai.com";
  const now = new Date();

  return [
    { url: `${base}/login`,            lastModified: now, changeFrequency: "monthly", priority: 0.8 },
    { url: `${base}/register`,         lastModified: now, changeFrequency: "monthly", priority: 0.7 },
    { url: `${base}/forgot-password`,  lastModified: now, changeFrequency: "yearly",  priority: 0.3 },
    { url: `${base}/recover-account`,  lastModified: now, changeFrequency: "yearly",  priority: 0.3 },
    { url: `${base}/install`,          lastModified: now, changeFrequency: "monthly", priority: 0.5 },
    { url: `${base}/privacy`,          lastModified: now, changeFrequency: "yearly",  priority: 0.3 },
    { url: `${base}/terms`,            lastModified: now, changeFrequency: "yearly",  priority: 0.3 },
  ];
}

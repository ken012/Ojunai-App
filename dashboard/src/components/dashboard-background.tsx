"use client";

import { useBusiness } from "@/lib/data-sync";
import { absoluteApiUrl } from "@/lib/api";

/**
 * Custom dashboard background image (Pro/Business plans).
 * Renders absolutely-positioned layers inside the dashboard's <main>:
 *   1. The image itself (fills the visible viewport area, doesn't scroll with content)
 *   2. A theme-aware overlay at user-controlled opacity — keeps text legible
 * Both sit at -z-10 so all dashboard content renders on top.
 *
 * No-op when the business hasn't uploaded an image. Renders nothing on the server
 * or before DataSync hydrates so there's no flash-of-default-then-image.
 */
export function DashboardBackground() {
  const business = useBusiness();
  const imageUrl = absoluteApiUrl(business?.backgroundImageUrl);
  if (!imageUrl) return null;

  // Opacity is the OVERLAY opacity (0 = image fully visible, 1 = overlay fully opaque).
  // Default 0.85 keeps text legible while letting the image peek through.
  const overlayOpacity = business?.backgroundImageOpacity ?? 0.85;

  return (
    <>
      <div
        aria-hidden
        className="absolute inset-0 -z-10 bg-cover bg-center bg-no-repeat pointer-events-none"
        style={{ backgroundImage: `url(${imageUrl})` }}
      />
      <div
        aria-hidden
        className="absolute inset-0 -z-10 bg-white dark:bg-slate-950 pointer-events-none transition-opacity"
        style={{ opacity: overlayOpacity }}
      />
    </>
  );
}

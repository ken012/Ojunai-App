import type { Metadata, Viewport } from "next";
import { Inter } from "next/font/google";
import "./globals.css";
import { Providers } from "./providers";
import { themeBootScript } from "@/lib/theme";

const inter = Inter({ subsets: ["latin"] });

export const metadata: Metadata = {
  title: "Ojunai",
  description: "The eye that never blinks. The AI that runs your shop's books on WhatsApp.",
  manifest: "/manifest.webmanifest",
  applicationName: "Ojunai",
  appleWebApp: {
    capable: true,
    title: "Ojunai",
    statusBarStyle: "black",
  },
  icons: {
    icon: [
      { url: "/favicon.ico", sizes: "any" },
      { url: "/favicon-32.png", sizes: "32x32", type: "image/png" },
      { url: "/favicon-16.png", sizes: "16x16", type: "image/png" },
    ],
    apple: [
      { url: "/apple-touch-icon.png", sizes: "180x180", type: "image/png" },
    ],
  },
  verification: {
    google: "8-npwdTxdHqH0oAP7Nii75oolo1dgozM1oKUTwRJGR0",
  },
};

export const viewport: Viewport = {
  themeColor: [
    { media: "(prefers-color-scheme: light)", color: "#FFFFFF" },
    { media: "(prefers-color-scheme: dark)", color: "#0F172A" },
  ],
  viewportFit: "cover",
  width: "device-width",
  initialScale: 1,
};

// iOS splash screens — Next's Metadata API has no first-class field for
// media-queried apple-touch-startup-image, so these stay as raw <link> tags.
const APPLE_SPLASH_SCREENS: Array<{ width: number; height: number; ratio: number; src: string }> = [
  { width: 430, height: 932, ratio: 3, src: "/splash/splash-1290x2796.png" }, // iPhone 14/15/16 Pro Max
  { width: 428, height: 926, ratio: 3, src: "/splash/splash-1284x2778.png" }, // iPhone 12-15 Plus
  { width: 414, height: 896, ratio: 3, src: "/splash/splash-1242x2688.png" }, // iPhone XS Max / 11 Pro Max
  { width: 414, height: 896, ratio: 2, src: "/splash/splash-828x1792.png"  }, // iPhone XR / 11
  { width: 393, height: 852, ratio: 3, src: "/splash/splash-1179x2556.png" }, // iPhone 14/15/16 Pro
  { width: 390, height: 844, ratio: 3, src: "/splash/splash-1170x2532.png" }, // iPhone 12/13/14
  { width: 375, height: 812, ratio: 3, src: "/splash/splash-1125x2436.png" }, // iPhone X / XS / 11 Pro
  { width: 375, height: 667, ratio: 2, src: "/splash/splash-750x1334.png"  }, // iPhone SE 2/3
  { width: 1024, height: 1366, ratio: 2, src: "/splash/splash-2048x2732.png" }, // iPad Pro 12.9"
  { width: 834,  height: 1194, ratio: 2, src: "/splash/splash-1668x2388.png" }, // iPad Pro 11"
  { width: 768,  height: 1024, ratio: 2, src: "/splash/splash-1536x2048.png" }, // iPad
];

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en" suppressHydrationWarning>
      <head>
        {/* Run before paint to set the html.dark class — prevents flash-of-wrong-theme. */}
        <script dangerouslySetInnerHTML={{ __html: themeBootScript }} />
        {APPLE_SPLASH_SCREENS.map((s) => (
          <link
            key={s.src}
            rel="apple-touch-startup-image"
            href={s.src}
            media={`(device-width: ${s.width}px) and (device-height: ${s.height}px) and (-webkit-device-pixel-ratio: ${s.ratio}) and (orientation: portrait)`}
          />
        ))}
      </head>
      <body className={inter.className}>
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}

#!/usr/bin/env node
// Generate PWA maskable icons + iOS splash screens from the brand pack.
// Re-run after a logo change. Reads from public/brand/, writes to public/icons/ and public/splash/.

import sharp from "sharp";
import { copyFile, mkdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(__dirname, "..");
const PUBLIC = path.join(root, "public");
const SOURCE = path.join(PUBLIC, "brand/icon-1024.png");
const ICONS_OUT = path.join(PUBLIC, "icons");
const SPLASH_OUT = path.join(PUBLIC, "splash");

// Brand dark — splash background and maskable backfill.
const BG = { r: 0x0b, g: 0x0b, b: 0x1f, alpha: 1 };

await mkdir(ICONS_OUT, { recursive: true });
await mkdir(SPLASH_OUT, { recursive: true });

// Home-screen icon — composite the real brand logo (public/brand/icon-1024.png)
// onto a white canvas at the given scale. White bg matches the App Store norm
// (Excalidraw, Notion, Slack) and lets the brand's dark rounded shell pop. The
// brand logo IS the icon — no SVG recreation, no synthetic eye. The platform
// rounds the outer corners (iOS) or applies its launcher mask (Android), so we
// keep the brand at 90% of the canvas with a small white border for breathing
// room. For Android maskable variants we drop to 78% so the brand stays inside
// the central 80% safe zone after the launcher mask cuts the corners off.
const WHITE = { r: 255, g: 255, b: 255, alpha: 1 };

async function compositeBrandOnWhite(canvasSize, brandScale) {
  const brandPx = Math.round(canvasSize * brandScale);
  const offset = Math.round((canvasSize - brandPx) / 2);
  // Use the highest-quality source, downscale with Lanczos3 for crispness, then
  // sharpen and lift saturation slightly so the brand reads sharp at home-icon
  // sizes. These are pure image-processing tweaks — the design is untouched.
  const brandBuf = await sharp(SOURCE)
    .resize(brandPx, brandPx, { kernel: sharp.kernel.lanczos3 })
    .modulate({ saturation: 1.08 })
    .sharpen({ sigma: 0.6 })
    .png({ compressionLevel: 9 })
    .toBuffer();
  return sharp({ create: { width: canvasSize, height: canvasSize, channels: 4, background: WHITE } })
    .composite([{ input: brandBuf, top: offset, left: offset }])
    .png({ compressionLevel: 9 });
}

async function makeMaskable(size) {
  // 0.86: brand fills enough of the canvas that the icon doesn't read as
  // padded, while keeping the eye glyph (the part that absolutely cannot be
  // clipped) safely inside Android's 80% maskable safe zone. The brand's own
  // rounded corners may be nibbled by tight launcher masks (squircle/circle)
  // but that just blends with the platform shape — the rim curve and the eye
  // both stay visible.
  await (await compositeBrandOnWhite(size, 0.86))
    .toFile(path.join(ICONS_OUT, `icon-maskable-${size}.png`));
}

// iOS splash: device-sized canvas filled with brand dark, icon centered at
// ~28% of the shorter dimension. Portrait orientation only — landscape splash
// is rarely shown for utility apps and adds 9 more files for little gain.
const SPLASH_SIZES = [
  { w: 2048, h: 2732 }, // iPad Pro 12.9"
  { w: 1668, h: 2388 }, // iPad Pro 11"
  { w: 1536, h: 2048 }, // iPad
  { w: 1290, h: 2796 }, // iPhone 15/16 Pro Max
  { w: 1284, h: 2778 }, // iPhone 12/13/14 Plus, 14/15 Plus
  { w: 1242, h: 2688 }, // iPhone XS Max / 11 Pro Max
  { w: 1179, h: 2556 }, // iPhone 15/16 Pro
  { w: 1170, h: 2532 }, // iPhone 13/14
  { w: 1125, h: 2436 }, // iPhone X / XS / 11 Pro
  { w: 828,  h: 1792 }, // iPhone XR / 11
  { w: 750,  h: 1334 }, // iPhone SE
];

async function makeSplash({ w, h }) {
  const iconSize = Math.round(Math.min(w, h) * 0.28);
  const iconBuf = await sharp(SOURCE).resize(iconSize, iconSize).png().toBuffer();
  const top = Math.round((h - iconSize) / 2);
  const left = Math.round((w - iconSize) / 2);
  await sharp({ create: { width: w, height: h, channels: 4, background: BG } })
    .composite([{ input: iconBuf, top, left }])
    .png()
    .toFile(path.join(SPLASH_OUT, `splash-${w}x${h}.png`));
}

// apple-touch-icon: iOS home-screen icon after Add-to-Home-Screen. iOS only
// rounds the outer corners — there's no aggressive cropping — so the brand
// sits at 0.97 of the canvas. The 3% white margin is just enough that iOS's
// corner radius doesn't bite into the brand's own rounded rim.
async function makeAppleTouchIcon() {
  await (await compositeBrandOnWhite(180, 0.97))
    .toFile(path.join(PUBLIC, "apple-touch-icon.png"));
}

// Browser-tab favicons. Source PNGs from the brand pack at the right sizes
// already exist — copy them directly so the design matches everywhere.
async function copyFavicons() {
  await Promise.all([
    copyFile(path.join(PUBLIC, "brand/icon-32.png"), path.join(PUBLIC, "favicon-32.png")),
    copyFile(path.join(PUBLIC, "brand/icon-16.png"), path.join(PUBLIC, "favicon-16.png")),
  ]);
}

await Promise.all([
  makeMaskable(192),
  makeMaskable(512),
  makeAppleTouchIcon(),
  copyFavicons(),
  ...SPLASH_SIZES.map(makeSplash),
]);

console.log("PWA assets generated:");
console.log(`  ${ICONS_OUT}/icon-maskable-{192,512}.png`);
console.log(`  ${SPLASH_OUT}/splash-*.png (${SPLASH_SIZES.length} sizes)`);
console.log(`  ${PUBLIC}/apple-touch-icon.png (180×180)`);
console.log(`  ${PUBLIC}/favicon-{16,32}.png`);

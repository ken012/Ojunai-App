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

// Detect the brand's tight bounding box using the alpha channel (the source
// has fully-transparent corners around the rounded shell). Computed once.
async function findBrandBboxAlpha() {
  const { data, info } = await sharp(SOURCE).raw().toBuffer({ resolveWithObject: true });
  let minX = info.width, maxX = 0, minY = info.height, maxY = 0;
  for (let y = 0; y < info.height; y++) {
    for (let x = 0; x < info.width; x++) {
      const a = data[(y * info.width + x) * info.channels + 3];
      if (a > 50) {
        if (x < minX) minX = x;
        if (x > maxX) maxX = x;
        if (y < minY) minY = y;
        if (y > maxY) maxY = y;
      }
    }
  }
  return { left: minX, top: minY, width: maxX - minX + 1, height: maxY - minY + 1 };
}
const BRAND_BBOX = await findBrandBboxAlpha();

// Home-screen icon — crop the source to its tight content bbox so the brand
// fills the full canvas, then composite on dark. The brand's transparent
// rounded corners let the dark canvas show through, so iOS's outer rounding
// blends seamlessly with the brand's own rounded shape.
async function compositeBrand(canvasSize, brandScale) {
  const brandPx = Math.round(canvasSize * brandScale);
  const offset = Math.round((canvasSize - brandPx) / 2);
  const brandBuf = await sharp(SOURCE)
    .extract(BRAND_BBOX)
    .resize(brandPx, brandPx, { kernel: sharp.kernel.lanczos3, fit: "cover", position: "center" })
    .modulate({ saturation: 1.05 })
    .sharpen({ sigma: 0.5 })
    .png({ compressionLevel: 9 })
    .toBuffer();
  return sharp({ create: { width: canvasSize, height: canvasSize, channels: 4, background: BG } })
    .composite([{ input: brandBuf, top: offset, left: offset }])
    .png({ compressionLevel: 9 });
}

async function makeMaskable(size) {
  // Scale 1.0: brand fills the canvas. Since the canvas is the same dark color
  // as the brand's interior, Android's launcher mask just cuts dark — the eye
  // and rim are both well inside the 80% safe zone.
  await (await compositeBrand(size, 1.0))
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
  // Reuse compositeBrand to get the masked, dark-canvas brand at the splash's
  // icon size, then place that on a full-size dark splash canvas.
  const iconSize = Math.round(Math.min(w, h) * 0.28);
  const iconBuf = await (await compositeBrand(iconSize, 1.0)).toBuffer();
  const top = Math.round((h - iconSize) / 2);
  const left = Math.round((w - iconSize) / 2);
  await sharp({ create: { width: w, height: h, channels: 4, background: BG } })
    .composite([{ input: iconBuf, top, left }])
    .png()
    .toFile(path.join(SPLASH_OUT, `splash-${w}x${h}.png`));
}

// apple-touch-icon: iOS home-screen icon. Dark canvas matches the brand
// interior, so the brand at scale 1.0 fills naturally — no margin, no frame.
async function makeAppleTouchIcon() {
  await (await compositeBrand(180, 1.0))
    .toFile(path.join(PUBLIC, "apple-touch-icon.png"));
}

// Regenerate all sized brand variants used elsewhere (LogoMark in the dashboard
// sidebar, login page, etc.) from the canonical 1024 source so a single brand
// change cascades everywhere. Each variant is the brand resized with Lanczos3
// for crispness; transparent corners are preserved (no white fill) so the
// LogoMark blends with whatever surface it sits on.
const BRAND_SIZES = [16, 32, 64, 96, 128, 192, 256, 512];
async function regenerateBrandVariants() {
  await Promise.all(
    BRAND_SIZES.map((size) =>
      sharp(SOURCE)
        .resize(size, size, { kernel: sharp.kernel.lanczos3, fit: "contain", background: { r: 0, g: 0, b: 0, alpha: 0 } })
        .png({ compressionLevel: 9 })
        .toFile(path.join(PUBLIC, `brand/icon-${size}.png`))
    )
  );
}

// Browser-tab favicons. Source PNGs from the brand pack at the right sizes
// already exist — copy them directly so the design matches everywhere.
async function copyFavicons() {
  await Promise.all([
    copyFile(path.join(PUBLIC, "brand/icon-32.png"), path.join(PUBLIC, "favicon-32.png")),
    copyFile(path.join(PUBLIC, "brand/icon-16.png"), path.join(PUBLIC, "favicon-16.png")),
  ]);
}

// favicon.ico — a single 32×32 PNG embedded in an ICO container. Modern
// browsers (Vista+) accept PNG-format ICOs. Hand-written ICO header so we
// don't need a separate npm dep.
async function makeFaviconIco() {
  const png = await sharp(path.join(PUBLIC, "brand/icon-32.png"))
    .resize(32, 32, { kernel: sharp.kernel.lanczos3 })
    .png({ compressionLevel: 9 })
    .toBuffer();
  const header = Buffer.alloc(6);
  header.writeUInt16LE(0, 0);              // reserved
  header.writeUInt16LE(1, 2);              // type: 1 = ICO
  header.writeUInt16LE(1, 4);              // image count
  const entry = Buffer.alloc(16);
  entry.writeUInt8(32, 0);                 // width
  entry.writeUInt8(32, 1);                 // height
  entry.writeUInt8(0, 2);                  // color count (0 for true color)
  entry.writeUInt8(0, 3);                  // reserved
  entry.writeUInt16LE(1, 4);               // color planes
  entry.writeUInt16LE(32, 6);              // bits per pixel
  entry.writeUInt32LE(png.length, 8);      // PNG data size
  entry.writeUInt32LE(22, 12);             // PNG data offset (6 + 16 = 22)
  await import("node:fs/promises").then((fs) =>
    fs.writeFile(path.join(PUBLIC, "favicon.ico"), Buffer.concat([header, entry, png]))
  );
}

// og-image.png — 1200×630 social-share card. Dark brand canvas + the icon +
// "Ojunai" wordmark + tagline. Used by Open Graph / Twitter Card meta tags
// when ojunai.com is shared.
async function makeOgImage() {
  const W = 1200, H = 630;
  const ICON_SIZE = 200;
  const iconBuf = await sharp(SOURCE)
    .resize(ICON_SIZE, ICON_SIZE, { kernel: sharp.kernel.lanczos3 })
    .png()
    .toBuffer();
  const svg = Buffer.from(`<svg width="${W}" height="${H}" xmlns="http://www.w3.org/2000/svg">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="${W}" y2="${H}" gradientUnits="userSpaceOnUse">
      <stop offset="0%" stop-color="#0F172A"/>
      <stop offset="100%" stop-color="#0B0B1F"/>
    </linearGradient>
  </defs>
  <rect width="${W}" height="${H}" fill="url(#bg)"/>
  <text x="${W / 2}" y="460" font-family="-apple-system, system-ui, sans-serif" font-size="80" font-weight="700" fill="#FFFFFF" text-anchor="middle">Ojunai</text>
  <text x="${W / 2}" y="510" font-family="-apple-system, system-ui, sans-serif" font-size="28" font-weight="400" fill="#94A3B8" text-anchor="middle">The eye that never blinks.</text>
</svg>`);
  await sharp(svg)
    .composite([{ input: iconBuf, top: 130, left: Math.round((W - ICON_SIZE) / 2) }])
    .png({ compressionLevel: 9 })
    .toFile(path.join(PUBLIC, "og-image.png"));
}

// Brand variants must regenerate first because copyFavicons() reads from them.
await regenerateBrandVariants();

await Promise.all([
  makeMaskable(192),
  makeMaskable(512),
  makeAppleTouchIcon(),
  copyFavicons(),
  makeFaviconIco(),
  makeOgImage(),
  ...SPLASH_SIZES.map(makeSplash),
]);

console.log("PWA assets generated:");
console.log(`  ${ICONS_OUT}/icon-maskable-{192,512}.png`);
console.log(`  ${SPLASH_OUT}/splash-*.png (${SPLASH_SIZES.length} sizes)`);
console.log(`  ${PUBLIC}/apple-touch-icon.png (180×180)`);
console.log(`  ${PUBLIC}/favicon-{16,32}.png`);

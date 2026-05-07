"use client";

/**
 * Ojunai brand logo. Uses the canonical PNG raster from /public/brand/ — the
 * exact same icon used on ojunai.com. No inline SVG, no recreation, no tricks.
 */
export function LogoMark({
  size = "md",
  showWordmark = true,
  className = "",
  wordmarkColor = "currentColor",
}: {
  size?: "sm" | "md" | "lg";
  showWordmark?: boolean;
  className?: string;
  wordmarkColor?: string;
}) {
  const containerPx =
    size === "sm" ? 30 :
    size === "lg" ? 56 :
    36;
  const src1x =
    size === "sm" ? "/brand/icon-64.png" :
    size === "lg" ? "/brand/icon-128.png" :
    "/brand/icon-96.png";
  const src2x =
    size === "sm" ? "/brand/icon-128.png" :
    size === "lg" ? "/brand/icon-256.png" :
    "/brand/icon-192.png";
  const wordmarkSize =
    size === "sm" ? "text-base" :
    size === "lg" ? "text-2xl" :
    "text-lg";

  return (
    <div className={`inline-flex items-center gap-2.5 ${className}`}>
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img
        src={src1x}
        srcSet={`${src1x} 1x, ${src2x} 2x`}
        width={containerPx}
        height={containerPx}
        alt="Ojunai"
        className="flex-shrink-0"
      />
      {showWordmark && (
        <span
          className={`${wordmarkSize} font-bold tracking-tight`}
          style={{ color: wordmarkColor }}
        >
          Ojunai
        </span>
      )}
    </div>
  );
}

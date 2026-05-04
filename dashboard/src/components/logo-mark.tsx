"use client";

import { useId } from "react";

/**
 * Ojunai brand logo — exact replica of the website's favicon.svg / apple-touch-icon.
 * Indigo gradient (#6366F1 → #4338CA), white eye with indigo pupil center.
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
  // Unique gradient ID per render so multiple instances don't conflict
  const gradId = useId();

  const px =
    size === "sm" ? 28 :
    size === "lg" ? 56 :
    36;
  const wordmarkSize =
    size === "sm" ? "text-base" :
    size === "lg" ? "text-2xl" :
    "text-lg";

  return (
    <div className={`inline-flex items-center gap-2.5 ${className}`}>
      <svg
        width={px}
        height={px}
        viewBox="0 0 64 64"
        xmlns="http://www.w3.org/2000/svg"
        aria-label="Ojunai"
        className="flex-shrink-0"
      >
        <defs>
          <linearGradient id={gradId} x1="0" y1="0" x2="1" y2="1">
            <stop offset="0%" stopColor="#6366F1" />
            <stop offset="100%" stopColor="#4338CA" />
          </linearGradient>
        </defs>
        <rect width="64" height="64" rx="14" fill={`url(#${gradId})`} />
        <path
          d="M10 32 C 18 18, 46 18, 54 32 C 46 46, 18 46, 10 32 Z"
          fill="none"
          stroke="#FFFFFF"
          strokeWidth="3.5"
          strokeLinejoin="round"
        />
        <circle cx="32" cy="32" r="7" fill="#FFFFFF" />
        <circle cx="32" cy="32" r="3" fill="#4338CA" />
      </svg>
      {showWordmark && (
        <span
          className={`${wordmarkSize} font-bold tracking-[0.08em]`}
          style={{ color: wordmarkColor }}
        >
          OJUNAI
        </span>
      )}
    </div>
  );
}

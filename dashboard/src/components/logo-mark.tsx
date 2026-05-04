/**
 * Ojunai logo mark — matches the marketing website exactly.
 * Inline SVG eye icon on indigo gradient background + wordmark.
 *
 * Brand colors (from website style.css):
 *   --brand:     #4F46E5  (indigo-600)
 *   --brand-mid: #6366F1  (indigo-500)
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
  const iconClass =
    size === "sm" ? "w-7 h-7 rounded-lg" :
    size === "lg" ? "w-12 h-12 rounded-2xl" :
    "w-9 h-9 rounded-xl";
  const svgSize = size === "sm" ? 16 : size === "lg" ? 28 : 22;
  const wordmarkSize =
    size === "sm" ? "text-base" :
    size === "lg" ? "text-2xl" :
    "text-lg";

  return (
    <div className={`inline-flex items-center gap-2.5 ${className}`}>
      <div
        className={`${iconClass} flex items-center justify-center bg-gradient-to-br from-[#4F46E5] to-[#6366F1] flex-shrink-0`}
        aria-hidden="true"
      >
        <svg
          width={svgSize}
          height={svgSize}
          viewBox="0 0 24 24"
          fill="none"
          stroke="white"
          strokeWidth="2"
        >
          <path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z" />
          <circle cx="12" cy="12" r="3" fill="white" stroke="none" />
        </svg>
      </div>
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

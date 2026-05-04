/**
 * Initials avatar with deterministic brand-tinted background.
 * Used for contacts, staff, etc. — Linear/Notion pattern.
 */

const TINTS = [
  "bg-cyan-100 text-cyan-700",
  "bg-violet-100 text-violet-700",
  "bg-emerald-100 text-emerald-700",
  "bg-amber-100 text-amber-700",
  "bg-rose-100 text-rose-700",
  "bg-indigo-100 text-indigo-700",
  "bg-teal-100 text-teal-700",
  "bg-orange-100 text-orange-700",
];

function tintFor(seed: string): string {
  let h = 0;
  for (let i = 0; i < seed.length; i++) h = (h << 5) - h + seed.charCodeAt(i);
  return TINTS[Math.abs(h) % TINTS.length];
}

function initialsFrom(name: string): string {
  const parts = name.trim().split(/\s+/);
  if (parts.length === 0) return "?";
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
}

export function Avatar({
  name,
  size = "md",
  className = "",
}: {
  name: string;
  size?: "sm" | "md" | "lg";
  className?: string;
}) {
  const sizeClass =
    size === "sm" ? "w-7 h-7 text-[10px]" : size === "lg" ? "w-12 h-12 text-base" : "w-9 h-9 text-xs";
  return (
    <div
      className={`${sizeClass} rounded-full flex items-center justify-center font-semibold tracking-tight flex-shrink-0 ${tintFor(
        name
      )} ${className}`}
      aria-label={name}
    >
      {initialsFrom(name)}
    </div>
  );
}

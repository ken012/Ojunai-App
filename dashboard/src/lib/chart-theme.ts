"use client";

import { useTheme } from "@/lib/theme";

/**
 * Theme-aware tokens for Recharts. Used in place of hardcoded slate hex values
 * (#f1f5f9 grid, #94a3b8 ticks, #475569 strong labels) so charts adapt to
 * dark mode.
 */
export function useChartTheme() {
  const { resolvedTheme } = useTheme();
  const dark = resolvedTheme === "dark";
  return {
    // Faint horizontal/vertical grid lines
    grid: dark ? "#1e293b" : "#f1f5f9", // slate-800 / slate-100 (was #e2e8f0 in some files; close enough)
    // Axis tick text — muted
    tickMuted: dark ? "#64748b" : "#94a3b8", // slate-500 / slate-400
    // Axis tick text — stronger (Y-axis category labels)
    tickStrong: dark ? "#cbd5e1" : "#475569", // slate-300 / slate-600
    // Tooltip surface
    tooltipBg: dark ? "#0f172a" : "#ffffff", // slate-900 / white
    tooltipBorder: dark ? "#334155" : "#e2e8f0", // slate-700 / slate-200
    tooltipText: dark ? "#f1f5f9" : "#0f172a", // slate-100 / slate-900
  };
}

/** Recharts <Tooltip> contentStyle prop wired to current theme. */
export function useTooltipStyle() {
  const t = useChartTheme();
  return {
    backgroundColor: t.tooltipBg,
    border: `1px solid ${t.tooltipBorder}`,
    borderRadius: 8,
    fontSize: 12,
    color: t.tooltipText,
  } as const;
}

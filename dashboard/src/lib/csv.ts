/**
 * Lightweight CSV download helper. No deps.
 * Used by Reports cards (and others) for client-side CSV export.
 */

function escapeField(value: unknown): string {
  if (value === null || value === undefined) return "";
  const s = String(value);
  // Quote if contains comma, quote, or newline; escape internal quotes
  if (/[",\n\r]/.test(s)) {
    return `"${s.replace(/"/g, '""')}"`;
  }
  return s;
}

export function toCsv(headers: string[], rows: (string | number | null | undefined)[][]): string {
  const lines = [headers.map(escapeField).join(",")];
  for (const row of rows) {
    lines.push(row.map(escapeField).join(","));
  }
  return lines.join("\r\n");
}

export function downloadCsv(filename: string, content: string): void {
  // Add BOM so Excel reads UTF-8 correctly
  const blob = new Blob(["﻿" + content], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename.endsWith(".csv") ? filename : `${filename}.csv`;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

/** Convenience wrapper: builds and downloads in one call. */
export function exportCsv(
  filename: string,
  headers: string[],
  rows: (string | number | null | undefined)[][]
): void {
  downloadCsv(filename, toCsv(headers, rows));
}

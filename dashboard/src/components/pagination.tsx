"use client";

import { ChevronLeft, ChevronRight, ChevronsLeft, ChevronsRight } from "lucide-react";

/**
 * Pagination — page number row with first/last shortcuts.
 *
 * Compact, low-noise layout: «  ‹  1 … 4 [5] 6 … 99  ›  »
 *
 * Page-number rendering is windowed: always show first + last, plus current ± 1, with an
 * ellipsis marker where there's a gap. Avoids a 99-button row when there's a lot of data
 * but still lets a user jump directly to a known page they want.
 *
 * Disabled when there's only one page; the parent wraps the render in a totalPages > 1
 * guard typically.
 */
export function Pagination({
  page,
  totalPages,
  onPageChange,
  summary,
}: {
  page: number;
  totalPages: number;
  onPageChange: (newPage: number) => void;
  /** Optional summary text shown to the left (e.g. "473 contacts"). */
  summary?: string;
}) {
  if (totalPages <= 1) return null;

  const numbers = buildPageList(page, totalPages);
  const atFirst = page <= 1;
  const atLast = page >= totalPages;

  function go(p: number) {
    if (p < 1 || p > totalPages || p === page) return;
    onPageChange(p);
  }

  return (
    <div className="flex items-center justify-between gap-3 text-xs text-slate-500 dark:text-slate-400 mt-3 flex-wrap">
      <span className="whitespace-nowrap">
        {summary ?? `Page ${page} of ${totalPages}`}
      </span>
      <div className="flex items-center gap-0.5">
        <PageBtn label="First page" onClick={() => go(1)} disabled={atFirst}>
          <ChevronsLeft size={14} />
        </PageBtn>
        <PageBtn label="Previous page" onClick={() => go(page - 1)} disabled={atFirst}>
          <ChevronLeft size={14} />
        </PageBtn>

        {numbers.map((n, i) =>
          n === "ellipsis" ? (
            <span key={`gap-${i}`} className="px-1.5 text-slate-400 dark:text-slate-500 select-none">…</span>
          ) : (
            <button
              key={n}
              type="button"
              onClick={() => go(n)}
              aria-current={n === page ? "page" : undefined}
              className={`min-w-[28px] h-7 px-2 rounded-md text-xs font-medium tabular-nums transition-colors ${
                n === page
                  ? "bg-sky-600 text-white"
                  : "text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800"
              }`}
            >
              {n}
            </button>
          )
        )}

        <PageBtn label="Next page" onClick={() => go(page + 1)} disabled={atLast}>
          <ChevronRight size={14} />
        </PageBtn>
        <PageBtn label="Last page" onClick={() => go(totalPages)} disabled={atLast}>
          <ChevronsRight size={14} />
        </PageBtn>
      </div>
    </div>
  );
}

function PageBtn({
  children,
  onClick,
  disabled,
  label,
}: {
  children: React.ReactNode;
  onClick: () => void;
  disabled?: boolean;
  label: string;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-label={label}
      className={`min-w-[28px] h-7 px-1.5 rounded-md flex items-center justify-center transition-colors ${
        disabled
          ? "text-slate-300 dark:text-slate-700 cursor-not-allowed"
          : "text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800"
      }`}
    >
      {children}
    </button>
  );
}

/**
 * Pick the page numbers to render. Always includes first, last, and a window around the
 * current page. Returns "ellipsis" markers between non-contiguous numbers so the consumer
 * can render a single … span.
 *
 * Examples (current = 5, total = 12): [1, "ellipsis", 4, 5, 6, "ellipsis", 12]
 * Small cases (total <= 7): just [1, 2, 3, 4, 5, 6, 7] with no ellipsis.
 */
function buildPageList(current: number, total: number): Array<number | "ellipsis"> {
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);

  const wanted = new Set<number>();
  wanted.add(1);
  wanted.add(total);
  wanted.add(current);
  wanted.add(current - 1);
  wanted.add(current + 1);
  // Pad the start / end when we're near them so the visible row stays a similar width.
  if (current <= 3) { wanted.add(2); wanted.add(3); wanted.add(4); }
  if (current >= total - 2) { wanted.add(total - 1); wanted.add(total - 2); wanted.add(total - 3); }

  const sorted = [...wanted]
    .filter(p => p >= 1 && p <= total)
    .sort((a, b) => a - b);

  const result: Array<number | "ellipsis"> = [];
  for (let i = 0; i < sorted.length; i++) {
    result.push(sorted[i]);
    if (i < sorted.length - 1 && sorted[i + 1] - sorted[i] > 1) {
      result.push("ellipsis");
    }
  }
  return result;
}

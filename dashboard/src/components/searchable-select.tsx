"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { Search, ChevronDown, X } from "lucide-react";

/**
 * SearchableSelect — combobox with server-side search.
 *
 * Designed for pickers backed by lists too large to load all at once (products,
 * customers, etc.). The closed trigger looks like an HTML <select>; click it and
 * a popover opens with a search input at the top and a filtered result list below.
 * Typing fires `fetchOptions(query)` with a 250ms debounce so the server only sees
 * one request per pause-in-typing.
 *
 * Why server-side?
 * Loading every option client-side becomes painful past a few thousand rows —
 * slow initial fetch, big JSON payloads on mobile, browser memory pressure.
 * This component scales cleanly to hundreds of thousands of options because the
 * server only ever returns the top N matches.
 *
 * Keyboard support: ArrowUp/ArrowDown to navigate, Enter to select, Escape to
 * close. Click-outside also closes.
 */
export type SearchableOption = {
  /** Stable identifier passed back through `value` / `onChange`. */
  value: string;
  /** Primary text shown in both the trigger and the result list. */
  label: string;
  /** Optional secondary line (e.g. "8 bags in stock"). Rendered smaller. */
  secondary?: string;
};

interface SearchableSelectProps {
  /** Currently-selected value (matches one option's `value`). */
  value: string;
  /**
   * Called with the new value when the user picks an option, or "" if they cleared it.
   * The second argument carries the full picked option so callers can react with metadata
   * (e.g. auto-fill a price when the picked product has a stored selling price).
   */
  onChange: (value: string, option?: SearchableOption) => void;
  /**
   * Fetcher invoked whenever the search text changes (debounced 250ms) AND on first open.
   * Returning an empty array shows the "No matches" placeholder.
   * Initial call passes an empty string — return the most-relevant defaults (e.g. top 20).
   */
  fetchOptions: (search: string) => Promise<SearchableOption[]>;
  /**
   * Looks up the label for the current `value` so the closed trigger can display the
   * user's selection without us having to fetch the whole list. Called only when `value`
   * changes to something we haven't seen via fetchOptions.
   */
  resolveLabel?: (value: string) => Promise<SearchableOption | null>;
  placeholder?: string;
  searchPlaceholder?: string;
  emptyMessage?: string;
  className?: string;
  /** Allow clearing the selection with a × button on the trigger. Default true. */
  allowClear?: boolean;
  disabled?: boolean;
}

const DEBOUNCE_MS = 250;

export function SearchableSelect({
  value,
  onChange,
  fetchOptions,
  resolveLabel,
  placeholder = "Select…",
  searchPlaceholder = "Search",
  emptyMessage = "No matches",
  className = "",
  allowClear = true,
  disabled = false,
}: SearchableSelectProps) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [options, setOptions] = useState<SearchableOption[]>([]);
  const [loading, setLoading] = useState(false);
  // Cached resolution for the selected value's label. Without this, the trigger
  // would show the raw value (a UUID) before the options list loads.
  const [selectedLabel, setSelectedLabel] = useState<SearchableOption | null>(null);
  const [activeIdx, setActiveIdx] = useState(0);

  const triggerRef = useRef<HTMLButtonElement>(null);
  const popoverRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  // Resolve the label of the currently-selected value if it's not already in options.
  // Skipped when value is empty or already in the visible options list.
  useEffect(() => {
    if (!value) { setSelectedLabel(null); return; }
    const fromOptions = options.find(o => o.value === value);
    if (fromOptions) { setSelectedLabel(fromOptions); return; }
    if (selectedLabel?.value === value) return;
    if (!resolveLabel) return;
    let alive = true;
    resolveLabel(value).then(r => { if (alive && r) setSelectedLabel(r); });
    return () => { alive = false; };
  }, [value, options, resolveLabel, selectedLabel?.value]);

  // Debounced server search. Re-runs whenever `query` changes; also fires on open
  // with the current query (typically empty → defaults).
  useEffect(() => {
    if (!open) return;
    let alive = true;
    setLoading(true);
    const handle = setTimeout(async () => {
      try {
        const result = await fetchOptions(query);
        if (alive) setOptions(result);
      } catch {
        if (alive) setOptions([]);
      } finally {
        if (alive) setLoading(false);
      }
    }, query ? DEBOUNCE_MS : 0); // No debounce on empty (initial open) — show defaults instantly.
    return () => { alive = false; clearTimeout(handle); };
  }, [open, query, fetchOptions]);

  // Click-outside closes the popover.
  useEffect(() => {
    if (!open) return;
    function onClick(e: MouseEvent) {
      const target = e.target as Node;
      if (
        popoverRef.current && !popoverRef.current.contains(target) &&
        triggerRef.current && !triggerRef.current.contains(target)
      ) {
        setOpen(false);
      }
    }
    document.addEventListener("mousedown", onClick);
    return () => document.removeEventListener("mousedown", onClick);
  }, [open]);

  // Reset the highlighted row index whenever results change so ArrowUp/Down nav
  // doesn't try to highlight a row that doesn't exist anymore.
  useEffect(() => { setActiveIdx(0); }, [options]);

  const handleOpen = useCallback(() => {
    if (disabled) return;
    setOpen(true);
    setQuery("");
    // Focus the search input on next tick — needs to be after the popover renders.
    setTimeout(() => inputRef.current?.focus(), 10);
  }, [disabled]);

  const handlePick = useCallback((opt: SearchableOption) => {
    onChange(opt.value, opt);
    setSelectedLabel(opt);
    setOpen(false);
  }, [onChange]);

  const handleKey = useCallback((e: React.KeyboardEvent) => {
    if (!open) return;
    if (e.key === "Escape") { setOpen(false); e.preventDefault(); return; }
    if (e.key === "ArrowDown") {
      setActiveIdx(i => Math.min(options.length - 1, i + 1));
      e.preventDefault();
    } else if (e.key === "ArrowUp") {
      setActiveIdx(i => Math.max(0, i - 1));
      e.preventDefault();
    } else if (e.key === "Enter") {
      const choice = options[activeIdx];
      if (choice) handlePick(choice);
      e.preventDefault();
    }
  }, [open, options, activeIdx, handlePick]);

  // Displayed text on the closed trigger. Prefer the cached label; fall back to the
  // placeholder when nothing's picked yet.
  const triggerText = value ? (selectedLabel?.label ?? "…") : placeholder;
  const isPlaceholder = !value;

  return (
    <div className={`relative ${className}`}>
      <button
        ref={triggerRef}
        type="button"
        onClick={handleOpen}
        disabled={disabled}
        className={`w-full h-9 px-2 pr-8 rounded-md border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 text-sm text-left flex items-center justify-between gap-2 ${
          disabled ? "opacity-50 cursor-not-allowed" : "hover:border-slate-300 dark:hover:border-slate-700"
        }`}
      >
        <span className={`truncate ${isPlaceholder ? "text-slate-400 dark:text-slate-500" : "text-slate-900 dark:text-slate-100"}`}>
          {triggerText}
        </span>
        <div className="flex items-center gap-1 flex-shrink-0">
          {allowClear && value && !disabled && (
            <span
              role="button"
              aria-label="Clear selection"
              onClick={(e) => { e.stopPropagation(); onChange(""); setSelectedLabel(null); }}
              className="p-0.5 rounded hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-400 hover:text-slate-600"
            >
              <X size={12} />
            </span>
          )}
          <ChevronDown size={14} className="text-slate-400" />
        </div>
      </button>

      {open && (
        <div
          ref={popoverRef}
          onKeyDown={handleKey}
          className="absolute z-50 mt-1 min-w-full w-max max-w-[28rem] bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-md shadow-lg overflow-hidden"
        >
          <div className="flex items-center gap-2 px-2 py-1.5 border-b border-slate-100 dark:border-slate-800">
            <Search size={14} className="text-slate-400 flex-shrink-0" />
            <input
              ref={inputRef}
              type="text"
              placeholder={searchPlaceholder}
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              className="flex-1 bg-transparent outline-none text-sm text-slate-900 dark:text-slate-100 placeholder:text-slate-400"
            />
            {loading && (
              <span className="text-xs text-slate-400 dark:text-slate-500 italic">…</span>
            )}
          </div>
          <ul className="max-h-64 overflow-y-auto py-1">
            {options.length === 0 && !loading ? (
              <li className="px-3 py-2 text-xs italic text-slate-500 dark:text-slate-400">
                {query ? emptyMessage : "Start typing or pick from the list…"}
              </li>
            ) : (
              options.map((opt, i) => (
                <li key={opt.value}>
                  <button
                    type="button"
                    onClick={() => handlePick(opt)}
                    onMouseEnter={() => setActiveIdx(i)}
                    className={`w-full px-3 py-1.5 text-left flex flex-col gap-0.5 ${
                      i === activeIdx
                        ? "bg-sky-50 dark:bg-sky-950/40 text-sky-900 dark:text-sky-100"
                        : "hover:bg-slate-50 dark:hover:bg-slate-800/60 text-slate-900 dark:text-slate-100"
                    } ${opt.value === value ? "font-medium" : ""}`}
                  >
                    <span className="text-sm truncate">{opt.label}</span>
                    {opt.secondary && (
                      <span className="text-[11px] text-slate-500 dark:text-slate-400 truncate">{opt.secondary}</span>
                    )}
                  </button>
                </li>
              ))
            )}
          </ul>
        </div>
      )}
    </div>
  );
}

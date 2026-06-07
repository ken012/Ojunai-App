"use client";

import { useState, useEffect, useMemo, useRef, useCallback } from "react";
import { useRouter } from "next/navigation";
import { useStickyState } from "@/lib/sticky-state";
import { useQuery } from "@tanstack/react-query";
import { Label } from "@/components/ui/label";
import { Input } from "@/components/ui/input";
import { api } from "@/lib/api";
import { formatNaira } from "@/lib/format";
import type { ActivityFeedDto, PaginatedActivityResult } from "@/lib/types";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/empty-state";
import { Drawer, DrawerHeader, DrawerBody, DrawerFooter } from "@/components/ui/drawer";
import { useToast } from "@/components/toast";
import { SourceBadge } from "@/components/source-badge";
import {
  Search, X, ChevronLeft, ChevronRight, Download, Copy as CopyIcon, ExternalLink,
  Activity as ActivityIcon, Calendar,
  ShoppingCart, Receipt as ReceiptIcon, Package, CheckCircle, AlertTriangle, Wallet,
} from "lucide-react";

// ─── Type taxonomy ──────────────────────────────────────────────────────────
// Each activity type maps to:
//  - icon: color-tinted symbol shown beside the label (recognizable at a glance)
//  - dotColor: matching dot color used in the drawer header
//  - text: text color for the icon
//  - label: short display string
//  - filterGroup: bucket for the top-level type filter ("payment" rolls up payment_received/payment_made)

type TypeMeta = {
  icon: React.ComponentType<{ size?: number; className?: string }>;
  dotColor: string;
  text: string;
  label: string;
  filterGroup: string;
};
const TYPE_META: Record<string, TypeMeta> = {
  sale:             { icon: ShoppingCart,   dotColor: "bg-emerald-500", text: "text-emerald-600 dark:text-emerald-400", label: "Sale",           filterGroup: "sale" },
  sale_voided:      { icon: ShoppingCart,   dotColor: "bg-rose-500",    text: "text-rose-600 dark:text-rose-400",       label: "Sale voided",    filterGroup: "sale" },
  void_event:       { icon: AlertTriangle,  dotColor: "bg-rose-500",    text: "text-rose-600 dark:text-rose-400",       label: "Void",           filterGroup: "sale" },
  expense:          { icon: ReceiptIcon,    dotColor: "bg-orange-500",  text: "text-orange-600 dark:text-orange-400",   label: "Expense",        filterGroup: "expense" },
  expense_voided:   { icon: ReceiptIcon,    dotColor: "bg-rose-500",    text: "text-rose-600 dark:text-rose-400",       label: "Expense voided", filterGroup: "expense" },
  inventory:        { icon: Package,        dotColor: "bg-cyan-500",    text: "text-cyan-600 dark:text-cyan-400",       label: "Inventory",      filterGroup: "inventory" },
  debt_recorded:    { icon: Wallet,         dotColor: "bg-violet-500",  text: "text-violet-600 dark:text-violet-400",   label: "Debt",           filterGroup: "payment" },
  payment_received: { icon: CheckCircle,    dotColor: "bg-emerald-500", text: "text-emerald-600 dark:text-emerald-400", label: "Payment in",     filterGroup: "payment" },
  payment_made:     { icon: CheckCircle,    dotColor: "bg-orange-500",  text: "text-orange-600 dark:text-orange-400",   label: "Payment out",    filterGroup: "payment" },
  adjustment:       { icon: ActivityIcon,   dotColor: "bg-amber-500",   text: "text-amber-600 dark:text-amber-400",     label: "Adjustment",     filterGroup: "adjustment" },
};
function metaFor(type: string): TypeMeta {
  return TYPE_META[type] ?? { icon: ActivityIcon, dotColor: "bg-slate-400", text: "text-slate-500 dark:text-slate-400", label: type, filterGroup: "other" };
}

const FILTER_TABS: { id: string; label: string }[] = [
  { id: "all",        label: "All" },
  { id: "sale",       label: "Sales" },
  { id: "expense",    label: "Expenses" },
  { id: "inventory",  label: "Inventory" },
  { id: "payment",    label: "Payments" },
  { id: "adjustment", label: "Adjustments" },
];

const PAGE_SIZE = 50;

// ─── Date grouping ──────────────────────────────────────────────────────────
// Renders sticky day headers above each group. "Today / Yesterday / day-of-week
// (within 7 days) / Full date" — same hierarchy Stripe and Linear use.

function dayKey(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}
function dayLabel(d: Date): string {
  const now = new Date();
  const today = dayKey(now);
  const yesterday = new Date(now); yesterday.setDate(yesterday.getDate() - 1);
  const k = dayKey(d);
  if (k === today) return "Today";
  if (k === dayKey(yesterday)) return "Yesterday";
  const diffDays = Math.floor((+now - +d) / 86400000);
  if (diffDays >= 0 && diffDays < 7) {
    return d.toLocaleDateString(undefined, { weekday: "long" });
  }
  return d.toLocaleDateString(undefined, { weekday: "short", month: "short", day: "numeric", year: now.getFullYear() === d.getFullYear() ? undefined : "numeric" });
}
type DayGroup = { key: string; label: string; date: Date; items: ActivityFeedDto[] };
function groupByDay(items: ActivityFeedDto[]): DayGroup[] {
  const groups = new Map<string, DayGroup>();
  for (const item of items) {
    const d = new Date(item.createdAtUtc);
    const k = dayKey(d);
    let g = groups.get(k);
    if (!g) {
      g = { key: k, label: dayLabel(d), date: new Date(d.getFullYear(), d.getMonth(), d.getDate()), items: [] };
      groups.set(k, g);
    }
    g.items.push(item);
  }
  return Array.from(groups.values()).sort((a, b) => +b.date - +a.date);
}

function formatTime(d: Date): string {
  return d.toLocaleTimeString(undefined, { hour: "2-digit", minute: "2-digit", hour12: false });
}

// ─── CSV export ─────────────────────────────────────────────────────────────
function exportToCsv(items: ActivityFeedDto[]) {
  const headers = ["Time", "Type", "Ref", "Description", "Details", "Contact", "Source", "Recorded by", "Amount"];
  const rows = items.map((it) => [
    new Date(it.createdAtUtc).toISOString(),
    metaFor(it.type).label,
    it.refId ?? "",
    it.description ?? "",
    it.details ?? "",
    it.contactName ?? "",
    it.source ?? "",
    it.recordedBy ?? "",
    it.amount != null ? String(it.amount) : "",
  ]);
  const csv = [headers, ...rows]
    .map((r) =>
      r.map((cell) => {
        const s = String(cell ?? "");
        return s.includes(",") || s.includes('"') || s.includes("\n") ? `"${s.replace(/"/g, '""')}"` : s;
      }).join(",")
    )
    .join("\n");
  const blob = new Blob(["﻿" + csv], { type: "text/csv;charset=utf-8;" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `activity-${new Date().toISOString().slice(0, 10)}.csv`;
  document.body.appendChild(a); a.click(); a.remove();
  URL.revokeObjectURL(url);
}

// ─── Page ───────────────────────────────────────────────────────────────────

export default function ActivityPage() {
  const router = useRouter();
  const { toast } = useToast();
  const [typeFilter, setTypeFilter] = useStickyState<string>("activity-type-filter", "all");
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  // Default the feed to the last 90 days so the landing view sends a bounded date window to the
  // API instead of pulling the business's entire history into memory (the backend pushes this
  // window into SQL). Older entries stay reachable: change the start date, or "Clear filters"
  // (which empties the dates) to load all time on demand.
  const [startDate, setStartDate] = useState(() => {
    const d = new Date();
    d.setDate(d.getDate() - 90);
    return d.toISOString().slice(0, 10);
  });
  const [endDate, setEndDate] = useState("");
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<ActivityFeedDto | null>(null);
  const [focusedIdx, setFocusedIdx] = useState<number>(-1);

  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search.trim()), 300);
    return () => clearTimeout(t);
  }, [search]);

  // Reset to page 1 when filters change
  useEffect(() => { setPage(1); setFocusedIdx(-1); }, [typeFilter, debouncedSearch, startDate, endDate]);

  const { data, isLoading } = useQuery({
    queryKey: ["activity-feed", typeFilter, page, debouncedSearch, startDate, endDate],
    queryFn: async () => {
      const params = new URLSearchParams();
      params.set("page", String(page));
      params.set("pageSize", String(PAGE_SIZE));
      if (typeFilter !== "all") params.set("type", typeFilter);
      if (debouncedSearch) params.set("search", debouncedSearch);
      if (startDate) params.set("startDate", startDate);
      if (endDate) params.set("endDate", endDate);
      const { data } = await api.get<{ data: PaginatedActivityResult }>(`/dashboard/activity?${params}`);
      return data.data!;
    },
  });

  const items = useMemo(() => data?.items ?? [], [data?.items]);
  const groups = useMemo(() => groupByDay(items), [items]);
  const totalPages = data?.totalPages ?? 1;
  const totalCount = data?.totalCount ?? 0;
  const hasFilters = typeFilter !== "all" || !!debouncedSearch || !!startDate || !!endDate;

  // Flat list of items in display order — used by keyboard nav
  const flat = useMemo(() => groups.flatMap((g) => g.items), [groups]);

  // Keyboard navigation: j/k and ArrowDown/ArrowUp move focus, Enter opens detail.
  // Don't fire while typing in inputs.
  const onKey = useCallback((e: KeyboardEvent) => {
    const t = e.target as HTMLElement;
    if (t.tagName === "INPUT" || t.tagName === "TEXTAREA" || t.isContentEditable) return;
    if (selected) return; // drawer owns focus
    if (e.key === "ArrowDown" || e.key === "j") {
      e.preventDefault();
      setFocusedIdx((i) => Math.min(i + 1, flat.length - 1));
    } else if (e.key === "ArrowUp" || e.key === "k") {
      e.preventDefault();
      setFocusedIdx((i) => Math.max(i - 1, 0));
    } else if (e.key === "Enter" && focusedIdx >= 0 && focusedIdx < flat.length) {
      e.preventDefault();
      setSelected(flat[focusedIdx]);
    }
  }, [flat, focusedIdx, selected]);

  useEffect(() => {
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onKey]);

  function clearFilters() {
    setTypeFilter("all");
    setSearch("");
    setStartDate("");
    setEndDate("");
  }

  return (
    <div className="space-y-5">
      {/* Page header */}
      <div className="flex items-end justify-between gap-3 flex-wrap">
        <div>
          <h2 className="text-2xl font-bold text-slate-900 dark:text-slate-50 tracking-tight">Activity log</h2>
          <p className="text-slate-500 dark:text-slate-400 text-sm mt-0.5">
            Complete record of every transaction and action — searchable, exportable, audit-ready.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => {
              if (items.length === 0) { toast.info("Nothing to export"); return; }
              exportToCsv(items);
              toast.success("Exported", `${items.length} event${items.length === 1 ? "" : "s"} downloaded as CSV`);
            }}
            className="gap-1.5"
          >
            <Download size={14} /> Export CSV
          </Button>
        </div>
      </div>

      {/* Filter bar — sticks to top while scrolling so filter context is always visible */}
      <div className="sticky top-0 z-10 -mx-4 sm:-mx-6 lg:-mx-8 px-4 sm:px-6 lg:px-8 py-3 bg-slate-50/80 dark:bg-slate-950/80 backdrop-blur-sm border-b border-slate-200/80 dark:border-slate-800/80">
        <div className="space-y-3">
          {/* Type segmented control */}
          <div className="overflow-x-auto -mx-1 px-1">
            <div className="inline-flex items-center gap-1 bg-slate-100 dark:bg-slate-800 p-1 rounded-lg">
              {FILTER_TABS.map((t) => {
                const active = typeFilter === t.id;
                return (
                  <button
                    key={t.id}
                    onClick={() => setTypeFilter(t.id)}
                    className={`px-3 py-1.5 rounded-md text-sm font-medium whitespace-nowrap transition-colors ${
                      active
                        ? "bg-white dark:bg-slate-900 text-slate-900 dark:text-slate-50 shadow-sm ring-1 ring-slate-200 dark:ring-slate-700"
                        : "text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-50"
                    }`}
                  >
                    {t.label}
                  </button>
                );
              })}
            </div>
          </div>

          {/* Search + date range */}
          <div className="flex items-center gap-3 flex-wrap">
            <div className="relative w-full sm:max-w-xs">
              <Search size={14} className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400 dark:text-slate-500 pointer-events-none" />
              <Input
                type="search"
                placeholder="Search ref, contact, staff, description..."
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                className="pl-9 pr-9 h-9"
              />
              {search && (
                <button onClick={() => setSearch("")} className="absolute right-2 top-1/2 -translate-y-1/2 text-slate-400 dark:text-slate-500 hover:text-slate-700 dark:hover:text-slate-300 p-1 rounded" type="button">
                  <X size={14} />
                </button>
              )}
            </div>
            <div className="flex items-center gap-2">
              <Calendar size={14} className="text-slate-400 dark:text-slate-500" />
              <Label className="text-xs text-slate-500 dark:text-slate-400 whitespace-nowrap sr-only">From</Label>
              <Input type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} className="h-9 w-[140px] text-xs" aria-label="Start date" />
              <span className="text-xs text-slate-400 dark:text-slate-500">→</span>
              <Input type="date" value={endDate} onChange={(e) => setEndDate(e.target.value)} className="h-9 w-[140px] text-xs" aria-label="End date" />
            </div>
            {hasFilters && (
              <button onClick={clearFilters} className="text-xs text-cyan-600 dark:text-cyan-400 hover:underline whitespace-nowrap">
                Clear filters
              </button>
            )}
            <span className="ml-auto text-xs text-slate-500 dark:text-slate-400 tabular-nums">
              {totalCount.toLocaleString()} event{totalCount === 1 ? "" : "s"}
            </span>
          </div>
        </div>
      </div>

      {/* Audit table */}
      <Card>
        <CardContent className="p-0">
          {isLoading ? (
            <div className="p-4 space-y-2">
              {Array.from({ length: 12 }).map((_, i) => <Skeleton key={i} className="h-9 rounded" />)}
            </div>
          ) : groups.length === 0 ? (
            <div className="p-8">
              <EmptyState
                icon={<ActivityIcon size={20} />}
                title={hasFilters ? "No activity matches your filters" : "No activity yet"}
                description={hasFilters ? "Try clearing filters or adjusting the date range." : "Sales, expenses, stock changes, and payments will appear here as they happen."}
                action={hasFilters ? <Button variant="outline" onClick={clearFilters}>Clear filters</Button> : undefined}
              />
            </div>
          ) : (
            <ActivityTable
              groups={groups}
              flat={flat}
              focusedIdx={focusedIdx}
              setFocusedIdx={setFocusedIdx}
              onSelect={setSelected}
            />
          )}
        </CardContent>
      </Card>

      {/* Pagination */}
      {totalPages > 1 && (
        <div className="flex items-center justify-between gap-3 flex-wrap">
          <p className="text-xs text-slate-500 dark:text-slate-400 tabular-nums">
            Page {page} of {totalPages} · showing {items.length.toLocaleString()} of {totalCount.toLocaleString()}
          </p>
          <div className="flex items-center gap-1">
            <Button variant="outline" size="sm" onClick={() => setPage(1)} disabled={page <= 1} className="text-xs px-2">
              First
            </Button>
            <Button variant="outline" size="sm" onClick={() => setPage((p) => p - 1)} disabled={page <= 1}>
              <ChevronLeft size={14} />
            </Button>
            {Array.from({ length: Math.min(5, totalPages) }, (_, i) => {
              let n: number;
              if (totalPages <= 5) n = i + 1;
              else if (page <= 3) n = i + 1;
              else if (page >= totalPages - 2) n = totalPages - 4 + i;
              else n = page - 2 + i;
              return (
                <Button
                  key={n}
                  variant={n === page ? "default" : "outline"}
                  size="sm"
                  onClick={() => setPage(n)}
                  className={`text-xs w-8 ${n === page ? "bg-cyan-500 text-white" : ""}`}
                >
                  {n}
                </Button>
              );
            })}
            <Button variant="outline" size="sm" onClick={() => setPage((p) => p + 1)} disabled={page >= totalPages}>
              <ChevronRight size={14} />
            </Button>
            <Button variant="outline" size="sm" onClick={() => setPage(totalPages)} disabled={page >= totalPages} className="text-xs px-2">
              Last
            </Button>
          </div>
        </div>
      )}

      {/* Detail drawer */}
      <DetailDrawer
        item={selected}
        onClose={() => setSelected(null)}
        onJumpToSource={(item) => {
          setSelected(null);
          if (item.type.startsWith("sale")) router.push("/sales");
          else if (item.type.startsWith("expense")) router.push("/expenses");
          else if (item.type === "inventory") router.push("/inventory");
          else if (item.type.includes("payment") || item.type.includes("debt") || item.type === "adjustment") router.push("/contacts");
        }}
      />
    </div>
  );
}

// ─── Table ──────────────────────────────────────────────────────────────────

function ActivityTable({
  groups, flat, focusedIdx, setFocusedIdx, onSelect,
}: {
  groups: DayGroup[];
  flat: ActivityFeedDto[];
  focusedIdx: number;
  setFocusedIdx: (i: number) => void;
  onSelect: (item: ActivityFeedDto) => void;
}) {
  const focusedRowRef = useRef<HTMLTableRowElement | null>(null);

  // Scroll focused row into view (smooth, only when focus is via keyboard)
  useEffect(() => {
    focusedRowRef.current?.scrollIntoView({ block: "nearest", behavior: "smooth" });
  }, [focusedIdx]);

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm border-separate border-spacing-0">
        {/* Sticky table header */}
        <thead className="sticky top-0 z-[1] bg-slate-50/95 dark:bg-slate-900/95 backdrop-blur supports-[backdrop-filter]:bg-slate-50/80 dark:supports-[backdrop-filter]:bg-slate-900/80">
          <tr>
            <th className="text-left px-3 py-2.5 text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800 w-[64px]">Time</th>
            <th className="text-left px-3 py-2.5 text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800 w-[120px]">Type</th>
            <th className="text-left px-3 py-2.5 text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800 w-[88px]">Ref</th>
            <th className="text-left px-3 py-2.5 text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800">Description</th>
            <th className="text-left px-3 py-2.5 text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800 w-[140px]">Contact</th>
            <th className="text-left px-3 py-2.5 text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800 w-[110px]">Source</th>
            <th className="text-left px-3 py-2.5 text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800 w-[100px]">By</th>
            <th className="text-right px-3 py-2.5 text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800 w-[120px]">Amount</th>
          </tr>
        </thead>
        <tbody>
          {(() => {
            let runningIdx = -1;
            return groups.map((group) => (
              <RenderGroup
                key={group.key}
                group={group}
                onIdx={() => { runningIdx++; return runningIdx; }}
                focusedIdx={focusedIdx}
                setFocusedIdx={setFocusedIdx}
                focusedRowRef={focusedRowRef}
                onSelect={onSelect}
              />
            ));
          })()}
        </tbody>
      </table>
      {flat.length > 0 && (
        <div className="px-3 py-2 border-t border-slate-200 dark:border-slate-800 text-[11px] text-slate-400 dark:text-slate-500">
          Tip: <kbd className="px-1 py-0.5 rounded bg-slate-100 dark:bg-slate-800 ring-1 ring-slate-200 dark:ring-slate-700 text-[10px] font-mono">↑↓</kbd> or <kbd className="px-1 py-0.5 rounded bg-slate-100 dark:bg-slate-800 ring-1 ring-slate-200 dark:ring-slate-700 text-[10px] font-mono">j</kbd>/<kbd className="px-1 py-0.5 rounded bg-slate-100 dark:bg-slate-800 ring-1 ring-slate-200 dark:ring-slate-700 text-[10px] font-mono">k</kbd> to navigate · <kbd className="px-1 py-0.5 rounded bg-slate-100 dark:bg-slate-800 ring-1 ring-slate-200 dark:ring-slate-700 text-[10px] font-mono">↵</kbd> to open
        </div>
      )}
    </div>
  );
}

function RenderGroup({
  group, onIdx, focusedIdx, setFocusedIdx, focusedRowRef, onSelect,
}: {
  group: DayGroup;
  onIdx: () => number;
  focusedIdx: number;
  setFocusedIdx: (i: number) => void;
  focusedRowRef: React.RefObject<HTMLTableRowElement | null>;
  onSelect: (item: ActivityFeedDto) => void;
}) {
  const totalAmount = group.items.reduce((s, it) => s + (it.amount ?? 0), 0);
  return (
    <>
      {/* Day header row — full-width, slightly tinted */}
      <tr>
        <td colSpan={8} className="px-3 py-2 bg-slate-50/60 dark:bg-slate-950/40 border-b border-slate-200 dark:border-slate-800">
          <div className="flex items-center justify-between">
            <span className="text-[11px] font-semibold uppercase tracking-wider text-slate-600 dark:text-slate-400">
              {group.label}
              <span className="text-slate-400 dark:text-slate-500 font-normal ml-2">· {group.date.toLocaleDateString(undefined, { month: "short", day: "numeric", year: "numeric" })}</span>
            </span>
            <span className="text-[11px] text-slate-400 dark:text-slate-500 tabular-nums">
              {group.items.length} event{group.items.length === 1 ? "" : "s"}
              {totalAmount > 0 && <span className="ml-2">· {formatNaira(totalAmount)} touched</span>}
            </span>
          </div>
        </td>
      </tr>
      {group.items.map((item) => {
        const idx = onIdx();
        const focused = idx === focusedIdx;
        return (
          <ActivityRow
            key={`${item.id}-${item.type}-${idx}`}
            item={item}
            focused={focused}
            rowRef={focused ? focusedRowRef : undefined}
            onClick={() => { setFocusedIdx(idx); onSelect(item); }}
            onMouseEnter={() => setFocusedIdx(idx)}
          />
        );
      })}
    </>
  );
}

function ActivityRow({
  item, focused, rowRef, onClick, onMouseEnter,
}: {
  item: ActivityFeedDto;
  focused: boolean;
  rowRef?: React.RefObject<HTMLTableRowElement | null>;
  onClick: () => void;
  onMouseEnter: () => void;
}) {
  const meta = metaFor(item.type);
  const isVoided = item.type === "sale_voided" || item.type === "expense_voided";
  const isPositive = item.type === "sale" || item.type === "payment_received" || item.type === "debt_recorded";
  const time = formatTime(new Date(item.createdAtUtc));

  return (
    <tr
      ref={rowRef}
      onClick={onClick}
      onMouseEnter={onMouseEnter}
      className={`group cursor-pointer border-b border-slate-100 dark:border-slate-800 transition-colors ${
        focused ? "bg-cyan-50/40 dark:bg-cyan-950/20" : "hover:bg-slate-50 dark:hover:bg-slate-800/40"
      } ${isVoided ? "opacity-60" : ""}`}
    >
      <td className="px-3 py-2 text-xs font-mono tabular-nums text-slate-500 dark:text-slate-400 align-top">
        {time}
      </td>
      <td className="px-3 py-2 align-top">
        <span className="inline-flex items-center gap-1.5">
          <meta.icon size={13} className={meta.text} aria-hidden="true" />
          <span className="text-xs font-medium text-slate-700 dark:text-slate-300">{meta.label}</span>
        </span>
      </td>
      <td className="px-3 py-2 align-top">
        <span className="text-[11px] font-mono text-slate-500 dark:text-slate-400">{item.refId}</span>
      </td>
      <td className="px-3 py-2 align-top">
        <p className={`text-sm text-slate-900 dark:text-slate-100 truncate max-w-md ${isVoided ? "line-through" : ""}`}>
          {item.description}
        </p>
        {item.details && (
          <p className="text-xs text-slate-500 dark:text-slate-400 truncate max-w-md mt-0.5">{item.details}</p>
        )}
      </td>
      <td className="px-3 py-2 text-sm text-slate-600 dark:text-slate-400 align-top">
        <span className="truncate block max-w-[140px]">{item.contactName ?? <span className="text-slate-300 dark:text-slate-600">—</span>}</span>
      </td>
      <td className="px-3 py-2 align-top">
        {item.source ? <SourceBadge source={item.source} /> : <span className="text-slate-300 dark:text-slate-600">—</span>}
      </td>
      <td className="px-3 py-2 text-xs text-slate-500 dark:text-slate-400 align-top">
        <span className="truncate block max-w-[100px]">{item.recordedBy ?? <span className="text-slate-300 dark:text-slate-600">—</span>}</span>
      </td>
      <td className="px-3 py-2 text-right align-top">
        {item.amount != null ? (
          <span className={`text-sm font-semibold tabular-nums whitespace-nowrap ${
            isVoided
              ? "text-slate-400 dark:text-slate-500 line-through"
              : isPositive
              ? "text-emerald-600 dark:text-emerald-400"
              : "text-rose-600 dark:text-rose-400"
          }`}>
            {isPositive ? "+" : "-"}{formatNaira(item.amount)}
          </span>
        ) : (
          <span className="text-slate-300 dark:text-slate-600">—</span>
        )}
      </td>
    </tr>
  );
}

// ─── Detail drawer ──────────────────────────────────────────────────────────

function DetailDrawer({
  item, onClose, onJumpToSource,
}: {
  item: ActivityFeedDto | null;
  onClose: () => void;
  onJumpToSource: (item: ActivityFeedDto) => void;
}) {
  const { toast } = useToast();
  if (!item) return null;
  const meta = metaFor(item.type);
  const isVoided = item.type === "sale_voided" || item.type === "expense_voided";
  const isPositive = item.type === "sale" || item.type === "payment_received" || item.type === "debt_recorded";
  const created = new Date(item.createdAtUtc);

  return (
    <Drawer open={!!item} onClose={onClose} width="md">
      <DrawerHeader
        title={meta.label}
        subtitle={item.description}
        onClose={onClose}
        actions={
          <div className={`p-2 rounded-md ring-1 ring-slate-200 dark:ring-slate-700 bg-slate-50 dark:bg-slate-800 ${meta.text}`}>
            <meta.icon size={16} aria-hidden="true" />
          </div>
        }
      />
      <DrawerBody>
        <div className="space-y-5">
          {/* Hero: amount */}
          {item.amount != null && (
            <div className="rounded-lg border border-slate-200 dark:border-slate-800 bg-slate-50/50 dark:bg-slate-950/50 p-4">
              <p className="text-[11px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">Amount</p>
              <p className={`text-3xl font-bold tabular-nums tracking-tight mt-1 ${
                isVoided
                  ? "text-slate-400 dark:text-slate-500 line-through"
                  : isPositive
                  ? "text-emerald-600 dark:text-emerald-400"
                  : "text-rose-600 dark:text-rose-400"
              }`}>
                {isPositive ? "+" : "-"}{formatNaira(item.amount)}
              </p>
            </div>
          )}

          {/* Reference + copy */}
          <DetailRow
            label="Reference"
            value={
              <button
                onClick={() => {
                  navigator.clipboard.writeText(item.refId);
                  toast.success("Reference copied", item.refId);
                }}
                className="inline-flex items-center gap-1.5 font-mono text-xs px-2 py-1 rounded bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 text-slate-700 dark:text-slate-300 transition-colors"
              >
                {item.refId}
                <CopyIcon size={11} />
              </button>
            }
          />

          <DetailRow label="When" value={
            <div>
              <p className="text-sm text-slate-900 dark:text-slate-100">
                {created.toLocaleString(undefined, { weekday: "long", year: "numeric", month: "long", day: "numeric", hour: "2-digit", minute: "2-digit" })}
              </p>
              <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">{relativeTime(created)}</p>
            </div>
          } />

          {item.contactName && <DetailRow label="Contact" value={item.contactName} />}
          {item.source && <DetailRow label="Source" value={<SourceBadge source={item.source} />} />}
          {item.recordedBy && <DetailRow label="Recorded by" value={item.recordedBy} />}
          {item.paymentStatus && <DetailRow label="Payment status" value={item.paymentStatus} />}
          {item.paymentMethod && <DetailRow label="Payment method" value={item.paymentMethod} />}
          {item.details && <DetailRow label="Details" value={<p className="whitespace-pre-wrap">{item.details}</p>} />}
        </div>
      </DrawerBody>
      <DrawerFooter>
        <Button variant="outline" onClick={onClose}>Close</Button>
        <Button onClick={() => onJumpToSource(item)} className="gap-1.5">
          View source <ExternalLink size={13} />
        </Button>
      </DrawerFooter>
    </Drawer>
  );
}

function DetailRow({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div>
      <p className="text-[11px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 mb-1">{label}</p>
      <div className="text-sm text-slate-900 dark:text-slate-100">{value}</div>
    </div>
  );
}

function relativeTime(d: Date): string {
  const diff = Date.now() - d.getTime();
  const m = Math.floor(diff / 60000);
  if (m < 1) return "just now";
  if (m < 60) return `${m} minute${m === 1 ? "" : "s"} ago`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h} hour${h === 1 ? "" : "s"} ago`;
  const days = Math.floor(h / 24);
  if (days < 30) return `${days} day${days === 1 ? "" : "s"} ago`;
  return d.toLocaleDateString();
}

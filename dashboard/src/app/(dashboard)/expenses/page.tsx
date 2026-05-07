"use client";

export const dynamic = "force-dynamic";

import { useState, useEffect, useMemo } from "react";
import { useStickyState } from "@/lib/sticky-state";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { AreaChart, Area, ResponsiveContainer } from "recharts";
import { api, fetchAllPaged } from "@/lib/api";
import { formatNaira, formatDateTime } from "@/lib/format";
import { useBusiness } from "@/lib/data-sync";
import type { ExpenseDto, ApiResponse } from "@/lib/types";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { PageHeader } from "@/components/page-header";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import { EmptyState } from "@/components/empty-state";
import { useToast } from "@/components/toast";
import { Pencil, Trash2, Search, X, Receipt as ReceiptIcon, TrendingDown, TrendingUp, Tag, User as UserIcon, LayoutList, Table as TableIcon } from "lucide-react";
import { SourceBadge } from "@/components/source-badge";
import { hasPermission, Permission } from "@/lib/permissions";

const CURRENCY_SYMBOLS: Record<string, string> = { NGN: "\u20A6", GHS: "GH\u20B5", USD: "$", GBP: "\u00A3", KES: "KSh", ZAR: "R", TZS: "TSh", UGX: "USh", RWF: "RF", XAF: "FCFA", XOF: "CFA", EGP: "E\u00A3", ETB: "Br" };

// \u2500\u2500\u2500 Hero card (period total / top category / top vendor) \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
function HeroCard({
  label, mainValue, mainTone = "neutral", sub, sparkline, icon, isLoading, onClick,
}: {
  label: string;
  mainValue: string;
  mainTone?: "neutral" | "bad";
  sub: React.ReactNode;
  sparkline?: { v: number }[];
  icon: React.ReactNode;
  isLoading?: boolean;
  onClick?: () => void;
}) {
  const Wrapper = onClick ? "button" : "div";
  const valueClass = mainTone === "bad"
    ? "text-rose-600 dark:text-rose-400"
    : "text-slate-900 dark:text-slate-50";
  return (
    <Wrapper
      onClick={onClick}
      className={`group relative overflow-hidden text-left bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 transition-all w-full ${
        onClick ? "hover:border-slate-300 dark:hover:border-slate-700 hover:shadow-sm cursor-pointer" : ""
      }`}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <p className="text-[11px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">
            {label}
          </p>
          {isLoading ? (
            <div className="mt-2 space-y-2">
              <Skeleton className="h-7 w-32" />
              <Skeleton className="h-3 w-24" />
            </div>
          ) : (
            <>
              <p className={`text-2xl font-bold mt-1.5 tabular-nums tracking-tight truncate ${valueClass}`}>
                {mainValue}
              </p>
              <div className="text-[11px] mt-1 text-slate-500 dark:text-slate-400">
                {sub}
              </div>
            </>
          )}
        </div>
        <div className="p-1.5 rounded-md bg-slate-50 dark:bg-slate-950 text-slate-500 dark:text-slate-400 flex-shrink-0">
          {icon}
        </div>
      </div>
      {/* Sparkline — only render when there's actual data. Avoids Recharts'
          SSG "container has no dimensions" warning on empty zero-buckets. */}
      {sparkline && sparkline.length > 1 && sparkline.some((p) => p.v > 0) && (
        <div className="mt-3 -mx-5 -mb-5 h-10">
          <ResponsiveContainer width="100%" height="100%">
            <AreaChart data={sparkline} margin={{ top: 0, right: 0, left: 0, bottom: 0 }}>
              <Area type="monotone" dataKey="v" stroke="#f43f5e" strokeWidth={1.5} fill="#f43f5e" fillOpacity={0.1} isAnimationActive={false} />
            </AreaChart>
          </ResponsiveContainer>
        </div>
      )}
    </Wrapper>
  );
}

// \u2500\u2500\u2500 Filter chip \u2014 small removable pill for active filters \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
function FilterChip({ label, value, onRemove }: { label: string; value: string; onRemove: () => void }) {
  return (
    <span className="inline-flex items-center gap-1 px-2 py-1 rounded-md bg-slate-100 dark:bg-slate-800 text-[11px] text-slate-700 dark:text-slate-300 ring-1 ring-slate-200 dark:ring-slate-700">
      <span className="text-slate-500 dark:text-slate-400 font-medium">{label}:</span>
      <span className="font-semibold">{value}</span>
      <button
        onClick={onRemove}
        className="ml-0.5 -mr-0.5 p-0.5 rounded hover:bg-slate-200 dark:hover:bg-slate-700 text-slate-400 dark:text-slate-500 hover:text-slate-700 dark:hover:text-slate-200 transition-colors"
        aria-label={`Remove ${label} filter`}
      >
        <X size={11} />
      </button>
    </span>
  );
}

// \u2500\u2500\u2500 Bulk recategorize dialog \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
function BulkRecategorizeDialog({
  count, onClose, onApply, busy,
}: {
  count: number;
  onClose: () => void;
  onApply: (newCategory: string) => Promise<void>;
  busy: boolean;
}) {
  const [category, setCategory] = useState("");
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Recategorize {count} expense{count === 1 ? "" : "s"}</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <p className="text-sm text-slate-600 dark:text-slate-400">
            Pick a new category. All selected expenses will be updated.
          </p>
          <div>
            <Label className="text-xs text-slate-500 dark:text-slate-400">New category</Label>
            <select
              autoFocus
              value={category}
              onChange={(e) => setCategory(e.target.value)}
              className="w-full h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 text-sm"
            >
              <option value="">Choose a category\u2026</option>
              <optgroup label="Operating">
                {OPERATING_CATEGORIES.map((c) => <option key={c} value={c}>{c}</option>)}
              </optgroup>
              <optgroup label="Inventory">
                {INVENTORY_CATEGORIES.map((c) => <option key={c} value={c}>{c}</option>)}
              </optgroup>
            </select>
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={busy}>Cancel</Button>
          <Button onClick={() => category && onApply(category)} disabled={busy || !category}>
            {busy ? "Updating\u2026" : `Recategorize ${count}`}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// \u2500\u2500\u2500 Expense row \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
function ExpenseRow({
  expense, canEdit, selected, onToggleSelect, onEdit, onDelete,
}: {
  expense: ExpenseDto;
  canEdit: boolean;
  selected: boolean;
  onToggleSelect: () => void;
  onEdit: () => void;
  onDelete: () => void;
}) {
  const isCogs = (expense.expenseType ?? "").toLowerCase() === "cogs";
  return (
    <div
      role="button"
      tabIndex={0}
      onClick={() => canEdit && onEdit()}
      onKeyDown={(e) => { if ((e.key === "Enter" || e.key === " ") && canEdit) { e.preventDefault(); onEdit(); } }}
      className={`group flex items-center gap-3 px-2 py-3 transition-colors ${
        selected ? "bg-cyan-50/60 dark:bg-cyan-950/20" :
        canEdit ? "cursor-pointer hover:bg-slate-50 dark:hover:bg-slate-800/50" : "cursor-default"
      }`}
    >
      {canEdit && (
        <input
          type="checkbox"
          checked={selected}
          onClick={(e) => e.stopPropagation()}
          onChange={onToggleSelect}
          className="h-4 w-4 rounded border-slate-300 dark:border-slate-700 accent-cyan-500 flex-shrink-0"
          aria-label={`Select ${expense.category} expense`}
        />
      )}
      <div className="p-2 rounded-md flex-shrink-0 bg-orange-50 dark:bg-orange-950/30 text-orange-600 dark:text-orange-400">
        <ReceiptIcon size={14} />
      </div>
      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-2 flex-wrap">
          <span className="text-[10px] font-semibold uppercase tracking-wider text-slate-700 dark:text-slate-300">
            {expense.category}
          </span>
          {isCogs && (
            <span className="text-[10px] font-semibold uppercase tracking-wider px-1.5 py-0.5 rounded bg-violet-50 dark:bg-violet-950/30 text-violet-600 dark:text-violet-400">
              COGS
            </span>
          )}
          <SourceBadge source={expense.source} />
          {expense.paymentMethod && (
            <span className="text-[10px] font-medium text-slate-500 dark:text-slate-400 uppercase tracking-wider">
              \u00B7 {expense.paymentMethod}
            </span>
          )}
        </div>
        {(() => {
          const hasRecipient = !!expense.paidTo?.trim();
          const hasNotes = !!expense.notes?.trim();
          if (hasRecipient) {
            return (
              <>
                <p className="text-sm font-medium text-slate-900 dark:text-slate-50 mt-0.5 truncate">
                  {expense.paidTo}
                </p>
                {hasNotes && (
                  <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5 truncate">
                    {expense.notes}
                  </p>
                )}
              </>
            );
          }
          if (hasNotes) {
            return (
              <p className="text-sm font-medium text-slate-900 dark:text-slate-50 mt-0.5 truncate">
                {expense.notes}
              </p>
            );
          }
          return (
            <p className="text-sm text-slate-400 dark:text-slate-500 italic mt-0.5 truncate">
              No recipient
            </p>
          );
        })()}
        <p className="text-[11px] text-slate-500 dark:text-slate-400 mt-1 tabular-nums">
          {formatDateTime(expense.createdAtUtc)}
        </p>
      </div>
      <span className="text-sm font-semibold tabular-nums whitespace-nowrap text-rose-600 dark:text-rose-400">
        -{formatNaira(expense.amount)}
      </span>
      {canEdit && (
        <div className="flex items-center gap-0.5 opacity-0 group-hover:opacity-100 transition-opacity flex-shrink-0">
          <button
            onClick={(e) => { e.stopPropagation(); onEdit(); }}
            className="p-1.5 rounded hover:bg-slate-200 dark:hover:bg-slate-700 text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-50"
            title="Edit"
          >
            <Pencil size={13} />
          </button>
          <button
            onClick={(e) => { e.stopPropagation(); onDelete(); }}
            className="p-1.5 rounded hover:bg-rose-50 dark:hover:bg-rose-950/30 text-slate-500 dark:text-slate-400 hover:text-rose-600 dark:hover:text-rose-400"
            title="Delete"
          >
            <Trash2 size={13} />
          </button>
        </div>
      )}
    </div>
  );
}

// ─── Tabular view ───────────────────────────────────────────────────────────
// Same data as the list view but rendered in a proper table with aligned columns.
// Day headers span all columns and stay tinted. Sticky table header on scroll.
function ExpensesTable({
  groups, visibleItems, canEdit, selectedIds, onToggleSelect, onToggleSelectAll,
  onEdit, onDelete, truncated, totalCount,
}: {
  groups: DayGroup[];
  visibleItems: ExpenseDto[];
  canEdit: boolean;
  selectedIds: Set<string>;
  onToggleSelect: (id: string) => void;
  onToggleSelectAll: () => void;
  onEdit: (e: ExpenseDto) => void;
  onDelete: (e: ExpenseDto) => void;
  truncated: boolean;
  totalCount: number;
}) {
  const allSelected = visibleItems.length > 0 && visibleItems.every((p) => selectedIds.has(p.id));
  // Column count for day-header colspan — adjusts when checkbox column is hidden
  const colCount = canEdit ? 8 : 7;
  return (
    <div className="overflow-x-auto -mx-2">
      <table className="w-full text-sm border-separate border-spacing-0">
        <thead className="sticky top-0 z-[1] bg-slate-50/95 dark:bg-slate-900/95 backdrop-blur supports-[backdrop-filter]:bg-slate-50/80 dark:supports-[backdrop-filter]:bg-slate-900/80">
          <tr>
            {canEdit && (
              <th className="px-3 py-2.5 border-b border-slate-200 dark:border-slate-800 w-[40px]">
                <input
                  type="checkbox"
                  checked={allSelected}
                  onChange={onToggleSelectAll}
                  className="h-4 w-4 rounded border-slate-300 dark:border-slate-700 accent-cyan-500"
                  aria-label="Select all visible"
                />
              </th>
            )}
            <th className="text-left px-3 py-2.5 text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800 w-[64px]">Time</th>
            <th className="text-left px-3 py-2.5 text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800 w-[160px]">Category</th>
            <th className="text-left px-3 py-2.5 text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800">Paid to / Notes</th>
            <th className="text-left px-3 py-2.5 text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800 w-[110px]">Method</th>
            <th className="text-left px-3 py-2.5 text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800 w-[110px]">Source</th>
            <th className="text-right px-3 py-2.5 text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800 w-[120px]">Amount</th>
            <th className="px-3 py-2.5 border-b border-slate-200 dark:border-slate-800 w-[60px]" />
          </tr>
        </thead>
        <tbody>
          {groups.map((group) => (
            <ExpensesTableGroup
              key={group.key}
              group={group}
              colCount={colCount}
              canEdit={canEdit}
              selectedIds={selectedIds}
              onToggleSelect={onToggleSelect}
              onEdit={onEdit}
              onDelete={onDelete}
            />
          ))}
        </tbody>
      </table>
      {truncated && (
        <div className="mt-3 pt-3 border-t border-slate-200 dark:border-slate-800 text-center text-xs text-slate-500 dark:text-slate-400">
          Showing latest {visibleItems.length} of {totalCount.toLocaleString()} · narrow your period to see all
        </div>
      )}
    </div>
  );
}

function ExpensesTableGroup({
  group, colCount, canEdit, selectedIds, onToggleSelect, onEdit, onDelete,
}: {
  group: DayGroup;
  colCount: number;
  canEdit: boolean;
  selectedIds: Set<string>;
  onToggleSelect: (id: string) => void;
  onEdit: (e: ExpenseDto) => void;
  onDelete: (e: ExpenseDto) => void;
}) {
  return (
    <>
      {/* Day header row — spans all columns */}
      <tr>
        <td colSpan={colCount} className="px-3 py-2 bg-slate-50/60 dark:bg-slate-950/40 border-b border-slate-200 dark:border-slate-800">
          <div className="flex items-center justify-between">
            <span className="text-[11px] font-semibold uppercase tracking-wider text-slate-600 dark:text-slate-400">
              {group.label}
              <span className="text-slate-400 dark:text-slate-500 font-normal ml-2">
                · {group.date.toLocaleDateString(undefined, { month: "short", day: "numeric", year: "numeric" })}
              </span>
            </span>
            <span className="text-[11px] text-slate-500 dark:text-slate-400 tabular-nums">
              {group.items.length} · <span className="font-semibold text-rose-600 dark:text-rose-400">-{formatNaira(group.total)}</span>
            </span>
          </div>
        </td>
      </tr>
      {group.items.map((expense) => (
        <ExpenseTableRow
          key={expense.id}
          expense={expense}
          canEdit={canEdit}
          selected={selectedIds.has(expense.id)}
          onToggleSelect={() => onToggleSelect(expense.id)}
          onEdit={() => onEdit(expense)}
          onDelete={() => onDelete(expense)}
        />
      ))}
    </>
  );
}

function ExpenseTableRow({
  expense, canEdit, selected, onToggleSelect, onEdit, onDelete,
}: {
  expense: ExpenseDto;
  canEdit: boolean;
  selected: boolean;
  onToggleSelect: () => void;
  onEdit: () => void;
  onDelete: () => void;
}) {
  const isCogs = (expense.expenseType ?? "").toLowerCase() === "cogs";
  const time = new Date(expense.createdAtUtc).toLocaleTimeString(undefined, { hour: "2-digit", minute: "2-digit", hour12: false });
  return (
    <tr
      onClick={() => canEdit && onEdit()}
      className={`group border-b border-slate-100 dark:border-slate-800 transition-colors ${
        selected ? "bg-cyan-50/40 dark:bg-cyan-950/20" :
        canEdit ? "cursor-pointer hover:bg-slate-50 dark:hover:bg-slate-800/40" : ""
      }`}
    >
      {canEdit && (
        <td className="px-3 py-2 align-middle">
          <input
            type="checkbox"
            checked={selected}
            onClick={(e) => e.stopPropagation()}
            onChange={onToggleSelect}
            className="h-4 w-4 rounded border-slate-300 dark:border-slate-700 accent-cyan-500"
            aria-label={`Select ${expense.category} expense`}
          />
        </td>
      )}
      <td className="px-3 py-2 align-middle text-xs font-mono tabular-nums text-slate-500 dark:text-slate-400">
        {time}
      </td>
      <td className="px-3 py-2 align-middle">
        <div className="flex items-center gap-1.5">
          <span className="text-xs font-medium text-slate-700 dark:text-slate-300 truncate max-w-[120px]">{expense.category}</span>
          {isCogs && (
            <span className="text-[9px] font-semibold uppercase tracking-wider px-1 py-0.5 rounded bg-violet-50 dark:bg-violet-950/30 text-violet-600 dark:text-violet-400">
              COGS
            </span>
          )}
        </div>
      </td>
      <td className="px-3 py-2 align-middle">
        {(() => {
          const hasRecipient = !!expense.paidTo?.trim();
          const hasNotes = !!expense.notes?.trim();
          if (hasRecipient) {
            return (
              <>
                <p className="text-sm text-slate-900 dark:text-slate-100 truncate max-w-md">{expense.paidTo}</p>
                {hasNotes && (
                  <p className="text-xs text-slate-500 dark:text-slate-400 truncate max-w-md mt-0.5">{expense.notes}</p>
                )}
              </>
            );
          }
          if (hasNotes) {
            return <p className="text-sm text-slate-900 dark:text-slate-100 truncate max-w-md">{expense.notes}</p>;
          }
          return <p className="text-sm text-slate-400 dark:text-slate-500 italic">No recipient</p>;
        })()}
      </td>
      <td className="px-3 py-2 align-middle text-xs text-slate-600 dark:text-slate-400">
        {expense.paymentMethod ?? <span className="text-slate-300 dark:text-slate-600">—</span>}
      </td>
      <td className="px-3 py-2 align-middle">
        {expense.source ? <SourceBadge source={expense.source} /> : <span className="text-slate-300 dark:text-slate-600">—</span>}
      </td>
      <td className="px-3 py-2 align-middle text-right">
        <span className="text-sm font-semibold tabular-nums whitespace-nowrap text-rose-600 dark:text-rose-400">
          -{formatNaira(expense.amount)}
        </span>
      </td>
      <td className="px-3 py-2 align-middle text-right">
        {canEdit && (
          <div className="flex items-center justify-end gap-0.5 opacity-0 group-hover:opacity-100 transition-opacity">
            <button
              onClick={(e) => { e.stopPropagation(); onEdit(); }}
              className="p-1.5 rounded hover:bg-slate-200 dark:hover:bg-slate-700 text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-50"
              title="Edit"
            >
              <Pencil size={13} />
            </button>
            <button
              onClick={(e) => { e.stopPropagation(); onDelete(); }}
              className="p-1.5 rounded hover:bg-rose-50 dark:hover:bg-rose-950/30 text-slate-500 dark:text-slate-400 hover:text-rose-600 dark:hover:text-rose-400"
              title="Delete"
            >
              <Trash2 size={13} />
            </button>
          </div>
        )}
      </td>
    </tr>
  );
}

const OPERATING_CATEGORIES = [
  "Salary", "Rent", "Transport", "Utilities", "Fuel", "Airtime", "Internet",
  "Office Supplies", "Maintenance", "Marketing", "Insurance", "Taxes",
  "Professional Services", "Subscriptions", "Cleaning", "Security", "General",
];

const INVENTORY_CATEGORIES = [
  "Inventory Purchase", "Stock Replenishment", "Raw Materials", "Goods for Resale",
  "Supplies", "Merchandise", "Packaging", "Shipping & Freight",
];

// ─── Period selector ────────────────────────────────────────────────────────
type Period = "this_month" | "last_30d" | "last_quarter" | "year" | "all";

const PERIOD_OPTIONS: { id: Period; label: string }[] = [
  { id: "this_month",   label: "This month" },
  { id: "last_30d",     label: "Last 30 days" },
  { id: "last_quarter", label: "Last quarter" },
  { id: "year",         label: "Year to date" },
  { id: "all",          label: "All time" },
];

/** Returns [start, end] ISO strings for the given period, plus the previous period for delta. */
function periodRange(p: Period): { from?: string; to?: string; prevFrom?: string; prevTo?: string; days: number } {
  const now = new Date();
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const tomorrow = new Date(today); tomorrow.setDate(tomorrow.getDate() + 1);

  switch (p) {
    case "this_month": {
      const start = new Date(now.getFullYear(), now.getMonth(), 1);
      const prevStart = new Date(now.getFullYear(), now.getMonth() - 1, 1);
      const prevEnd = new Date(start); // exclusive
      return {
        from: start.toISOString(),
        to: tomorrow.toISOString(),
        prevFrom: prevStart.toISOString(),
        prevTo: prevEnd.toISOString(),
        days: Math.max(1, Math.round((+tomorrow - +start) / 86400000)),
      };
    }
    case "last_30d": {
      const start = new Date(today); start.setDate(start.getDate() - 29);
      const prevEnd = new Date(start);
      const prevStart = new Date(prevEnd); prevStart.setDate(prevStart.getDate() - 30);
      return {
        from: start.toISOString(),
        to: tomorrow.toISOString(),
        prevFrom: prevStart.toISOString(),
        prevTo: prevEnd.toISOString(),
        days: 30,
      };
    }
    case "last_quarter": {
      const start = new Date(today); start.setDate(start.getDate() - 89);
      const prevEnd = new Date(start);
      const prevStart = new Date(prevEnd); prevStart.setDate(prevStart.getDate() - 90);
      return {
        from: start.toISOString(),
        to: tomorrow.toISOString(),
        prevFrom: prevStart.toISOString(),
        prevTo: prevEnd.toISOString(),
        days: 90,
      };
    }
    case "year": {
      const start = new Date(now.getFullYear(), 0, 1);
      const prevStart = new Date(now.getFullYear() - 1, 0, 1);
      const prevEnd = new Date(start);
      return {
        from: start.toISOString(),
        to: tomorrow.toISOString(),
        prevFrom: prevStart.toISOString(),
        prevTo: prevEnd.toISOString(),
        days: Math.max(1, Math.round((+tomorrow - +start) / 86400000)),
      };
    }
    case "all":
    default:
      return { days: 365 };
  }
}

// Aggregations from a flat list of expenses
type Aggregates = {
  total: number;
  count: number;
  topCategory: { name: string; amount: number; share: number } | null;
  topVendor: { name: string; amount: number; count: number } | null;
  sparkline: { v: number }[];
};
function aggregate(items: ExpenseDto[], days: number): Aggregates {
  const total = items.reduce((s, e) => s + e.amount, 0);

  // Top category
  const byCat = new Map<string, number>();
  for (const e of items) {
    byCat.set(e.category, (byCat.get(e.category) ?? 0) + e.amount);
  }
  let topCat: { name: string; amount: number; share: number } | null = null;
  for (const [name, amount] of byCat) {
    if (!topCat || amount > topCat.amount) {
      topCat = { name, amount, share: total > 0 ? amount / total : 0 };
    }
  }

  // Top vendor (paidTo, normalized)
  const byVendor = new Map<string, { name: string; amount: number; count: number }>();
  for (const e of items) {
    if (!e.paidTo?.trim()) continue;
    const key = e.paidTo.trim().toLowerCase();
    const cur = byVendor.get(key) ?? { name: e.paidTo.trim(), amount: 0, count: 0 };
    cur.amount += e.amount;
    cur.count += 1;
    byVendor.set(key, cur);
  }
  let topVend: { name: string; amount: number; count: number } | null = null;
  for (const v of byVendor.values()) {
    if (!topVend || v.amount > topVend.amount) topVend = v;
  }

  // Sparkline: daily totals over the last `days` days
  const sparkBuckets = new Array(Math.min(days, 30)).fill(0); // cap at 30 buckets for readability
  const bucketDays = Math.min(days, 30);
  const now = new Date();
  for (const e of items) {
    const d = new Date(e.createdAtUtc);
    const ageDays = Math.floor((+now - +d) / 86400000);
    if (ageDays < 0 || ageDays >= bucketDays) continue;
    sparkBuckets[bucketDays - 1 - ageDays] += e.amount;
  }
  const sparkline = sparkBuckets.map((v) => ({ v }));

  return { total, count: items.length, topCategory: topCat, topVendor: topVend, sparkline };
}

// Date grouping (mirrors Activity page)
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
  if (diffDays >= 0 && diffDays < 7) return d.toLocaleDateString(undefined, { weekday: "long" });
  return d.toLocaleDateString(undefined, { weekday: "short", month: "short", day: "numeric", year: now.getFullYear() === d.getFullYear() ? undefined : "numeric" });
}
type DayGroup = { key: string; label: string; date: Date; items: ExpenseDto[]; total: number };
function groupByDay(items: ExpenseDto[]): DayGroup[] {
  const groups = new Map<string, DayGroup>();
  for (const item of items) {
    const d = new Date(item.createdAtUtc);
    const k = dayKey(d);
    let g = groups.get(k);
    if (!g) {
      g = { key: k, label: dayLabel(d), date: new Date(d.getFullYear(), d.getMonth(), d.getDate()), items: [], total: 0 };
      groups.set(k, g);
    }
    g.items.push(item);
    g.total += item.amount;
  }
  return Array.from(groups.values()).sort((a, b) => +b.date - +a.date);
}

export default function ExpensesPage() {
  const qc = useQueryClient();
  const { toast } = useToast();
  const [adding, setAdding] = useState(false);
  const [editing, setEditing] = useState<ExpenseDto | null>(null);
  const [deleting, setDeleting] = useState<ExpenseDto | null>(null);
  const [period, setPeriod] = useStickyState<Period>("expenses-period", "this_month",
    (v): v is Period => v === "this_month" || v === "last_30d" || v === "last_quarter" || v === "year" || v === "all");
  const [viewMode, setViewMode] = useStickyState<"list" | "table">(
    "ojunai-expenses-view",
    "list",
    (v): v is "list" | "table" => v === "list" || v === "table",
  );
  const [categoryFilter, setCategoryFilter] = useStickyState<string>("expenses-category-filter", "");
  const [methodFilter, setMethodFilter] = useStickyState<string>("expenses-method-filter", "");
  const [sourceFilter, setSourceFilter] = useStickyState<string>("expenses-source-filter", "");
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  // Bulk select state — set of expense ids
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [bulkConfirmDelete, setBulkConfirmDelete] = useState(false);
  const [bulkRecategorize, setBulkRecategorize] = useState(false);
  const [bulkBusy, setBulkBusy] = useState(false);
  // FAB visibility — show floating + button after scrolling past the page header
  const [scrolled, setScrolled] = useState(false);

  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search.trim()), 250);
    return () => clearTimeout(t);
  }, [search]);

  // Watch scroll position so the FAB only appears once the header is out of view
  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 200);
    window.addEventListener("scroll", onScroll, { passive: true });
    return () => window.removeEventListener("scroll", onScroll);
  }, []);

  // Auto-open create dialog from ?new=1 (dashboard quick action)
  useEffect(() => {
    if (new URLSearchParams(window.location.search).get("new") === "1" && hasPermission(Permission.RecordExpenses)) {
      setAdding(true);
    }
  }, []);

  const range = useMemo(() => periodRange(period), [period]);

  const { data: filters } = useQuery({
    queryKey: ["expense-filters"],
    queryFn: async () => {
      const { data } = await api.get<ApiResponse<{ categories: string[]; paymentMethods: string[]; sources: string[] }>>(
        `/expenses/filters`
      );
      return data.data!;
    },
  });

  // Page through the entire period so totals + the feed reflect every expense,
  // not just the first batch.
  const { data: allExpenses, isLoading } = useQuery({
    queryKey: ["expenses", period, categoryFilter, methodFilter, sourceFilter, debouncedSearch],
    queryFn: () => fetchAllPaged<ExpenseDto>((p, ps) => {
      const params = new URLSearchParams({ page: String(p), pageSize: String(ps) });
      if (range.from) params.set("from", range.from);
      if (range.to) params.set("to", range.to);
      if (categoryFilter) params.set("category", categoryFilter);
      if (methodFilter) params.set("paymentMethod", methodFilter);
      if (sourceFilter) params.set("source", sourceFilter);
      if (debouncedSearch) params.set("search", debouncedSearch);
      return `/expenses?${params}`;
    }),
  });

  const data = useMemo(() => allExpenses ? { items: allExpenses, totalCount: allExpenses.length, page: 1, pageSize: allExpenses.length, totalPages: 1 } : undefined, [allExpenses]);

  // Previous-period query — only fetched when period != "all" (no comparable previous).
  const { data: prevExpenses } = useQuery({
    queryKey: ["expenses-prev", period],
    queryFn: () => fetchAllPaged<ExpenseDto>((p, ps) => {
      const params = new URLSearchParams({ page: String(p), pageSize: String(ps) });
      if (range.prevFrom) params.set("from", range.prevFrom);
      if (range.prevTo) params.set("to", range.prevTo);
      return `/expenses?${params}`;
    }),
    enabled: !!range.prevFrom && !!range.prevTo,
  });

  const prevData = useMemo(() => prevExpenses ? { items: prevExpenses, totalCount: prevExpenses.length, page: 1, pageSize: prevExpenses.length, totalPages: 1 } : undefined, [prevExpenses]);

  const items = useMemo(() => data?.items ?? [], [data?.items]);
  const stats = useMemo(() => aggregate(items, range.days), [items, range.days]);
  const prevTotal = useMemo(() => (prevData?.items ?? []).reduce((s, e) => s + e.amount, 0), [prevData?.items]);
  const delta = useMemo(() => {
    if (!range.prevFrom || prevTotal === 0) return null;
    const pct = ((stats.total - prevTotal) / prevTotal) * 100;
    return { pct, up: stats.total > prevTotal };
  }, [stats.total, prevTotal, range.prevFrom]);

  const groups = useMemo(() => groupByDay(items), [items]);
  const truncated = (data?.totalCount ?? 0) > items.length;
  const periodLabel = PERIOD_OPTIONS.find((p) => p.id === period)?.label ?? period;

  const hasFilters = !!categoryFilter || !!methodFilter || !!sourceFilter || !!debouncedSearch;
  function clearFilters() {
    setCategoryFilter("");
    setMethodFilter("");
    setSourceFilter("");
    setSearch("");
  }

  // ── Bulk select helpers ────────────────────────────────────────────────────
  function toggleSelect(id: string) {
    setSelectedIds((s) => {
      const n = new Set(s);
      if (n.has(id)) n.delete(id); else n.add(id);
      return n;
    });
  }
  function clearSelection() { setSelectedIds(new Set()); }
  function toggleSelectAll(visible: ExpenseDto[]) {
    setSelectedIds((s) => {
      const allVisibleSelected = visible.length > 0 && visible.every((p) => s.has(p.id));
      const n = new Set(s);
      if (allVisibleSelected) visible.forEach((p) => n.delete(p.id));
      else visible.forEach((p) => n.add(p.id));
      return n;
    });
  }
  // Clear selection when filters/period change so the user doesn't accidentally act on out-of-view rows
  useEffect(() => { clearSelection(); }, [period, categoryFilter, methodFilter, sourceFilter, debouncedSearch]);

  async function bulkDelete() {
    const ids = Array.from(selectedIds);
    setBulkBusy(true);
    try {
      const results = await Promise.allSettled(ids.map((id) => api.delete(`/expenses/${id}`)));
      const succeeded = results.filter((r) => r.status === "fulfilled").length;
      qc.invalidateQueries({ queryKey: ["expenses"] });
      qc.invalidateQueries({ queryKey: ["expenses-prev"] });
      qc.invalidateQueries({ queryKey: ["expense-filters"] });
      toast.success(`${succeeded} expense${succeeded === 1 ? "" : "s"} deleted`,
        succeeded < ids.length ? `${ids.length - succeeded} failed — check permissions` : undefined);
      clearSelection();
      setBulkConfirmDelete(false);
    } finally {
      setBulkBusy(false);
    }
  }

  async function bulkRecategorizeApply(newCategory: string) {
    const ids = Array.from(selectedIds);
    const targets = items.filter((e) => ids.includes(e.id));
    setBulkBusy(true);
    try {
      // Use existing PUT /expenses/:id endpoint per-row. Send full payload to preserve other fields.
      const results = await Promise.allSettled(
        targets.map((e) =>
          api.put(`/expenses/${e.id}`, {
            category: newCategory,
            amount: e.amount,
            paidTo: e.paidTo,
            notes: e.notes,
            paymentMethod: e.paymentMethod,
          })
        )
      );
      const succeeded = results.filter((r) => r.status === "fulfilled").length;
      qc.invalidateQueries({ queryKey: ["expenses"] });
      qc.invalidateQueries({ queryKey: ["expenses-prev"] });
      qc.invalidateQueries({ queryKey: ["expense-filters"] });
      toast.success(`${succeeded} expense${succeeded === 1 ? "" : "s"} recategorized to ${newCategory}`,
        succeeded < ids.length ? `${ids.length - succeeded} failed` : undefined);
      clearSelection();
      setBulkRecategorize(false);
    } finally {
      setBulkBusy(false);
    }
  }

  function bulkExportCsv() {
    const ids = selectedIds;
    const rows = items.filter((e) => ids.has(e.id));
    const headers = ["Date", "Category", "Type", "Paid To", "Notes", "Method", "Source", "Amount"];
    const csvRows = rows.map((e) => [
      new Date(e.createdAtUtc).toISOString(),
      e.category,
      e.expenseType,
      e.paidTo ?? "",
      e.notes ?? "",
      e.paymentMethod ?? "",
      e.source ?? "",
      String(e.amount),
    ]);
    const csv = [headers, ...csvRows]
      .map((r) => r.map((c) => {
        const s = String(c ?? "");
        return s.includes(",") || s.includes('"') || s.includes("\n") ? `"${s.replace(/"/g, '""')}"` : s;
      }).join(","))
      .join("\n");
    const blob = new Blob(["﻿" + csv], { type: "text/csv;charset=utf-8;" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `expenses-${new Date().toISOString().slice(0, 10)}.csv`;
    document.body.appendChild(a); a.click(); a.remove();
    URL.revokeObjectURL(url);
    toast.success(`Exported ${rows.length} expense${rows.length === 1 ? "" : "s"} to CSV`);
  }

  // List of all expenses on the current view, used by select-all
  const visibleItems = items;

  return (
    <div className="space-y-6">
      <PageHeader
        title="Expenses"
        subtitle="Where your money goes — track, categorize, and spot leaks."
        actions={
          hasPermission(Permission.RecordExpenses) ? (
            <Button onClick={() => setAdding(true)}>+ Add Expense</Button>
          ) : null
        }
      />

      {/* Period selector */}
      <div className="overflow-x-auto -mx-1 px-1">
        <div className="inline-flex items-center gap-1 bg-slate-100 dark:bg-slate-800 p-1 rounded-lg">
          {PERIOD_OPTIONS.map((p) => {
            const active = period === p.id;
            return (
              <button
                key={p.id}
                onClick={() => setPeriod(p.id)}
                className={`px-3 py-1.5 rounded-md text-sm font-medium whitespace-nowrap transition-colors ${
                  active
                    ? "bg-white dark:bg-slate-900 text-slate-900 dark:text-slate-50 shadow-sm ring-1 ring-slate-200 dark:ring-slate-700"
                    : "text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-50"
                }`}
              >
                {p.label}
              </button>
            );
          })}
        </div>
      </div>

      {/* ── Hero: 3 cards (period total + delta + sparkline / top category / top vendor) ── */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <HeroCard
          label={periodLabel}
          isLoading={isLoading}
          icon={<TrendingDown size={14} />}
          mainValue={formatNaira(stats.total)}
          mainTone="bad"
          sub={
            delta ? (
              <span className={`inline-flex items-center gap-1 ${delta.up ? "text-rose-600 dark:text-rose-400" : "text-emerald-600 dark:text-emerald-400"}`}>
                {delta.up ? <TrendingUp size={11} /> : <TrendingDown size={11} />}
                {Math.abs(delta.pct).toFixed(0)}% vs last period
              </span>
            ) : (
              <span className="text-slate-400 dark:text-slate-500">{stats.count} expense{stats.count === 1 ? "" : "s"}</span>
            )
          }
          sparkline={stats.sparkline}
        />
        <HeroCard
          label="Top category"
          isLoading={isLoading}
          icon={<Tag size={14} />}
          mainValue={stats.topCategory ? stats.topCategory.name : "—"}
          mainTone="neutral"
          sub={
            stats.topCategory ? (
              <span className="tabular-nums">
                {formatNaira(stats.topCategory.amount)}
                <span className="text-slate-400 dark:text-slate-500"> · {(stats.topCategory.share * 100).toFixed(0)}% of spend</span>
              </span>
            ) : (
              <span className="text-slate-400 dark:text-slate-500">No expenses yet</span>
            )
          }
          onClick={stats.topCategory ? () => setCategoryFilter(stats.topCategory!.name) : undefined}
        />
        <HeroCard
          label="Top vendor"
          isLoading={isLoading}
          icon={<UserIcon size={14} />}
          mainValue={stats.topVendor ? stats.topVendor.name : "—"}
          mainTone="neutral"
          sub={
            stats.topVendor ? (
              <span className="tabular-nums">
                {formatNaira(stats.topVendor.amount)}
                <span className="text-slate-400 dark:text-slate-500"> · {stats.topVendor.count} payment{stats.topVendor.count === 1 ? "" : "s"}</span>
              </span>
            ) : (
              <span className="text-slate-400 dark:text-slate-500">No vendor data</span>
            )
          }
          onClick={stats.topVendor ? () => setSearch(stats.topVendor!.name) : undefined}
        />
      </div>

      {/* Filter row — search + dropdowns. Active filters surfaced as removable chips below. */}
      <div className="space-y-2">
        <div className="flex items-center gap-2 flex-wrap">
          <div className="relative w-full sm:max-w-xs">
            <Search size={14} className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400 dark:text-slate-500 pointer-events-none" />
            <Input
              type="search"
              placeholder="Search by category, paid to, or notes..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="h-9 pl-9 pr-9"
            />
            {search && (
              <button onClick={() => setSearch("")} className="absolute right-2 top-1/2 -translate-y-1/2 text-slate-400 dark:text-slate-500 hover:text-slate-700 dark:hover:text-slate-300 p-1 rounded" type="button">
                <X size={13} />
              </button>
            )}
          </div>
          <select
            value={categoryFilter}
            onChange={(e) => setCategoryFilter(e.target.value)}
            className="h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 text-xs"
            aria-label="Category filter"
          >
            <option value="">+ Category</option>
            {(filters?.categories ?? []).map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
          <select
            value={methodFilter}
            onChange={(e) => setMethodFilter(e.target.value)}
            className="h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 text-xs"
            aria-label="Payment method filter"
          >
            <option value="">+ Method</option>
            {(filters?.paymentMethods ?? []).map((m) => <option key={m} value={m}>{m}</option>)}
          </select>
          <select
            value={sourceFilter}
            onChange={(e) => setSourceFilter(e.target.value)}
            className="h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 text-xs"
            aria-label="Source filter"
          >
            <option value="">+ Source</option>
            {(filters?.sources ?? []).map((s) => <option key={s} value={s}>{s}</option>)}
          </select>

          {/* View toggle — pushed to the right */}
          <div className="ml-auto inline-flex items-center bg-slate-100 dark:bg-slate-800 p-1 rounded-md flex-shrink-0">
            <button
              onClick={() => setViewMode("list")}
              aria-label="List view"
              title="List view"
              className={`flex items-center gap-1.5 px-2 py-1 rounded text-xs font-medium transition-colors ${
                viewMode === "list"
                  ? "bg-white dark:bg-slate-900 text-slate-900 dark:text-slate-50 shadow-sm ring-1 ring-slate-200 dark:ring-slate-700"
                  : "text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-50"
              }`}
            >
              <LayoutList size={13} />
              <span className="hidden sm:inline">List</span>
            </button>
            <button
              onClick={() => setViewMode("table")}
              aria-label="Table view"
              title="Table view"
              className={`flex items-center gap-1.5 px-2 py-1 rounded text-xs font-medium transition-colors ${
                viewMode === "table"
                  ? "bg-white dark:bg-slate-900 text-slate-900 dark:text-slate-50 shadow-sm ring-1 ring-slate-200 dark:ring-slate-700"
                  : "text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-50"
              }`}
            >
              <TableIcon size={13} />
              <span className="hidden sm:inline">Table</span>
            </button>
          </div>
        </div>

        {/* Active filter chips */}
        {hasFilters && (
          <div className="flex items-center gap-1.5 flex-wrap">
            {categoryFilter && <FilterChip label="Category" value={categoryFilter} onRemove={() => setCategoryFilter("")} />}
            {methodFilter && <FilterChip label="Method" value={methodFilter} onRemove={() => setMethodFilter("")} />}
            {sourceFilter && <FilterChip label="Source" value={sourceFilter} onRemove={() => setSourceFilter("")} />}
            {debouncedSearch && <FilterChip label="Search" value={debouncedSearch} onRemove={() => setSearch("")} />}
            <button
              onClick={clearFilters}
              className="text-[11px] font-medium text-cyan-600 dark:text-cyan-400 hover:underline px-2 py-1"
            >
              Clear all
            </button>
          </div>
        )}
      </div>

      {/* Date-grouped feed */}
      <Card>
        <CardContent className="pt-4">
          {isLoading ? (
            <div className="space-y-2">
              {Array.from({ length: 8 }).map((_, i) => <Skeleton key={i} className="h-14 rounded-lg" />)}
            </div>
          ) : items.length === 0 ? (
            <EmptyState
              icon={<ReceiptIcon size={20} />}
              title={hasFilters || period !== "all" ? "No expenses match your filters" : "No expenses yet"}
              description={hasFilters || period !== "all" ? "Try a different period, clear filters, or search for a different term." : "Record your first expense via WhatsApp, voice, or click + Add Expense above."}
              action={hasPermission(Permission.RecordExpenses) && !hasFilters && period === "all" ? (
                <Button onClick={() => setAdding(true)}>+ Add Expense</Button>
              ) : undefined}
            />
          ) : viewMode === "list" ? (
            <>
              {/* Select-all header (only shown when user has edit permission) */}
              {hasPermission(Permission.RecordExpenses) && (
                <div className="flex items-center gap-3 px-2 py-2 mb-1 border-b border-slate-200 dark:border-slate-800">
                  <input
                    type="checkbox"
                    checked={visibleItems.length > 0 && visibleItems.every((p) => selectedIds.has(p.id))}
                    onChange={() => toggleSelectAll(visibleItems)}
                    className="h-4 w-4 rounded border-slate-300 dark:border-slate-700 accent-cyan-500"
                    aria-label="Select all visible"
                  />
                  <p className="text-[11px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">
                    {selectedIds.size > 0 ? `${selectedIds.size} of ${visibleItems.length} selected` : `${visibleItems.length} expense${visibleItems.length === 1 ? "" : "s"}`}
                  </p>
                </div>
              )}
              <div className="divide-y divide-slate-100 dark:divide-slate-800 -mx-2">
                {groups.map((group) => (
                  <div key={group.key}>
                    {/* Day header */}
                    <div className="px-2 py-2 flex items-center justify-between bg-slate-50/60 dark:bg-slate-950/40">
                      <span className="text-[11px] font-semibold uppercase tracking-wider text-slate-600 dark:text-slate-400">
                        {group.label}
                        <span className="text-slate-400 dark:text-slate-500 font-normal ml-2">
                          · {group.date.toLocaleDateString(undefined, { month: "short", day: "numeric", year: "numeric" })}
                        </span>
                      </span>
                      <span className="text-[11px] text-slate-500 dark:text-slate-400 tabular-nums">
                        {group.items.length} · <span className="font-semibold text-rose-600 dark:text-rose-400">-{formatNaira(group.total)}</span>
                      </span>
                    </div>
                    {group.items.map((expense) => (
                      <ExpenseRow
                        key={expense.id}
                        expense={expense}
                        canEdit={hasPermission(Permission.RecordExpenses)}
                        selected={selectedIds.has(expense.id)}
                        onToggleSelect={() => toggleSelect(expense.id)}
                        onEdit={() => setEditing(expense)}
                        onDelete={() => setDeleting(expense)}
                      />
                    ))}
                  </div>
                ))}
              </div>

              {truncated && (
                <div className="mt-3 pt-3 border-t border-slate-200 dark:border-slate-800 text-center text-xs text-slate-500 dark:text-slate-400">
                  Showing latest {items.length} of {data?.totalCount.toLocaleString()} · narrow your period to see all
                </div>
              )}
            </>
          ) : (
            <ExpensesTable
              groups={groups}
              visibleItems={visibleItems}
              canEdit={hasPermission(Permission.RecordExpenses)}
              selectedIds={selectedIds}
              onToggleSelect={toggleSelect}
              onToggleSelectAll={() => toggleSelectAll(visibleItems)}
              onEdit={setEditing}
              onDelete={setDeleting}
              truncated={truncated}
              totalCount={data?.totalCount ?? 0}
            />
          )}
        </CardContent>
      </Card>

      {/* ── Sticky bulk action bar ───────────────────────────────────────── */}
      {selectedIds.size > 0 && (
        <div className="sticky bottom-4 z-20 mx-auto max-w-2xl">
          <div className="bg-slate-900 dark:bg-slate-100 text-white dark:text-slate-900 rounded-xl shadow-2xl ring-1 ring-slate-700 dark:ring-slate-300 px-4 py-3 flex items-center gap-3">
            <span className="text-sm font-semibold tabular-nums">
              {selectedIds.size} selected
            </span>
            <span className="flex-1" />
            {hasPermission(Permission.RecordExpenses) && (
              <button
                onClick={() => setBulkRecategorize(true)}
                disabled={bulkBusy}
                className="text-xs font-semibold px-3 py-1.5 rounded-md bg-white/10 dark:bg-slate-900/10 hover:bg-white/20 dark:hover:bg-slate-900/20 transition-colors disabled:opacity-50"
              >
                Recategorize
              </button>
            )}
            <button
              onClick={bulkExportCsv}
              disabled={bulkBusy}
              className="text-xs font-semibold px-3 py-1.5 rounded-md bg-white/10 dark:bg-slate-900/10 hover:bg-white/20 dark:hover:bg-slate-900/20 transition-colors disabled:opacity-50"
            >
              Export CSV
            </button>
            {hasPermission(Permission.RecordExpenses) && (
              <button
                onClick={() => setBulkConfirmDelete(true)}
                disabled={bulkBusy}
                className="text-xs font-semibold px-3 py-1.5 rounded-md bg-rose-500/90 hover:bg-rose-500 text-white transition-colors disabled:opacity-50"
              >
                Delete
              </button>
            )}
            <button
              onClick={clearSelection}
              disabled={bulkBusy}
              className="text-xs font-medium text-slate-300 dark:text-slate-600 hover:text-white dark:hover:text-slate-900 transition-colors"
            >
              Clear
            </button>
          </div>
        </div>
      )}

      {/* ── Floating + button ──────────────────────────────────────────────
          Always visible on mobile (where the header CTA scrolls away).
          On desktop, only after the user scrolls past the page header. */}
      {hasPermission(Permission.RecordExpenses) && selectedIds.size === 0 && (
        <button
          onClick={() => setAdding(true)}
          aria-label="Add expense"
          className={`fixed bottom-6 right-6 z-30 h-14 w-14 rounded-full bg-slate-900 dark:bg-slate-100 text-white dark:text-slate-900 shadow-lg ring-1 ring-slate-200 dark:ring-slate-800 hover:scale-105 active:scale-95 transition-all items-center justify-center ${
            scrolled ? "flex" : "flex lg:hidden"
          }`}
        >
          <span className="text-2xl font-light leading-none -mt-0.5">+</span>
        </button>
      )}

      {/* Bulk delete confirmation */}
      {bulkConfirmDelete && (
        <Dialog open={bulkConfirmDelete} onOpenChange={(o) => !o && setBulkConfirmDelete(false)}>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>Delete {selectedIds.size} expense{selectedIds.size === 1 ? "" : "s"}?</DialogTitle>
            </DialogHeader>
            <p className="text-sm text-slate-600 dark:text-slate-400">
              This action can&rsquo;t be undone.
            </p>
            <DialogFooter>
              <Button variant="outline" onClick={() => setBulkConfirmDelete(false)} disabled={bulkBusy}>Cancel</Button>
              <Button onClick={bulkDelete} disabled={bulkBusy} className="bg-rose-600 hover:bg-rose-700 text-white">
                {bulkBusy ? "Deleting…" : `Delete ${selectedIds.size}`}
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      )}

      {/* Bulk recategorize dialog */}
      {bulkRecategorize && (
        <BulkRecategorizeDialog
          count={selectedIds.size}
          onClose={() => setBulkRecategorize(false)}
          onApply={bulkRecategorizeApply}
          busy={bulkBusy}
        />
      )}

      <AddExpenseDialog open={adding} onClose={() => setAdding(false)} defaultExpenseType="operating" />
      <EditExpenseDialog
        expense={editing}
        open={editing !== null}
        onClose={() => setEditing(null)}
      />
      <DeleteExpenseDialog
        expense={deleting}
        open={deleting !== null}
        onClose={() => setDeleting(null)}
      />
    </div>
  );
}

function AddExpenseDialog({ open, onClose, defaultExpenseType }: { open: boolean; onClose: () => void; defaultExpenseType: "operating" | "cogs" }) {
  const qc = useQueryClient();
  const biz = useBusiness();
  const { toast } = useToast();
  const currencySymbol = CURRENCY_SYMBOLS[biz?.currency?.toUpperCase() ?? "NGN"] ?? biz?.currency ?? "\u20A6";
  const [form, setForm] = useState({ category: "General", amount: "", paidTo: "", notes: "", expenseType: defaultExpenseType, paymentMethod: "" });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Sync default when tab changes
  if (open && form.expenseType !== defaultExpenseType && form.amount === "") {
    setForm(f => ({ ...f, expenseType: defaultExpenseType }));
  }

  async function handleSave() {
    setSaving(true);
    setError(null);
    try {
      await api.post(`/expenses`, {
        category: form.category || "General",
        amount: Number(form.amount),
        paidTo: form.paidTo || undefined,
        notes: form.notes || undefined,
        expenseType: form.expenseType,
        paymentMethod: form.paymentMethod || undefined,
      });
      qc.invalidateQueries({ queryKey: ["expenses"] });
      qc.invalidateQueries({ queryKey: ["expense-filters"] });
      toast.success("Expense recorded", `${formatNaira(Number(form.amount))} \u00B7 ${form.category}`);
      handleClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to add expense");
    } finally {
      setSaving(false);
    }
  }

  function handleClose() {
    setForm({ category: "General", amount: "", paidTo: "", notes: "", expenseType: defaultExpenseType, paymentMethod: "" });
    setError(null);
    onClose();
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && handleClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Add Expense</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label>Expense Type</Label>
            <select
              className="w-full h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 text-sm bg-white dark:bg-slate-900"
              value={form.expenseType}
              onChange={(e) => setForm({ ...form, expenseType: e.target.value as "operating" | "cogs" })}
            >
              <option value="operating">Operating Expense</option>
              <option value="cogs">Inventory Expenses</option>
            </select>
          </div>
          <div>
            <Label>Category</Label>
            <select
              className="w-full h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 text-sm bg-white dark:bg-slate-900"
              value={(form.expenseType === "cogs" ? INVENTORY_CATEGORIES : OPERATING_CATEGORIES).includes(form.category) ? form.category : "Other"}
              onChange={(e) => {
                const val = e.target.value;
                if (val === "Other") {
                  setForm({ ...form, category: "" });
                } else {
                  const inventoryKeywords = ["inventory", "stock", "goods for", "raw material", "merchandise", "replenish", "restock"];
                  const isInventory = inventoryKeywords.some(k => val.toLowerCase().includes(k));
                  setForm({ ...form, category: val, expenseType: isInventory ? "cogs" : form.expenseType });
                }
              }}
            >
              {(form.expenseType === "cogs" ? INVENTORY_CATEGORIES : OPERATING_CATEGORIES).map((c) => (
                <option key={c} value={c}>{c}</option>
              ))}
              <option value="Other">Other</option>
            </select>
            {!(form.expenseType === "cogs" ? INVENTORY_CATEGORIES : OPERATING_CATEGORIES).includes(form.category) && (
              <Input
                className="mt-2"
                value={form.category}
                onChange={(e) => setForm({ ...form, category: e.target.value })}
                placeholder="Enter custom category"
              />
            )}
          </div>
          <div>
            <Label>Amount ({currencySymbol})</Label>
            <Input type="number" value={form.amount} onChange={(e) => setForm({ ...form, amount: e.target.value })} placeholder="5000" />
          </div>
          <div>
            <Label>Paid To (optional)</Label>
            <Input value={form.paidTo} onChange={(e) => setForm({ ...form, paidTo: e.target.value })} placeholder="e.g. Tunde, NEPA" />
          </div>
          <div>
            <Label>Notes (optional)</Label>
            <Input value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} />
          </div>
          <div>
            <Label>Payment Method (optional)</Label>
            <select
              className="w-full h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 text-sm bg-white dark:bg-slate-900"
              value={form.paymentMethod}
              onChange={(e) => setForm({ ...form, paymentMethod: e.target.value })}
            >
              <option value="">Not specified</option>
              <option value="Cash">Cash</option>
              <option value="Card">Card</option>
              <option value="Bank Transfer">Bank Transfer</option>
            </select>
          </div>
          {error && <p className="text-xs text-red-500">{error}</p>}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={handleClose} disabled={saving}>Cancel</Button>
          <Button onClick={handleSave} disabled={saving || !form.amount}>{saving ? "Saving…" : "Add Expense"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function EditExpenseDialog({
  expense,
  open,
  onClose,
}: {
  expense: ExpenseDto | null;
  open: boolean;
  onClose: () => void;
}) {
  const qc = useQueryClient();
  const biz = useBusiness();
  const { toast } = useToast();
  const currencySymbol = CURRENCY_SYMBOLS[biz?.currency?.toUpperCase() ?? "NGN"] ?? biz?.currency ?? "\u20A6";
  const [form, setForm] = useState({ category: "", amount: "", paidTo: "", notes: "", paymentMethod: "" });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (expense && form.category === "" && form.amount === "") {
    setForm({
      category: expense.category,
      amount: expense.amount.toString(),
      paidTo: expense.paidTo ?? "",
      notes: expense.notes ?? "",
      paymentMethod: expense.paymentMethod ?? "",
    });
  }

  async function handleSave() {
    if (!expense) return;
    setSaving(true);
    setError(null);
    try {
      await api.put(`/expenses/${expense.id}`, {
        category: form.category,
        amount: form.amount ? Number(form.amount) : null,
        paidTo: form.paidTo,
        notes: form.notes,
        paymentMethod: form.paymentMethod || undefined,
      });
      qc.invalidateQueries({ queryKey: ["expenses"] });
      qc.invalidateQueries({ queryKey: ["expense-filters"] });
      toast.success("Expense updated", `${formatNaira(Number(form.amount))} \u00B7 ${form.category}`);
      handleClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to save");
    } finally {
      setSaving(false);
    }
  }

  function handleClose() {
    setForm({ category: "", amount: "", paidTo: "", notes: "", paymentMethod: "" });
    setError(null);
    onClose();
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && handleClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Edit Expense</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label>Category</Label>
            {(() => {
              const expType = expense?.expenseType ?? "operating";
              const cats = expType === "cogs" ? INVENTORY_CATEGORIES : OPERATING_CATEGORIES;
              const isPreset = cats.includes(form.category);
              return (
                <>
                  <select
                    className="w-full h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 text-sm bg-white dark:bg-slate-900"
                    value={isPreset ? form.category : "Other"}
                    onChange={(e) => setForm({ ...form, category: e.target.value === "Other" ? "" : e.target.value })}
                  >
                    {cats.map((c) => <option key={c} value={c}>{c}</option>)}
                    <option value="Other">Other</option>
                  </select>
                  {!isPreset && (
                    <Input className="mt-2" value={form.category} onChange={(e) => setForm({ ...form, category: e.target.value })} placeholder="Enter custom category" />
                  )}
                </>
              );
            })()}
          </div>
          <div>
            <Label>Amount ({currencySymbol})</Label>
            <Input
              type="number"
              value={form.amount}
              onChange={(e) => setForm({ ...form, amount: e.target.value })}
            />
          </div>
          <div>
            <Label>Paid To</Label>
            <Input value={form.paidTo} onChange={(e) => setForm({ ...form, paidTo: e.target.value })} />
          </div>
          <div>
            <Label>Notes</Label>
            <Input value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} />
          </div>
          <div>
            <Label>Payment Method</Label>
            <select
              className="w-full h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 text-sm bg-white dark:bg-slate-900"
              value={form.paymentMethod}
              onChange={(e) => setForm({ ...form, paymentMethod: e.target.value })}
            >
              <option value="">Not specified</option>
              <option value="Cash">Cash</option>
              <option value="Card">Card</option>
              <option value="Bank Transfer">Bank Transfer</option>
            </select>
          </div>
          {error && <p className="text-xs text-red-500">{error}</p>}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={handleClose} disabled={saving}>Cancel</Button>
          <Button onClick={handleSave} disabled={saving}>{saving ? "Saving…" : "Save Changes"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function DeleteExpenseDialog({
  expense,
  open,
  onClose,
}: {
  expense: ExpenseDto | null;
  open: boolean;
  onClose: () => void;
}) {
  const qc = useQueryClient();
  const { toast } = useToast();
  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleDelete() {
    if (!expense) return;
    setDeleting(true);
    setError(null);
    try {
      await api.delete(`/expenses/${expense.id}`);
      qc.invalidateQueries({ queryKey: ["expenses"] });
      qc.invalidateQueries({ queryKey: ["expense-filters"] });
      toast.success("Expense deleted", `${formatNaira(expense.amount)} · ${expense.category}`);
      onClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to delete");
    } finally {
      setDeleting(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Delete Expense?</DialogTitle>
        </DialogHeader>
        <p className="text-sm text-slate-600 dark:text-slate-400">
          This will remove <strong>{expense?.category}</strong> ({formatNaira(expense?.amount ?? 0)}) from your records.
        </p>
        {error && <p className="text-xs text-red-500">{error}</p>}
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={deleting}>Cancel</Button>
          <Button onClick={handleDelete} disabled={deleting} className="bg-red-600 hover:bg-red-700 text-white">
            {deleting ? "Deleting…" : "Delete"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

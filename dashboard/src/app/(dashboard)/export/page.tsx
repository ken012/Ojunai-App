"use client";

import { useState, type ReactNode } from "react";
import { api } from "@/lib/api";
import { useBusiness } from "@/lib/data-sync";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Download,
  FileSpreadsheet,
  Users,
  Package,
  Receipt,
  ShoppingCart,
  Activity,
  DollarSign,
} from "lucide-react";
import type {
  PaginatedResult,
  SaleSummaryDto,
  ExpenseDto,
  ProductDto,
  ContactDto,
  ActivityFeedDto,
  PaginatedActivityResult,
  MonthlyPnlDto,
} from "@/lib/types";

// ─── CSV helper ──────────────────────────────────────────────────────────────

function downloadCsv(filename: string, headers: string[], rows: string[][]) {
  const csvContent = [
    headers.join(","),
    ...rows.map((row) =>
      row
        .map((cell) => {
          const str = String(cell ?? "");
          if (str.includes(",") || str.includes('"') || str.includes("\n"))
            return `"${str.replace(/"/g, '""')}"`;
          return str;
        })
        .join(",")
    ),
  ].join("\n");

  const blob = new Blob(["\ufeff" + csvContent], {
    type: "text/csv;charset=utf-8;",
  });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}

// ─── Date formatting for CSV cells ──────────────────────────────────────────

function fmtDate(dateStr: string | undefined | null, tz: string): string {
  if (!dateStr) return "";
  try {
    const d = new Date(dateStr);
    const parts = new Intl.DateTimeFormat("en-CA", {
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
      hour12: false,
      timeZone: tz,
    }).formatToParts(d);
    const get = (t: string) => parts.find((p) => p.type === t)?.value ?? "";
    return `${get("year")}-${get("month")}-${get("day")} ${get("hour")}:${get("minute")}`;
  } catch {
    return dateStr;
  }
}

function todayStamp(): string {
  return new Date().toISOString().slice(0, 10);
}

// ─── Reusable export card ───────────────────────────────────────────────────

function ExportCard({
  icon,
  title,
  description,
  onExport,
  loading,
  children,
}: {
  icon: ReactNode;
  title: string;
  description: string;
  onExport: () => void;
  loading: boolean;
  children?: ReactNode;
}) {
  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
          {icon}
          {title}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <p className="text-xs text-slate-400">{description}</p>
        {children}
        <Button onClick={onExport} disabled={loading} className="w-full">
          <Download size={14} className="mr-2" />
          {loading ? "Preparing..." : "Download CSV"}
        </Button>
      </CardContent>
    </Card>
  );
}

// ─── Date range filter ──────────────────────────────────────────────────────

function DateRangeFilter({
  from,
  to,
  onFromChange,
  onToChange,
}: {
  from: string;
  to: string;
  onFromChange: (v: string) => void;
  onToChange: (v: string) => void;
}) {
  return (
    <div className="grid grid-cols-2 gap-2">
      <div>
        <Label className="text-xs text-slate-500">From</Label>
        <Input
          type="date"
          value={from}
          onChange={(e) => onFromChange(e.target.value)}
          className="h-8 text-xs"
        />
      </div>
      <div>
        <Label className="text-xs text-slate-500">To</Label>
        <Input
          type="date"
          value={to}
          onChange={(e) => onToChange(e.target.value)}
          className="h-8 text-xs"
        />
      </div>
    </div>
  );
}

// ─── Main page ──────────────────────────────────────────────────────────────

export default function ExportPage() {
  const business = useBusiness();
  const tz = business?.timezone ?? "Africa/Lagos";

  // Loading flags
  const [salesLoading, setSalesLoading] = useState(false);
  const [expensesLoading, setExpensesLoading] = useState(false);
  const [productsLoading, setProductsLoading] = useState(false);
  const [contactsLoading, setContactsLoading] = useState(false);
  const [activityLoading, setActivityLoading] = useState(false);
  const [pnlLoading, setPnlLoading] = useState(false);

  // Date range filters
  const [salesFrom, setSalesFrom] = useState("");
  const [salesTo, setSalesTo] = useState("");
  const [expensesFrom, setExpensesFrom] = useState("");
  const [expensesTo, setExpensesTo] = useState("");
  const [activityFrom, setActivityFrom] = useState("");
  const [activityTo, setActivityTo] = useState("");

  // Expense type filter
  const [expenseType, setExpenseType] = useState<"all" | "operating" | "cogs">("all");

  // ── Sales export ────────────────────────────────────────────────────────

  async function exportSales() {
    setSalesLoading(true);
    try {
      const params = new URLSearchParams({ page: "1", pageSize: "10000" });
      if (salesFrom) params.set("from", salesFrom);
      if (salesTo) params.set("to", salesTo);
      const { data } = await api.get<{ data: PaginatedResult<SaleSummaryDto> }>(
        `/sales?${params}`
      );
      const items = data.data?.items ?? [];
      downloadCsv(
        `bizpilot-sales-${todayStamp()}.csv`,
        ["Date", "Customer", "Items", "Payment Status", "Payment Method", "Amount", "Source", "Recorded By"],
        items.map((s) => [
          fmtDate(s.createdAtUtc, tz),
          s.customerName ?? "",
          s.itemSummary ?? `${s.itemCount} item${s.itemCount !== 1 ? "s" : ""}`,
          s.paymentStatus,
          s.paymentMethod ?? "",
          String(s.totalAmount),
          s.source ?? "",
          s.recordedByName ?? "",
        ])
      );
    } catch {
      alert("Failed to export sales. Please try again.");
    } finally {
      setSalesLoading(false);
    }
  }

  // ── Expenses export ─────────────────────────────────────────────────────

  async function exportExpenses() {
    setExpensesLoading(true);
    try {
      const params = new URLSearchParams({ page: "1", pageSize: "10000" });
      if (expensesFrom) params.set("from", expensesFrom);
      if (expensesTo) params.set("to", expensesTo);
      if (expenseType !== "all") params.set("type", expenseType);
      const { data } = await api.get<{ data: PaginatedResult<ExpenseDto> }>(
        `/expenses?${params}`
      );
      const items = data.data?.items ?? [];
      downloadCsv(
        `bizpilot-expenses-${todayStamp()}.csv`,
        ["Date", "Category", "Expense Type", "Amount", "Paid To", "Notes", "Source", "Recorded By"],
        items.map((e) => [
          fmtDate(e.createdAtUtc, tz),
          e.category,
          e.expenseType,
          String(e.amount),
          e.paidTo ?? "",
          e.notes ?? "",
          e.source ?? "",
          (e as ExpenseDto & { recordedByName?: string }).recordedByName ?? "",
        ])
      );
    } catch {
      alert("Failed to export expenses. Please try again.");
    } finally {
      setExpensesLoading(false);
    }
  }

  // ── Products / Inventory export ─────────────────────────────────────────

  async function exportProducts() {
    setProductsLoading(true);
    try {
      const { data } = await api.get<{ data: PaginatedResult<ProductDto> }>(
        "/products?page=1&pageSize=10000"
      );
      const items = data.data?.items ?? [];
      downloadCsv(
        `bizpilot-inventory-${todayStamp()}.csv`,
        ["Name", "SKU", "Category", "Subcategory", "Unit", "Cost Price", "Selling Price", "Current Stock", "Low Stock Threshold", "Status"],
        items.map((p) => [
          p.name,
          p.sku ?? "",
          p.category ?? "",
          p.subcategory ?? "",
          p.unit,
          p.costPrice != null ? String(p.costPrice) : "",
          p.sellingPrice != null ? String(p.sellingPrice) : "",
          String(p.currentStock),
          String(p.lowStockThreshold),
          p.isActive ? "Active" : "Inactive",
        ])
      );
    } catch {
      alert("Failed to export products. Please try again.");
    } finally {
      setProductsLoading(false);
    }
  }

  // ── Contacts export ─────────────────────────────────────────────────────

  async function exportContacts() {
    setContactsLoading(true);
    try {
      const { data } = await api.get<{ data: PaginatedResult<ContactDto> }>(
        "/contacts?page=1&pageSize=10000"
      );
      const items = data.data?.items ?? [];
      downloadCsv(
        `bizpilot-contacts-${todayStamp()}.csv`,
        ["Name", "Type", "Phone", "Outstanding Receivable", "Outstanding Payable"],
        items.map((c) => [
          c.name,
          c.type,
          c.phoneNumber ?? "",
          String(c.outstandingReceivable),
          String(c.outstandingPayable),
        ])
      );
    } catch {
      alert("Failed to export contacts. Please try again.");
    } finally {
      setContactsLoading(false);
    }
  }

  // ── Activity log export ─────────────────────────────────────────────────

  async function exportActivity() {
    setActivityLoading(true);
    try {
      const params = new URLSearchParams({ page: "1", pageSize: "10000" });
      if (activityFrom) params.set("startDate", activityFrom);
      if (activityTo) params.set("endDate", activityTo);
      const { data } = await api.get<{ data: PaginatedActivityResult }>(
        `/dashboard/activity?${params}`
      );
      const items = data.data?.items ?? [];
      downloadCsv(
        `bizpilot-activity-${todayStamp()}.csv`,
        ["Ref ID", "Type", "Description", "Contact", "Amount", "Source", "Recorded By", "Date"],
        items.map((a) => [
          a.refId,
          a.type,
          a.description,
          a.contactName ?? "",
          a.amount != null ? String(a.amount) : "",
          a.source ?? "",
          a.recordedBy ?? "",
          fmtDate(a.createdAtUtc, tz),
        ])
      );
    } catch {
      alert("Failed to export activity log. Please try again.");
    } finally {
      setActivityLoading(false);
    }
  }

  // ── Monthly P&L export ─────────────────────────────────────────────────

  async function exportPnl() {
    setPnlLoading(true);
    try {
      const { data } = await api.get<{ data: MonthlyPnlDto }>(
        "/reports/monthly-pnl"
      );
      const pnl = data.data;
      if (!pnl) {
        alert("No P&L data available.");
        return;
      }
      downloadCsv(
        `bizpilot-pnl-${todayStamp()}.csv`,
        ["Month", "Revenue", "Cost of Goods Sold", "Gross Profit", "Operating Expenses", "Net Profit", "Gross Margin %", "Net Margin %"],
        [
          [
            pnl.month,
            String(pnl.revenue),
            String(pnl.costOfGoodsSold),
            String(pnl.grossProfit),
            String(pnl.operatingExpenses),
            String(pnl.netProfit),
            String(pnl.grossMarginPercent),
            String(pnl.netMarginPercent),
          ],
          [
            pnl.previousMonth,
            String(pnl.previousRevenue),
            String(pnl.previousCostOfGoodsSold),
            String(pnl.previousGrossProfit),
            String(pnl.previousOperatingExpenses),
            String(pnl.previousNetProfit),
            "",
            "",
          ],
        ]
      );
    } catch {
      alert("Failed to export P&L. Please try again.");
    } finally {
      setPnlLoading(false);
    }
  }

  // ── Render ──────────────────────────────────────────────────────────────

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-2xl font-bold text-slate-900">Export & Share</h2>
        <p className="text-slate-500 text-sm mt-0.5">
          Download your business data as CSV files
        </p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {/* Sales */}
        <ExportCard
          icon={<ShoppingCart size={16} className="text-emerald-600" />}
          title="Sales"
          description="All sales transactions including customer, items, payment status, and amounts."
          onExport={exportSales}
          loading={salesLoading}
        >
          <DateRangeFilter
            from={salesFrom}
            to={salesTo}
            onFromChange={setSalesFrom}
            onToChange={setSalesTo}
          />
        </ExportCard>

        {/* Expenses */}
        <ExportCard
          icon={<Receipt size={16} className="text-orange-600" />}
          title="Expenses"
          description="All expense entries with category, type, and payment details."
          onExport={exportExpenses}
          loading={expensesLoading}
        >
          <DateRangeFilter
            from={expensesFrom}
            to={expensesTo}
            onFromChange={setExpensesFrom}
            onToChange={setExpensesTo}
          />
          <div>
            <Label className="text-xs text-slate-500">Expense Type</Label>
            <select
              className="w-full h-8 px-2 rounded-md border border-slate-200 text-xs"
              value={expenseType}
              onChange={(e) => setExpenseType(e.target.value as "all" | "operating" | "cogs")}
            >
              <option value="all">All types</option>
              <option value="operating">Operating expenses</option>
              <option value="cogs">Cost of goods sold</option>
            </select>
          </div>
        </ExportCard>

        {/* Inventory / Products */}
        <ExportCard
          icon={<Package size={16} className="text-sky-600" />}
          title="Inventory / Products"
          description="Full product catalog with pricing, stock levels, and status."
          onExport={exportProducts}
          loading={productsLoading}
        />

        {/* Contacts */}
        <ExportCard
          icon={<Users size={16} className="text-violet-600" />}
          title="Contacts"
          description="Customers and suppliers with phone numbers and outstanding balances."
          onExport={exportContacts}
          loading={contactsLoading}
        />

        {/* Activity Log */}
        <ExportCard
          icon={<Activity size={16} className="text-slate-600" />}
          title="Activity Log"
          description="Full audit trail of all business transactions and events."
          onExport={exportActivity}
          loading={activityLoading}
        >
          <DateRangeFilter
            from={activityFrom}
            to={activityTo}
            onFromChange={setActivityFrom}
            onToChange={setActivityTo}
          />
        </ExportCard>

        {/* Monthly P&L */}
        <ExportCard
          icon={<DollarSign size={16} className="text-emerald-600" />}
          title="Monthly P&L"
          description="Profit & loss summary for the current and previous month."
          onExport={exportPnl}
          loading={pnlLoading}
        />
      </div>
    </div>
  );
}

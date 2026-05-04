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
  Printer,
  ClipboardList,
  Briefcase,
} from "lucide-react";
import type {
  PaginatedResult,
  SaleSummaryDto,
  ExpenseDto,
  ProductDto,
  ContactDto,
  PaginatedActivityResult,
  MonthlyPnlDto,
  DashboardOverviewDto,
  DailySummaryDto,
  CashPositionDto,
  OutstandingBalanceDto,
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

// ─── Print-friendly PDF helper ───────────────────────────────────────────────

function printReport(title: string, headers: string[], rows: string[][], businessName: string) {
  const win = window.open("", "_blank");
  if (!win) return;

  const html = `<!DOCTYPE html>
<html><head>
<title>${title} — ${businessName}</title>
<style>
  body { font-family: Arial, sans-serif; margin: 20px; font-size: 12px; }
  h1 { font-size: 18px; margin-bottom: 4px; }
  .meta { color: #666; font-size: 11px; margin-bottom: 16px; }
  table { width: 100%; border-collapse: collapse; }
  th { background: #f1f5f9; text-align: left; padding: 6px 8px; border: 1px solid #e2e8f0; font-size: 11px; }
  td { padding: 5px 8px; border: 1px solid #e2e8f0; font-size: 11px; }
  tr:nth-child(even) { background: #f8fafc; }
  .total { font-weight: bold; background: #f1f5f9; }
  @media print { body { margin: 0; } }
</style>
</head><body>
<h1>${title}</h1>
<p class="meta">${businessName} — Generated ${new Date().toLocaleDateString()}</p>
<table>
<thead><tr>${headers.map(h => `<th>${h}</th>`).join("")}</tr></thead>
<tbody>${rows.map(row => `<tr>${row.map(cell => `<td>${cell ?? ""}</td>`).join("")}</tr>`).join("")}</tbody>
</table>
<script>window.print();</script>
</body></html>`;

  win.document.write(html);
  win.document.close();
}

// ─── Rich multi-section print helper ────────────────────────────────────────

function printRichReport(title: string, sections: { heading: string; content: string }[], businessName: string) {
  const win = window.open("", "_blank");
  if (!win) return;

  const html = `<!DOCTYPE html>
<html><head>
<title>${title} — ${businessName}</title>
<style>
  body { font-family: Arial, sans-serif; margin: 24px; font-size: 12px; color: #1e293b; }
  h1 { font-size: 20px; margin-bottom: 4px; border-bottom: 2px solid #06b6d4; padding-bottom: 8px; }
  .meta { color: #64748b; font-size: 11px; margin-bottom: 20px; }
  h2 { font-size: 14px; margin-top: 20px; margin-bottom: 8px; color: #1e40af; }
  table { width: 100%; border-collapse: collapse; margin-bottom: 12px; }
  th { background: #f1f5f9; text-align: left; padding: 6px 8px; border: 1px solid #e2e8f0; font-size: 11px; }
  td { padding: 5px 8px; border: 1px solid #e2e8f0; font-size: 11px; }
  tr:nth-child(even) { background: #f8fafc; }
  .summary-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 12px; margin-bottom: 16px; }
  .summary-box { background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 6px; padding: 10px; text-align: center; }
  .summary-box .label { font-size: 10px; color: #64748b; text-transform: uppercase; }
  .summary-box .value { font-size: 16px; font-weight: bold; margin-top: 4px; }
  @media print { body { margin: 0; } }
</style>
</head><body>
<h1>${title}</h1>
<p class="meta">${businessName} — Generated ${new Date().toLocaleDateString()}</p>
${sections.map(s => `<h2>${s.heading}</h2>${s.content}`).join("")}
<script>window.print();</script>
</body></html>`;

  win.document.write(html);
  win.document.close();
}

// ─── Reusable export card ───────────────────────────────────────────────────

function ExportCard({
  icon,
  title,
  description,
  onExport,
  onPrint,
  loading,
  children,
}: {
  icon: ReactNode;
  title: string;
  description: string;
  onExport: () => void;
  onPrint: () => void;
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
        <div className="flex gap-2">
          <Button onClick={onExport} disabled={loading} className="flex-1">
            <Download size={14} className="mr-1" />
            {loading ? "Preparing..." : "CSV"}
          </Button>
          <Button onClick={onPrint} disabled={loading} variant="outline" className="flex-1">
            <Printer size={14} className="mr-1" />
            PDF
          </Button>
        </div>
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
  const bizName = business?.name ?? "My Business";
  const currencyMeta: Record<string, string> = { NGN: "\u20A6", GHS: "GH\u20B5", USD: "$", GBP: "\u00A3", KES: "KSh", ZAR: "R", TZS: "TSh", UGX: "USh", RWF: "RF", XAF: "FCFA", XOF: "CFA", EGP: "E\u00A3", ETB: "Br" };
  const cs = currencyMeta[business?.currency?.toUpperCase() ?? "NGN"] ?? business?.currency ?? "\u20A6";

  // Loading flags
  const [salesLoading, setSalesLoading] = useState(false);
  const [expensesLoading, setExpensesLoading] = useState(false);
  const [productsLoading, setProductsLoading] = useState(false);
  const [contactsLoading, setContactsLoading] = useState(false);
  const [activityLoading, setActivityLoading] = useState(false);
  const [pnlLoading, setPnlLoading] = useState(false);
  const [reportLoading, setReportLoading] = useState(false);
  const [taxLoading, setTaxLoading] = useState(false);

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

  async function handleSales(mode: "csv" | "print") {
    setSalesLoading(true);
    try {
      const params = new URLSearchParams({ page: "1", pageSize: "10000" });
      if (salesFrom) params.set("from", salesFrom);
      if (salesTo) params.set("to", salesTo);
      const { data } = await api.get<{ data: PaginatedResult<SaleSummaryDto> }>(
        `/sales?${params}`
      );
      const items = data.data?.items ?? [];
      const headers = ["Date", "Customer", "Items", "Payment Status", "Payment Method", "Amount", "Source", "Recorded By"];
      const rows = items.map((s) => [
        fmtDate(s.createdAtUtc, tz),
        s.customerName ?? "",
        s.itemSummary ?? `${s.itemCount} item${s.itemCount !== 1 ? "s" : ""}`,
        s.paymentStatus,
        s.paymentMethod ?? "",
        String(s.totalAmount),
        s.source ?? "",
        s.recordedByName ?? "",
      ]);
      if (mode === "csv") downloadCsv(`ojunai-sales-${todayStamp()}.csv`, headers, rows);
      else printReport("Sales Report", headers, rows, bizName);
    } catch {
      alert("Failed to export sales. Please try again.");
    } finally {
      setSalesLoading(false);
    }
  }

  // ── Expenses export ─────────────────────────────────────────────────────

  async function handleExpenses(mode: "csv" | "print") {
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
      const headers = ["Date", "Category", "Expense Type", "Amount", "Paid To", "Notes", "Source", "Recorded By"];
      const rows = items.map((e) => [
        fmtDate(e.createdAtUtc, tz),
        e.category,
        e.expenseType,
        String(e.amount),
        e.paidTo ?? "",
        e.notes ?? "",
        e.source ?? "",
        (e as ExpenseDto & { recordedByName?: string }).recordedByName ?? "",
      ]);
      if (mode === "csv") downloadCsv(`ojunai-expenses-${todayStamp()}.csv`, headers, rows);
      else printReport("Expenses Report", headers, rows, bizName);
    } catch {
      alert("Failed to export expenses. Please try again.");
    } finally {
      setExpensesLoading(false);
    }
  }

  // ── Products / Inventory export ─────────────────────────────────────────

  async function handleProducts(mode: "csv" | "print") {
    setProductsLoading(true);
    try {
      const { data } = await api.get<{ data: PaginatedResult<ProductDto> }>(
        "/products?page=1&pageSize=10000"
      );
      const items = data.data?.items ?? [];
      const headers = ["Name", "SKU", "Category", "Subcategory", "Unit", "Cost Price", "Selling Price", "Current Stock", "Low Stock Threshold", "Status"];
      const rows = items.map((p) => [
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
      ]);
      if (mode === "csv") downloadCsv(`ojunai-inventory-${todayStamp()}.csv`, headers, rows);
      else printReport("Inventory Report", headers, rows, bizName);
    } catch {
      alert("Failed to export products. Please try again.");
    } finally {
      setProductsLoading(false);
    }
  }

  // ── Contacts export ─────────────────────────────────────────────────────

  async function handleContacts(mode: "csv" | "print") {
    setContactsLoading(true);
    try {
      const { data } = await api.get<{ data: PaginatedResult<ContactDto> }>(
        "/contacts?page=1&pageSize=10000"
      );
      const items = data.data?.items ?? [];
      const headers = ["Name", "Type", "Phone", "Outstanding Receivable", "Outstanding Payable"];
      const rows = items.map((c) => [
        c.name,
        c.type,
        c.phoneNumber ?? "",
        String(c.outstandingReceivable),
        String(c.outstandingPayable),
      ]);
      if (mode === "csv") downloadCsv(`ojunai-contacts-${todayStamp()}.csv`, headers, rows);
      else printReport("Contacts Report", headers, rows, bizName);
    } catch {
      alert("Failed to export contacts. Please try again.");
    } finally {
      setContactsLoading(false);
    }
  }

  // ── Activity log export ─────────────────────────────────────────────────

  async function handleActivity(mode: "csv" | "print") {
    setActivityLoading(true);
    try {
      const params = new URLSearchParams({ page: "1", pageSize: "10000" });
      if (activityFrom) params.set("startDate", activityFrom);
      if (activityTo) params.set("endDate", activityTo);
      const { data } = await api.get<{ data: PaginatedActivityResult }>(
        `/dashboard/activity?${params}`
      );
      const items = data.data?.items ?? [];
      const headers = ["Ref ID", "Type", "Description", "Contact", "Amount", "Source", "Recorded By", "Date"];
      const rows = items.map((a) => [
        a.refId,
        a.type,
        a.description,
        a.contactName ?? "",
        a.amount != null ? String(a.amount) : "",
        a.source ?? "",
        a.recordedBy ?? "",
        fmtDate(a.createdAtUtc, tz),
      ]);
      if (mode === "csv") downloadCsv(`ojunai-activity-${todayStamp()}.csv`, headers, rows);
      else printReport("Activity Log", headers, rows, bizName);
    } catch {
      alert("Failed to export activity log. Please try again.");
    } finally {
      setActivityLoading(false);
    }
  }

  // ── Monthly P&L export ─────────────────────────────────────────────────

  async function handlePnl(mode: "csv" | "print") {
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
      const headers = ["Month", "Revenue", "Cost of Goods Sold", "Gross Profit", "Operating Expenses", "Net Profit", "Gross Margin %", "Net Margin %"];
      const rows = [
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
      ];
      if (mode === "csv") downloadCsv(`ojunai-pnl-${todayStamp()}.csv`, headers, rows);
      else printReport("Profit & Loss Statement", headers, rows, bizName);
    } catch {
      alert("Failed to export P&L. Please try again.");
    } finally {
      setPnlLoading(false);
    }
  }

  // ── Monthly Business Report (smart multi-section) ──────────────────────

  async function handleMonthlyReport() {
    setReportLoading(true);
    try {
      const [overviewRes, dailyRes, cashRes, lowStockRes, receivablesRes] = await Promise.all([
        api.get<{ data: DashboardOverviewDto }>("/dashboard/overview"),
        api.get<{ data: DailySummaryDto }>("/reports/daily"),
        api.get<{ data: CashPositionDto }>("/reports/cash-position"),
        api.get<{ data: ProductDto[] }>("/products/low-stock"),
        api.get<{ data: OutstandingBalanceDto[] }>("/ledger/balances?type=receivable"),
      ]);

      const overview = overviewRes.data.data;
      const daily = dailyRes.data.data;
      const cash = cashRes.data.data;
      const lowStock = lowStockRes.data.data ?? [];
      const receivables = receivablesRes.data.data ?? [];

      const fmtNum = (n: number) => n.toLocaleString("en-NG", { minimumFractionDigits: 0 });

      const sections: { heading: string; content: string }[] = [];

      // Revenue Summary
      const monthlySales = overview?.monthlySales ?? cash?.totalSalesThisMonth ?? 0;
      const monthlyExpenses = overview?.monthlyExpenses ?? cash?.totalExpensesThisMonth ?? 0;
      const netProfit = monthlySales - monthlyExpenses;
      sections.push({
        heading: "Revenue Summary",
        content: `<div class="summary-grid">
          <div class="summary-box"><div class="label">Total Sales</div><div class="value">${cs}${fmtNum(monthlySales)}</div></div>
          <div class="summary-box"><div class="label">Total Expenses</div><div class="value">${cs}${fmtNum(monthlyExpenses)}</div></div>
          <div class="summary-box"><div class="label">Net Profit</div><div class="value" style="color:${netProfit >= 0 ? "#16a34a" : "#dc2626"}">${cs}${fmtNum(netProfit)}</div></div>
        </div>`,
      });

      // Today's Snapshot
      if (daily) {
        sections.push({
          heading: "Today\u2019s Snapshot",
          content: `<table>
            <thead><tr><th>Metric</th><th>Value</th></tr></thead>
            <tbody>
              <tr><td>Sales</td><td>${cs}${fmtNum(daily.totalSales)} (${daily.saleCount} transactions)</td></tr>
              <tr><td>Expenses</td><td>${cs}${fmtNum(daily.totalExpenses)}</td></tr>
              <tr><td>Net Cash In</td><td>${cs}${fmtNum(daily.netCashIn)}</td></tr>
            </tbody>
          </table>`,
        });
      }

      // Cash Position
      if (cash) {
        sections.push({
          heading: "Cash Position",
          content: `<table>
            <thead><tr><th>Metric</th><th>Amount</th></tr></thead>
            <tbody>
              <tr><td>Monthly Sales</td><td>${cs}${fmtNum(cash.totalSalesThisMonth)}</td></tr>
              <tr><td>Monthly Expenses</td><td>${cs}${fmtNum(cash.totalExpensesThisMonth)}</td></tr>
              <tr><td>Outstanding Receivables</td><td>${cs}${fmtNum(cash.outstandingReceivables)}</td></tr>
              <tr><td>Outstanding Payables</td><td>${cs}${fmtNum(cash.outstandingPayables)}</td></tr>
              <tr style="font-weight:bold;background:#f1f5f9"><td>Net Position</td><td>${cs}${fmtNum(cash.netPosition)}</td></tr>
            </tbody>
          </table>`,
        });
      }

      // Outstanding Receivables
      if (receivables.length > 0) {
        const totalOwed = receivables.reduce((s, r) => s + r.totalReceivable, 0);
        sections.push({
          heading: `Outstanding Receivables (${cs}${fmtNum(totalOwed)} total)`,
          content: `<table>
            <thead><tr><th>Contact</th><th>Type</th><th>Amount Owed</th></tr></thead>
            <tbody>${receivables
              .filter(r => r.totalReceivable > 0)
              .sort((a, b) => b.totalReceivable - a.totalReceivable)
              .map(r => `<tr><td>${r.contactName}</td><td>${r.contactType}</td><td>${cs}${fmtNum(r.totalReceivable)}</td></tr>`)
              .join("")}
            </tbody>
          </table>`,
        });
      }

      // Low Stock Alerts
      if (lowStock.length > 0) {
        sections.push({
          heading: `Low Stock Alerts (${lowStock.length} items)`,
          content: `<table>
            <thead><tr><th>Product</th><th>Current Stock</th><th>Threshold</th><th>Unit</th></tr></thead>
            <tbody>${lowStock
              .map(p => `<tr><td>${p.name}</td><td>${p.currentStock}</td><td>${p.lowStockThreshold}</td><td>${p.unit}</td></tr>`)
              .join("")}
            </tbody>
          </table>`,
        });
      }

      printRichReport("Monthly Business Report", sections, bizName);
    } catch (err) {
      console.error("Monthly report error:", err);
      alert("Failed to generate monthly report. Please try again.");
    } finally {
      setReportLoading(false);
    }
  }

  // ── Tax-Ready Package ──────────────────────────────────────────────────

  async function handleTaxExport() {
    setTaxLoading(true);
    try {
      const [salesRes, expensesRes, contactsRes] = await Promise.all([
        api.get<{ data: PaginatedResult<SaleSummaryDto> }>("/sales?page=1&pageSize=10000"),
        api.get<{ data: PaginatedResult<ExpenseDto> }>("/expenses?page=1&pageSize=10000"),
        api.get<{ data: PaginatedResult<ContactDto> }>("/contacts?page=1&pageSize=10000"),
      ]);

      const sales = salesRes.data.data?.items ?? [];
      const expenses = expensesRes.data.data?.items ?? [];
      const contacts = contactsRes.data.data?.items ?? [];

      downloadCsv(
        `ojunai-revenue-${todayStamp()}.csv`,
        ["Date", "Customer", "Items", "Payment Status", "Payment Method", "Amount", "Source"],
        sales.map((s) => [
          fmtDate(s.createdAtUtc, tz),
          s.customerName ?? "",
          s.itemSummary ?? `${s.itemCount} item${s.itemCount !== 1 ? "s" : ""}`,
          s.paymentStatus,
          s.paymentMethod ?? "",
          String(s.totalAmount),
          s.source ?? "",
        ])
      );

      await new Promise((r) => setTimeout(r, 500));

      downloadCsv(
        `ojunai-expenses-${todayStamp()}.csv`,
        ["Date", "Category", "Expense Type", "Amount", "Paid To", "Notes"],
        expenses.map((e) => [
          fmtDate(e.createdAtUtc, tz),
          e.category,
          e.expenseType,
          String(e.amount),
          e.paidTo ?? "",
          e.notes ?? "",
        ])
      );

      await new Promise((r) => setTimeout(r, 500));

      downloadCsv(
        `ojunai-contacts-balances-${todayStamp()}.csv`,
        ["Name", "Type", "Phone", "Outstanding Receivable", "Outstanding Payable"],
        contacts.map((c) => [
          c.name,
          c.type,
          c.phoneNumber ?? "",
          String(c.outstandingReceivable),
          String(c.outstandingPayable),
        ])
      );
    } catch {
      alert("Failed to generate tax package. Please try again.");
    } finally {
      setTaxLoading(false);
    }
  }

  // ── Render ──────────────────────────────────────────────────────────────

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-2xl font-bold text-slate-900">Export & Share</h2>
        <p className="text-slate-500 text-sm mt-0.5">
          Download your business data as CSV or print-friendly PDF
        </p>
      </div>

      {/* Monthly Business Report — premium card */}
      <Card className="border-blue-200 bg-gradient-to-r from-blue-50 to-indigo-50">
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-blue-800 flex items-center gap-2">
            <ClipboardList size={18} className="text-blue-600" />
            Monthly Business Report
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          <p className="text-xs text-blue-600">
            Comprehensive report combining revenue summary, cash position, outstanding receivables,
            and low stock alerts. Opens a print-friendly view you can save as PDF.
          </p>
          <Button
            onClick={handleMonthlyReport}
            disabled={reportLoading}
            className="w-full bg-blue-600 hover:bg-blue-700"
          >
            <FileSpreadsheet size={14} className="mr-2" />
            {reportLoading ? "Generating report..." : "Generate Monthly Report"}
          </Button>
        </CardContent>
      </Card>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {/* Sales */}
        <ExportCard
          icon={<ShoppingCart size={16} className="text-emerald-600" />}
          title="Sales"
          description="All sales transactions including customer, items, payment status, and amounts."
          onExport={() => handleSales("csv")}
          onPrint={() => handleSales("print")}
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
          onExport={() => handleExpenses("csv")}
          onPrint={() => handleExpenses("print")}
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
          icon={<Package size={16} className="text-cyan-600" />}
          title="Inventory / Products"
          description="Full product catalog with pricing, stock levels, and status."
          onExport={() => handleProducts("csv")}
          onPrint={() => handleProducts("print")}
          loading={productsLoading}
        />

        {/* Contacts */}
        <ExportCard
          icon={<Users size={16} className="text-violet-600" />}
          title="Contacts"
          description="Customers and suppliers with phone numbers and outstanding balances."
          onExport={() => handleContacts("csv")}
          onPrint={() => handleContacts("print")}
          loading={contactsLoading}
        />

        {/* Activity Log */}
        <ExportCard
          icon={<Activity size={16} className="text-slate-600" />}
          title="Activity Log"
          description="Full audit trail of all business transactions and events."
          onExport={() => handleActivity("csv")}
          onPrint={() => handleActivity("print")}
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
          onExport={() => handlePnl("csv")}
          onPrint={() => handlePnl("print")}
          loading={pnlLoading}
        />
      </div>

      {/* Tax-Ready Package */}
      <Card className="border-amber-200 bg-gradient-to-r from-amber-50 to-orange-50">
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-amber-800 flex items-center gap-2">
            <Briefcase size={18} className="text-amber-600" />
            Tax-Ready Package
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          <p className="text-xs text-amber-700">
            Download all financial data for your accountant.
            Includes: Revenue, Expenses, Contacts &amp; Balances — as three separate CSV files.
          </p>
          <Button
            onClick={handleTaxExport}
            disabled={taxLoading}
            className="w-full bg-amber-600 hover:bg-amber-700"
          >
            <Download size={14} className="mr-2" />
            {taxLoading ? "Preparing files..." : "Download All for Accountant"}
          </Button>
        </CardContent>
      </Card>
    </div>
  );
}

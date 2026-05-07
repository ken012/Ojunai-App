"use client";

import { useState, useRef, useEffect, useMemo, useCallback } from "react";
import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { hasPermission, Permission } from "@/lib/permissions";
import { usePlanStatus } from "@/lib/use-plan-status";
import { UpgradePrompt } from "@/components/upgrade-prompt";
import { useToast } from "@/components/toast";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Upload, Download, Copy, CheckCircle, AlertTriangle, Clock, ChevronLeft, ChevronRight, History, Undo2, Trash2 } from "lucide-react";

type ImportType = "inventory" | "sales" | "expenses" | "contacts" | "contacts-ledger";

const FORMATS: Record<ImportType, { headers: string; example: string; required: string; optional: string; permission: string; label: string }> = {
  inventory: {
    label: "Inventory (Products & Stock)",
    headers: "ProductName,Quantity,Unit,CostPrice,SellingPrice,Date",
    example: "Rice,100,bag,3000,5000,2026-03-15\nShampoo,50,bottle,1200,2500,2026-03-15\nCement,30,bag,4500,6000,2026-03-16",
    required: "ProductName, Quantity, Date",
    optional: "Unit (default: bag), CostPrice, SellingPrice. Category auto-detected.",
    permission: Permission.ManageStock,
  },
  sales: {
    label: "Sales",
    headers: "ProductName,Quantity,UnitPrice,CustomerName,PaymentStatus,PaymentMethod,Date",
    example: "Rice,5,5000,Emeka,Paid,Cash,2026-03-15\nShampoo,3,2500,Ngozi,Unpaid,,2026-03-16",
    required: "ProductName, Quantity, UnitPrice, Date",
    optional: "CustomerName, PaymentStatus (default: Paid), PaymentMethod (Cash, Card, Bank Transfer)",
    permission: Permission.RecordSales,
  },
  expenses: {
    label: "Expenses",
    headers: "Category,Amount,PaidTo,Notes,PaymentMethod,Date",
    example: "Transport,5000,Driver,Market run,Cash,2026-03-15\nRent,100000,Landlord,Monthly rent,Bank Transfer,2026-03-01\nElectricity,15000,NEPA,April bill,,2026-04-05",
    required: "Category, Amount, Date",
    optional: "PaidTo, Notes, ExpenseType (operating or cogs), PaymentMethod (Cash, Card, Bank Transfer)",
    permission: Permission.RecordExpenses,
  },
  contacts: {
    label: "Contacts Only",
    headers: "Name,Phone,Type",
    example: "Ada Okafor,08012345678,Customer\nMarket Mama,08098765432,Supplier\nTunde Bakare,,Customer",
    required: "Name",
    optional: "Phone, Type (Customer, Supplier, or Both — default: Customer)",
    permission: Permission.ManageDebts,
  },
  "contacts-ledger": {
    label: "Contacts with Debts",
    headers: "Name,Phone,Amount,LedgerType,Notes,Date",
    example: "Ada Okafor,08012345678,25000,Receivable,3 bags of rice on credit,2026-02-10\nMarket Mama,08098765432,50000,Payable,Bulk supply — pay by month end,2026-03-01\nTunde Bakare,,15000,Receivable,Outstanding from last week,2026-04-14",
    required: "Name, Amount, LedgerType (Receivable or Payable), Date",
    optional: "Phone, Type (auto-detected from LedgerType), Notes",
    permission: Permission.ManageDebts,
  },
};

// Matches the ImportJobDto returned by the API. Status values mirror the backend enum as strings.
type ImportJob = {
  id: string;
  type: string;
  status: "Queued" | "Running" | "Completed" | "Failed" | "RolledBack";
  fileName: string;
  totalRows: number;
  processedRows: number;
  successCount: number;
  errorCount: number;
  errors: string[];
  failureReason?: string;
  createdAtUtc: string;
  startedAtUtc?: string;
  completedAtUtc?: string;
  progressPercent: number;
};

type RowIssue = { row: number; column: string; issue: string; severity: "error" | "warning" };

function validateRows(headers: string[], rows: string[][], importType: string): RowIssue[] {
  const issues: RowIssue[] = [];
  const seen = new Map<string, number>();

  rows.forEach((row, idx) => {
    const rowNum = idx + 2; // +2 for header row + 0-index

    // Check for empty required fields based on import type
    headers.forEach((header, colIdx) => {
      const val = row[colIdx]?.trim() ?? "";
      const h = header.toLowerCase();

      // Required field checks
      if (importType === "inventory" && h.includes("name") && !val)
        issues.push({ row: rowNum, column: header, issue: "Product name is required", severity: "error" });
      if (importType === "sales" && h.includes("product") && !val)
        issues.push({ row: rowNum, column: header, issue: "Product name is required", severity: "error" });
      if (importType === "expenses" && h.includes("amount") && !val)
        issues.push({ row: rowNum, column: header, issue: "Amount is required", severity: "error" });

      // Numeric field checks
      if ((h.includes("price") || h.includes("amount") || h.includes("quantity") || h.includes("stock") || h.includes("cost")) && val) {
        const cleaned = val.replace(/[₦$£€,\s]/g, "");
        if (cleaned && isNaN(Number(cleaned)))
          issues.push({ row: rowNum, column: header, issue: `"${val}" is not a valid number`, severity: "error" });
        if (Number(cleaned) < 0)
          issues.push({ row: rowNum, column: header, issue: "Negative values not allowed", severity: "warning" });
      }
    });

    // Duplicate detection (all columns must match)
    const key = row.map(c => c?.trim().toLowerCase()).join("|");
    if (key && key !== row.map(() => "").join("|")) {
      if (seen.has(key)) {
        issues.push({ row: rowNum, column: headers[0], issue: `Duplicate of row ${seen.get(key)}`, severity: "warning" });
      } else {
        seen.set(key, rowNum);
      }
    }

    // Empty row check
    if (row.every(cell => !cell?.trim()))
      issues.push({ row: rowNum, column: "-", issue: "Empty row", severity: "warning" });
  });

  return issues;
}

export default function ImportPage() {
  const { toast } = useToast();
  const [type, setType] = useState<ImportType>("inventory");
  const [file, setFile] = useState<File | null>(null);
  const [allRows, setAllRows] = useState<string[][]>([]);
  const [headers, setHeaders] = useState<string[]>([]);
  const [totalRows, setTotalRows] = useState(0);
  const [submitting, setSubmitting] = useState(false);
  const [job, setJob] = useState<ImportJob | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [showErrors, setShowErrors] = useState(false);
  const [showIssues, setShowIssues] = useState(true);
  const [previewPage, setPreviewPage] = useState(1);
  const [rollingBack, setRollingBack] = useState(false);
  const [rollingBackId, setRollingBackId] = useState<string | null>(null);
  const [importMode, setImportMode] = useState("new_purchase");
  const previewPageSize = 50;
  const fileRef = useRef<HTMLInputElement>(null);
  const { data: planStatus } = usePlanStatus();

  const issues = useMemo(() =>
    allRows.length > 0 ? validateRows(headers, allRows, type) : [],
    [headers, allRows, type]
  );
  const errorCount = issues.filter(i => i.severity === "error").length;
  const warningCount = issues.filter(i => i.severity === "warning").length;
  const totalPreviewPages = Math.ceil(allRows.length / previewPageSize);
  const previewRows = allRows.slice((previewPage - 1) * previewPageSize, previewPage * previewPageSize);

  const baseFormat = FORMATS[type];
  const format = importMode === "price_update" ? {
    ...baseFormat,
    headers: "ProductName,CostPrice,SellingPrice",
    example: "Rice,3000,5000\nShampoo,1200,2500\nCement,4500,6000",
    required: "ProductName, and at least one of CostPrice or SellingPrice",
    optional: "Both price columns are optional individually — only provided values are updated.",
  } : baseFormat;
  const canImport = hasPermission(format.permission);
  const hasCsvImport = planStatus?.hasCsvImport ?? true;

  const { data: importHistory, refetch: refetchHistory } = useQuery({
    queryKey: ["import-history"],
    queryFn: async () => {
      const { data } = await api.get<{ data: ImportJob[] }>("/import/jobs?limit=20");
      return data.data ?? [];
    },
  });

  const handleHistoryRollback = useCallback(async (historyJobId: string) => {
    if (!confirm("Undo this entire import? All imported records will be deleted.")) return;
    setRollingBackId(historyJobId);
    try {
      await api.post(`/import/rollback/${historyJobId}`);
      refetchHistory();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      toast.error("Rollback failed", ax.response?.data?.errors?.[0] ?? undefined);
    } finally {
      setRollingBackId(null);
    }
  }, [refetchHistory, toast]);

  // Poll the job status until it reaches a terminal state. The backend updates ProcessedRows every 200
  // rows, so a 1.5s interval gives smooth progress without hammering the server.
  useEffect(() => {
    if (!job || job.status === "Completed" || job.status === "Failed" || job.status === "RolledBack") return;

    const interval = setInterval(async () => {
      try {
        const { data } = await api.get<{ data: ImportJob }>(`/import/jobs/${job.id}`);
        if (data.data) {
          setJob(data.data);
          if (data.data.status === "Completed" || data.data.status === "Failed") refetchHistory();
        }
      } catch {
        // Network blip during polling isn't fatal — the next tick will retry.
      }
    }, 1500);

    return () => clearInterval(interval);
  }, [job, refetchHistory]);

  if (planStatus && !hasCsvImport) {
    return (
      <div className="space-y-6">
        <div>
          <h2 className="text-2xl font-bold text-slate-900 dark:text-slate-50">Import</h2>
          <p className="text-slate-500 dark:text-slate-400 text-sm mt-0.5">Import products, sales, or expenses from CSV files</p>
        </div>
        <UpgradePrompt feature="CSV Import" plan="Pro">
          <p className="text-xs text-slate-400 dark:text-slate-500 mt-2">Import products, sales, and expenses from CSV files to save time on data entry.</p>
        </UpgradePrompt>
      </div>
    );
  }

  function handleFileSelect(f: File | null) {
    if (!f) return;
    setFile(f);
    setJob(null);
    setError(null);

    const reader = new FileReader();
    reader.onload = (e) => {
      const text = e.target?.result as string;
      if (!text) return;
      const lines = text.replace(/\r\n/g, "\n").replace(/\r/g, "\n").split("\n").filter(Boolean);
      if (lines.length < 2) { setError("File has no data rows."); return; }

      const delim = lines[0].split(";").length > lines[0].split(",").length ? ";" : ",";
      const hdrs = lines[0].split(delim).map(h => h.trim().replace(/^"|"$/g, ""));
      setHeaders(hdrs);
      setTotalRows(lines.length - 1);

      const rows = lines.slice(1).map(line =>
        line.split(delim).map(v => v.trim().replace(/^"|"$/g, ""))
      );
      setAllRows(rows);
      setPreviewPage(1);
    };
    reader.readAsText(f);
  }

  async function handleImport() {
    if ((!file && allRows.length === 0) || !canImport) return;
    setSubmitting(true);
    setError(null);
    setJob(null);

    try {
      // Rebuild CSV from (possibly edited) preview data
      const csvLines = [headers.join(","), ...allRows.map(row => row.map(c => c.includes(",") ? `"${c}"` : c).join(","))];
      const csvBlob = new Blob([csvLines.join("\n") + "\n"], { type: "text/csv" });
      const csvFile = new File([csvBlob], file?.name ?? "import.csv", { type: "text/csv" });

      const formData = new FormData();
      formData.append("file", csvFile);
      const hasMode = type === "inventory" || type === "sales" || type === "contacts-ledger";
      const url = hasMode ? `/import/${type}?mode=${importMode}` : `/import/${type}`;
      const { data } = await api.post<{ data: ImportJob }>(url, formData, {
        headers: { "Content-Type": "multipart/form-data" },
      });
      if (data.data) setJob(data.data);
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Import failed.");
    } finally {
      setSubmitting(false);
    }
  }

  function downloadTemplate() {
    const blob = new Blob([format.headers + "\n" + format.example + "\n"], { type: "text/csv" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `ojunai-${type}-template.csv`;
    a.click();
    URL.revokeObjectURL(url);
  }

  function copyFormat() {
    navigator.clipboard.writeText(format.headers + "\n" + format.example);
  }

  function resetForNewImport() {
    setFile(null);
    setAllRows([]);
    setHeaders([]);
    setTotalRows(0);
    setPreviewPage(1);
    setJob(null);
    setError(null);
    setImportMode(type === "sales" ? "new_sales" : type === "contacts-ledger" ? "new_debts" : "new_purchase");
    if (fileRef.current) fileRef.current.value = "";
  }

  const isTerminal = job?.status === "Completed" || job?.status === "Failed" || job?.status === "RolledBack";
  const isProcessing = job && !isTerminal;

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-2xl font-bold text-slate-900 dark:text-slate-50">Import Data</h2>
        <p className="text-slate-500 dark:text-slate-400 text-sm mt-0.5">Bulk import products, sales, or expenses from a CSV file</p>
      </div>

      <div>
        <Label>What are you importing?</Label>
        <select
          className="w-full max-w-xs h-9 px-2 mt-1 rounded-md border border-slate-200 dark:border-slate-800 text-sm bg-white dark:bg-slate-900"
          value={type}
          onChange={(e) => { const t = e.target.value as ImportType; setType(t); setImportMode(t === "sales" ? "new_sales" : t === "contacts-ledger" ? "new_debts" : "new_purchase"); resetForNewImport(); }}
          disabled={!!isProcessing}
        >
          {Object.entries(FORMATS).map(([key, f]) => (
            <option key={key} value={key}>{f.label}</option>
          ))}
        </select>
      </div>

      <Card>
        <CardContent className="pt-4">
          <p className="text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2">Expected CSV Format</p>
          <pre className="bg-slate-50 dark:bg-slate-950 border rounded-lg p-3 text-xs font-mono text-slate-700 dark:text-slate-300 overflow-x-auto">
            {format.headers + "\n" + format.example}
          </pre>
          <div className="mt-2 text-xs text-slate-500 dark:text-slate-400">
            <p><strong>Required:</strong> {format.required}</p>
            <p><strong>Optional:</strong> {format.optional}</p>
          </div>
          <div className="flex gap-2 mt-3">
            <Button size="sm" variant="outline" onClick={copyFormat}>
              <Copy size={14} className="mr-1" /> Copy Format
            </Button>
            <Button size="sm" variant="outline" onClick={downloadTemplate}>
              <Download size={14} className="mr-1" /> Download Template
            </Button>
          </div>
        </CardContent>
      </Card>

      {type === "inventory" && (
        <Card>
          <CardContent className="pt-4">
            <p className="text-sm font-semibold text-slate-700 dark:text-slate-300 mb-3">How should we handle this import?</p>
            <div className="space-y-3">
              <label className={`flex items-start gap-3 cursor-pointer rounded-lg border p-3 transition-colors ${importMode === "new_purchase" ? "border-cyan-300 bg-cyan-50/50" : "border-slate-200 dark:border-slate-800 hover:bg-slate-50 dark:hover:bg-slate-800"}`}>
                <input type="radio" name="inv-mode" value="new_purchase" checked={importMode === "new_purchase"} onChange={() => setImportMode("new_purchase")} className="mt-0.5" />
                <div>
                  <p className="text-sm font-medium text-slate-700 dark:text-slate-300">New purchase — I just bought this stock</p>
                  <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">Creates products, adds stock, and records an expense.</p>
                </div>
              </label>
              <label className={`flex items-start gap-3 cursor-pointer rounded-lg border p-3 transition-colors ${importMode === "existing_stock" ? "border-cyan-300 bg-cyan-50/50" : "border-slate-200 dark:border-slate-800 hover:bg-slate-50 dark:hover:bg-slate-800"}`}>
                <input type="radio" name="inv-mode" value="existing_stock" checked={importMode === "existing_stock"} onChange={() => setImportMode("existing_stock")} className="mt-0.5" />
                <div>
                  <p className="text-sm font-medium text-slate-700 dark:text-slate-300">Existing stock — I already own this, moving to Ojunai</p>
                  <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">Creates products and stock levels. No expense recorded since these were already paid for.</p>
                </div>
              </label>
              <label className={`flex items-start gap-3 cursor-pointer rounded-lg border p-3 transition-colors ${importMode === "price_update" ? "border-cyan-300 bg-cyan-50/50" : "border-slate-200 dark:border-slate-800 hover:bg-slate-50 dark:hover:bg-slate-800"}`}>
                <input type="radio" name="inv-mode" value="price_update" checked={importMode === "price_update"} onChange={() => setImportMode("price_update")} className="mt-0.5" />
                <div>
                  <p className="text-sm font-medium text-slate-700 dark:text-slate-300">Price update only — Just update my catalog prices</p>
                  <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">Updates cost and selling prices on existing products. Stock levels are not changed. New products are skipped.</p>
                </div>
              </label>
            </div>
          </CardContent>
        </Card>
      )}

      {type === "sales" && (
        <Card>
          <CardContent className="pt-4">
            <p className="text-sm font-semibold text-slate-700 dark:text-slate-300 mb-3">How should we handle this import?</p>
            <div className="space-y-3">
              <label className={`flex items-start gap-3 cursor-pointer rounded-lg border p-3 transition-colors ${importMode === "new_sales" ? "border-cyan-300 bg-cyan-50/50" : "border-slate-200 dark:border-slate-800 hover:bg-slate-50 dark:hover:bg-slate-800"}`}>
                <input type="radio" name="sales-mode" value="new_sales" checked={importMode === "new_sales"} onChange={() => setImportMode("new_sales")} className="mt-0.5" />
                <div>
                  <p className="text-sm font-medium text-slate-700 dark:text-slate-300">New sales — Record these sales and deduct stock</p>
                  <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">Deducts inventory and creates receivables for credit sales.</p>
                </div>
              </label>
              <label className={`flex items-start gap-3 cursor-pointer rounded-lg border p-3 transition-colors ${importMode === "historical_sales" ? "border-cyan-300 bg-cyan-50/50" : "border-slate-200 dark:border-slate-800 hover:bg-slate-50 dark:hover:bg-slate-800"}`}>
                <input type="radio" name="sales-mode" value="historical_sales" checked={importMode === "historical_sales"} onChange={() => setImportMode("historical_sales")} className="mt-0.5" />
                <div>
                  <p className="text-sm font-medium text-slate-700 dark:text-slate-300">Historical sales — Import for reporting only</p>
                  <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">Records sales for revenue history. Stock is not deducted and no receivables are created, since your current inventory already reflects these.</p>
                </div>
              </label>
            </div>
          </CardContent>
        </Card>
      )}

      {type === "contacts-ledger" && (
        <Card>
          <CardContent className="pt-4">
            <p className="text-sm font-semibold text-slate-700 dark:text-slate-300 mb-3">How should we handle this import?</p>
            <div className="space-y-3">
              <label className={`flex items-start gap-3 cursor-pointer rounded-lg border p-3 transition-colors ${importMode === "new_debts" ? "border-cyan-300 bg-cyan-50/50" : "border-slate-200 dark:border-slate-800 hover:bg-slate-50 dark:hover:bg-slate-800"}`}>
                <input type="radio" name="ledger-mode" value="new_debts" checked={importMode === "new_debts"} onChange={() => setImportMode("new_debts")} className="mt-0.5" />
                <div>
                  <p className="text-sm font-medium text-slate-700 dark:text-slate-300">New debts — Recording new money owed</p>
                  <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">Creates receivable or payable entries as new transactions.</p>
                </div>
              </label>
              <label className={`flex items-start gap-3 cursor-pointer rounded-lg border p-3 transition-colors ${importMode === "existing_debts" ? "border-cyan-300 bg-cyan-50/50" : "border-slate-200 dark:border-slate-800 hover:bg-slate-50 dark:hover:bg-slate-800"}`}>
                <input type="radio" name="ledger-mode" value="existing_debts" checked={importMode === "existing_debts"} onChange={() => setImportMode("existing_debts")} className="mt-0.5" />
                <div>
                  <p className="text-sm font-medium text-slate-700 dark:text-slate-300">Existing debts — Migrating balances I{"'"}m already tracking</p>
                  <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">Import the current outstanding balance for each contact. These show as {'"'}Opening balance{'"'} in the ledger so it{"'"}s clear they were carried over from your previous records.</p>
                </div>
              </label>
            </div>
          </CardContent>
        </Card>
      )}

      {canImport ? (
        <Card>
          <CardContent className="pt-4">
            <input
              ref={fileRef}
              type="file"
              accept=".csv"
              className="hidden"
              onChange={(e) => handleFileSelect(e.target.files?.[0] ?? null)}
              disabled={!!isProcessing}
            />
            <div
              onClick={() => { if (!isProcessing) fileRef.current?.click(); }}
              onDragOver={(e) => { e.preventDefault(); e.stopPropagation(); }}
              onDrop={(e) => { e.preventDefault(); e.stopPropagation(); if (!isProcessing) handleFileSelect(e.dataTransfer.files?.[0] ?? null); }}
              className={`border-2 border-dashed rounded-xl p-8 text-center transition-colors ${
                isProcessing
                  ? "border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-950 cursor-not-allowed"
                  : "border-slate-200 dark:border-slate-800 hover:border-cyan-400 hover:bg-cyan-50/30 cursor-pointer"
              }`}
            >
              <Upload size={28} className="mx-auto text-slate-400 dark:text-slate-500 mb-2" />
              {file ? (
                <p className="text-sm text-slate-700 dark:text-slate-300 font-medium">{file.name} <span className="text-slate-400 dark:text-slate-500">({(file.size / 1024).toFixed(1)} KB)</span></p>
              ) : (
                <>
                  <p className="text-sm text-slate-600 dark:text-slate-400">Drag & drop a CSV file here</p>
                  <p className="text-xs text-slate-400 dark:text-slate-500 mt-1">or click to browse (max 50MB, 100,000 rows)</p>
                </>
              )}
            </div>
          </CardContent>
        </Card>
      ) : (
        <Card>
          <CardContent className="pt-4 text-center py-8 text-slate-400 dark:text-slate-500">
            Your role does not have permission to import {type}.
          </CardContent>
        </Card>
      )}

      {allRows.length > 0 && !job && (
        <Card>
          <CardContent className="pt-4">
            <div className="flex items-center justify-between mb-2">
              <p className="text-sm font-semibold text-slate-700 dark:text-slate-300">
                {totalRows} rows total — Page {previewPage} of {totalPreviewPages}
              </p>
              <Badge variant="secondary">{totalRows} rows total</Badge>
            </div>

            {issues.length > 0 && (
              <div className={`rounded-lg px-3 py-2 mb-3 text-sm font-medium ${
                errorCount > 0 ? "bg-red-50 text-red-700 border border-red-200" : "bg-amber-50 text-amber-700 border border-amber-200"
              }`}>
                {errorCount > 0 && <span>{errorCount} error{errorCount !== 1 ? "s" : ""}</span>}
                {errorCount > 0 && warningCount > 0 && <span>, </span>}
                {warningCount > 0 && <span>{warningCount} warning{warningCount !== 1 ? "s" : ""}</span>}
                {errorCount > 0 && <span className="ml-2 text-xs font-normal">— Fix errors before importing</span>}
              </div>
            )}

            <div className="overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="text-xs w-10">#</TableHead>
                    {headers.map((h) => <TableHead key={h} className="text-xs">{h}</TableHead>)}
                    <TableHead className="text-xs w-8"></TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {previewRows.map((row, i) => {
                    const globalIdx = (previewPage - 1) * previewPageSize + i;
                    const rowNum = globalIdx + 2;
                    const rowIssues = issues.filter(iss => iss.row === rowNum);
                    const hasError = rowIssues.some(iss => iss.severity === "error");
                    const hasWarning = !hasError && rowIssues.some(iss => iss.severity === "warning");
                    return (
                      <TableRow
                        key={i}
                        className={hasError ? "bg-red-50" : hasWarning ? "bg-amber-50" : ""}
                      >
                        <TableCell className="text-xs text-slate-400 dark:text-slate-500">{rowNum}</TableCell>
                        {row.map((cell, j) => {
                          const cellIssue = rowIssues.find(iss => iss.column === headers[j]);
                          return (
                            <TableCell key={j} className="text-xs text-slate-600 dark:text-slate-400 p-0">
                              <input
                                className="w-full border-0 bg-transparent text-xs px-2 py-1.5 focus:bg-white focus:ring-1 focus:ring-cyan-300 rounded outline-none"
                                value={cell}
                                onChange={(e) => {
                                  const newRows = [...allRows];
                                  newRows[globalIdx] = [...newRows[globalIdx]];
                                  newRows[globalIdx][j] = e.target.value;
                                  setAllRows(newRows);
                                }}
                              />
                              {cellIssue && (
                                <span className={`block text-[10px] px-2 pb-1 ${cellIssue.severity === "error" ? "text-red-500" : "text-amber-500"}`}>
                                  {cellIssue.issue}
                                </span>
                              )}
                            </TableCell>
                          );
                        })}
                        <TableCell className="p-0 text-center">
                          <button
                            onClick={() => setAllRows(prev => prev.filter((_, idx) => idx !== globalIdx))}
                            className="p-1 rounded hover:bg-red-50 text-slate-400 dark:text-slate-500 hover:text-red-500"
                            title="Remove row"
                          >
                            <Trash2 size={12} />
                          </button>
                        </TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            </div>

            {totalPreviewPages > 1 && (
              <div className="flex items-center justify-center gap-3 mt-3">
                <Button
                  size="sm"
                  variant="outline"
                  disabled={previewPage <= 1}
                  onClick={() => setPreviewPage(p => Math.max(1, p - 1))}
                >
                  <ChevronLeft size={14} className="mr-1" /> Previous
                </Button>
                <span className="text-xs text-slate-500 dark:text-slate-400">
                  Page {previewPage} of {totalPreviewPages}
                </span>
                <Button
                  size="sm"
                  variant="outline"
                  disabled={previewPage >= totalPreviewPages}
                  onClick={() => setPreviewPage(p => Math.min(totalPreviewPages, p + 1))}
                >
                  Next <ChevronRight size={14} className="ml-1" />
                </Button>
              </div>
            )}

            {issues.length > 0 && (
              <div className="border rounded-lg p-3 mt-3 space-y-1">
                <button
                  onClick={() => setShowIssues(!showIssues)}
                  className="text-sm font-medium hover:underline"
                >
                  {showIssues ? "Hide" : "Show"} {issues.length} issue{issues.length !== 1 ? "s" : ""}: {errorCount} error{errorCount !== 1 ? "s" : ""}, {warningCount} warning{warningCount !== 1 ? "s" : ""}
                </button>
                {showIssues && (
                  <div className="max-h-40 overflow-y-auto space-y-1 mt-1">
                    {issues.map((issue, i) => (
                      <p key={i} className={`text-xs ${issue.severity === "error" ? "text-red-600" : "text-amber-600"}`}>
                        Row {issue.row}, {issue.column}: {issue.issue}
                      </p>
                    ))}
                  </div>
                )}
              </div>
            )}

            <div className="flex items-center justify-between mt-4 pt-4 border-t">
              <p className="text-sm text-slate-500 dark:text-slate-400">{totalRows} rows will be imported.</p>
              <Button onClick={handleImport} disabled={submitting || !canImport}>
                {submitting ? "Queueing..." : `Import ${totalRows} Rows`}
              </Button>
            </div>
          </CardContent>
        </Card>
      )}

      {error && (
        <Card className="border-red-200 bg-red-50">
          <CardContent className="pt-4">
            <p className="text-sm text-red-600">{error}</p>
          </CardContent>
        </Card>
      )}

      {/* Import History */}
      {importHistory && importHistory.length > 0 && !job && (
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm flex items-center gap-2">
              <History size={14} /> Import History
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="text-xs">Type</TableHead>
                    <TableHead className="text-xs">File</TableHead>
                    <TableHead className="text-xs">Date</TableHead>
                    <TableHead className="text-xs">Rows</TableHead>
                    <TableHead className="text-xs">Imported</TableHead>
                    <TableHead className="text-xs">Skipped</TableHead>
                    <TableHead className="text-xs">Status</TableHead>
                    <TableHead className="text-xs"></TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {importHistory.map((h) => (
                    <TableRow key={h.id}>
                      <TableCell className="text-xs capitalize">{h.type}</TableCell>
                      <TableCell className="text-xs text-slate-500 dark:text-slate-400 max-w-[120px] truncate">{h.fileName}</TableCell>
                      <TableCell className="text-xs text-slate-500 dark:text-slate-400">{new Date(h.createdAtUtc).toLocaleDateString()}</TableCell>
                      <TableCell className="text-xs">{h.totalRows}</TableCell>
                      <TableCell className="text-xs text-emerald-600">{h.successCount}</TableCell>
                      <TableCell className="text-xs text-amber-600">{h.errorCount}</TableCell>
                      <TableCell className="text-xs">
                        <Badge variant={
                          h.status === "Completed" ? "default"
                            : h.status === "Failed" ? "destructive"
                            : h.status === "RolledBack" ? "secondary"
                            : "outline"
                        } className="text-[10px]">
                          {h.status}
                        </Badge>
                      </TableCell>
                      <TableCell className="text-xs">
                        {h.status === "Completed" && h.successCount > 0 && (
                          <Button
                            variant="ghost"
                            size="sm"
                            className="text-red-500 hover:text-red-700 hover:bg-red-50 h-7 px-2 text-xs"
                            disabled={rollingBackId === h.id}
                            onClick={() => handleHistoryRollback(h.id)}
                          >
                            <Undo2 size={12} className="mr-1" />
                            {rollingBackId === h.id ? "..." : "Undo"}
                          </Button>
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          </CardContent>
        </Card>
      )}

      {/* Active job — progress bar while running, final summary once terminal */}
      {job && (
        <Card className={
          job.status === "RolledBack" ? "border-slate-300 dark:border-slate-700 bg-slate-50 dark:bg-slate-950"
            : job.status === "Completed" && job.errorCount === 0 ? "border-emerald-200"
            : job.status === "Completed" ? "border-amber-200"
            : job.status === "Failed" ? "border-red-200"
            : "border-cyan-200"
        }>
          <CardContent className="pt-4 space-y-3">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                {job.status === "Queued" && <Clock size={18} className="text-slate-400 dark:text-slate-500" />}
                {job.status === "Running" && <Clock size={18} className="text-cyan-500 animate-pulse" />}
                {job.status === "Completed" && job.errorCount === 0 && <CheckCircle size={18} className="text-emerald-500" />}
                {job.status === "Completed" && job.errorCount > 0 && <AlertTriangle size={18} className="text-amber-500" />}
                {job.status === "Failed" && <AlertTriangle size={18} className="text-red-500" />}
                {job.status === "RolledBack" && <Clock size={18} className="text-slate-400 dark:text-slate-500" />}
                <span className="text-sm font-semibold text-slate-900 dark:text-slate-50">
                  {job.status === "Queued" && "Queued — waiting to start"}
                  {job.status === "Running" && `Running — processing row ${job.processedRows.toLocaleString()} of ${job.totalRows.toLocaleString()}`}
                  {job.status === "Completed" && "Import complete"}
                  {job.status === "Failed" && "Import failed"}
                  {job.status === "RolledBack" && "This import was rolled back"}
                </span>
              </div>
              {isProcessing && (
                <span className="text-xs text-slate-500 dark:text-slate-400">{job.progressPercent}%</span>
              )}
            </div>

            {isProcessing && (
              <div className="w-full h-2 bg-slate-100 dark:bg-slate-800 rounded-full overflow-hidden">
                <div
                  className="h-full bg-cyan-500 transition-all duration-500 ease-out"
                  style={{ width: `${job.progressPercent}%` }}
                />
              </div>
            )}

            {isTerminal && (
              <div className="flex flex-wrap items-center gap-4 pt-1">
                {job.successCount > 0 && (
                  <div className="flex items-center gap-2 text-emerald-600">
                    <CheckCircle size={16} />
                    <span className="text-sm font-medium">{job.successCount.toLocaleString()} imported</span>
                  </div>
                )}
                {job.errorCount > 0 && (
                  <div className="flex items-center gap-2 text-amber-600">
                    <AlertTriangle size={16} />
                    <span className="text-sm font-medium">{job.errorCount.toLocaleString()} skipped</span>
                  </div>
                )}
                <span className="text-xs text-slate-400 dark:text-slate-500">
                  of {job.totalRows.toLocaleString()} rows total
                </span>
              </div>
            )}

            {job.status === "Failed" && job.failureReason && (
              <p className="text-sm text-red-600 bg-red-50 border border-red-200 rounded-lg p-3">
                {job.failureReason}
              </p>
            )}

            {isTerminal && job.errors.length > 0 && (
              <div>
                <button
                  onClick={() => setShowErrors(!showErrors)}
                  className="text-xs text-amber-600 hover:underline"
                >
                  {showErrors ? "Hide errors" : `Show ${job.errors.length} errors`}
                </button>
                {showErrors && (
                  <div className="mt-2 bg-amber-50 border border-amber-200 rounded-lg p-3 max-h-48 overflow-y-auto">
                    {job.errors.map((err, i) => (
                      <p key={i} className="text-xs text-amber-700">{err}</p>
                    ))}
                  </div>
                )}
              </div>
            )}

            {isTerminal && (
              <div className="pt-2 border-t flex items-center justify-between">
                <p className="text-xs text-slate-400 dark:text-slate-500">
                  {job.status === "RolledBack"
                    ? "All imported records were deleted."
                    : "A WhatsApp message has been sent to the business owner with a summary."}
                </p>
                <div className="flex items-center gap-2">
                  {job.status === "Completed" && job.successCount > 0 && (
                    <Button
                      variant="outline"
                      size="sm"
                      className="text-red-600 border-red-200 hover:bg-red-50"
                      disabled={rollingBack}
                      onClick={async () => {
                        if (!confirm("Undo this entire import? All imported records will be deleted.")) return;
                        setRollingBack(true);
                        try {
                          await api.post(`/import/rollback/${job.id}`);
                          const { data } = await api.get<{ data: ImportJob }>(`/import/jobs/${job.id}`);
                          if (data.data) setJob(data.data);
                          refetchHistory();
                        } catch (err: unknown) {
                          const ax = err as { response?: { data?: { errors?: string[] } } };
                          toast.error("Rollback failed", ax.response?.data?.errors?.[0] ?? undefined);
                        } finally {
                          setRollingBack(false);
                        }
                      }}
                    >
                      {rollingBack ? "Rolling back..." : "Undo Import"}
                    </Button>
                  )}
                  <Button size="sm" variant="outline" onClick={resetForNewImport}>
                    Start another import
                  </Button>
                </div>
              </div>
            )}
          </CardContent>
        </Card>
      )}
    </div>
  );
}

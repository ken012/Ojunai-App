"use client";

import { useState, useRef, useEffect } from "react";
import { api } from "@/lib/api";
import { hasPermission, Permission } from "@/lib/permissions";
import { usePlanStatus } from "@/lib/use-plan-status";
import { UpgradePrompt } from "@/components/upgrade-prompt";
import { Card, CardContent } from "@/components/ui/card";
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
import { Upload, Download, Copy, CheckCircle, AlertTriangle, Clock } from "lucide-react";

type ImportType = "inventory" | "sales" | "expenses" | "contacts" | "contacts-ledger";

const FORMATS: Record<ImportType, { headers: string; example: string; required: string; optional: string; permission: string; label: string }> = {
  inventory: {
    label: "Inventory (Products & Stock)",
    headers: "ProductName,Quantity,Unit,CostPrice,SellingPrice",
    example: "Rice,100,bag,3000,5000\nShampoo,50,bottle,1200,2500\nCement,30,bag,4500,6000",
    required: "ProductName, Quantity",
    optional: "Unit (default: bag), CostPrice, SellingPrice. Category auto-detected.",
    permission: Permission.ManageStock,
  },
  sales: {
    label: "Sales",
    headers: "ProductName,Quantity,UnitPrice,CustomerName,PaymentStatus",
    example: "Rice,5,5000,Emeka,Paid\nShampoo,3,2500,Ngozi,Unpaid",
    required: "ProductName, Quantity, UnitPrice",
    optional: "CustomerName, PaymentStatus (default: Paid)",
    permission: Permission.RecordSales,
  },
  expenses: {
    label: "Expenses",
    headers: "Category,Amount,PaidTo,Notes",
    example: "Transport,5000,Driver,Market run\nRent,100000,Landlord,Monthly rent\nElectricity,15000,NEPA,April bill",
    required: "Category, Amount",
    optional: "PaidTo, Notes",
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
    headers: "Name,Phone,Amount,LedgerType,Notes",
    example: "Ada Okafor,08012345678,25000,Receivable,3 bags of rice on credit\nMarket Mama,08098765432,50000,Payable,Bulk supply — pay by month end\nTunde Bakare,,15000,Receivable,Outstanding from last week",
    required: "Name, Amount, LedgerType (Receivable or Payable)",
    optional: "Phone, Type (auto-detected from LedgerType), Notes",
    permission: Permission.ManageDebts,
  },
};

// Matches the ImportJobDto returned by the API. Status values mirror the backend enum as strings.
type ImportJob = {
  id: string;
  type: string;
  status: "Queued" | "Running" | "Completed" | "Failed";
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

export default function ImportPage() {
  const [type, setType] = useState<ImportType>("inventory");
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<string[][]>([]);
  const [headers, setHeaders] = useState<string[]>([]);
  const [totalRows, setTotalRows] = useState(0);
  const [submitting, setSubmitting] = useState(false);
  const [job, setJob] = useState<ImportJob | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [showErrors, setShowErrors] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);
  const { data: planStatus } = usePlanStatus();

  const format = FORMATS[type];
  const canImport = hasPermission(format.permission);
  const hasCsvImport = planStatus?.hasCsvImport ?? true;

  // Poll the job status until it reaches a terminal state. The backend updates ProcessedRows every 200
  // rows, so a 1.5s interval gives smooth progress without hammering the server.
  useEffect(() => {
    if (!job || job.status === "Completed" || job.status === "Failed") return;

    const interval = setInterval(async () => {
      try {
        const { data } = await api.get<{ data: ImportJob }>(`/import/jobs/${job.id}`);
        if (data.data) setJob(data.data);
      } catch {
        // Network blip during polling isn't fatal — the next tick will retry.
      }
    }, 1500);

    return () => clearInterval(interval);
  }, [job]);

  if (planStatus && !hasCsvImport) {
    return (
      <div className="space-y-6">
        <div>
          <h2 className="text-2xl font-bold text-slate-900">Import</h2>
          <p className="text-slate-500 text-sm mt-0.5">Import products, sales, or expenses from CSV files</p>
        </div>
        <UpgradePrompt feature="CSV Import" plan="Pro">
          <p className="text-xs text-slate-400 mt-2">Import products, sales, and expenses from CSV files to save time on data entry.</p>
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

      const rows = lines.slice(1, 11).map(line =>
        line.split(delim).map(v => v.trim().replace(/^"|"$/g, ""))
      );
      setPreview(rows);
    };
    reader.readAsText(f);
  }

  async function handleImport() {
    if (!file || !canImport) return;
    setSubmitting(true);
    setError(null);
    setJob(null);

    try {
      const formData = new FormData();
      formData.append("file", file);
      const { data } = await api.post<{ data: ImportJob }>(`/import/${type}`, formData, {
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
    a.download = `bizpilot-${type}-template.csv`;
    a.click();
    URL.revokeObjectURL(url);
  }

  function copyFormat() {
    navigator.clipboard.writeText(format.headers + "\n" + format.example);
  }

  function resetForNewImport() {
    setFile(null);
    setPreview([]);
    setHeaders([]);
    setTotalRows(0);
    setJob(null);
    setError(null);
    if (fileRef.current) fileRef.current.value = "";
  }

  const isTerminal = job?.status === "Completed" || job?.status === "Failed";
  const isProcessing = job && !isTerminal;

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-2xl font-bold text-slate-900">Import Data</h2>
        <p className="text-slate-500 text-sm mt-0.5">Bulk import products, sales, or expenses from a CSV file</p>
      </div>

      <div>
        <Label>What are you importing?</Label>
        <select
          className="w-full max-w-xs h-9 px-2 mt-1 rounded-md border border-slate-200 text-sm bg-white"
          value={type}
          onChange={(e) => { setType(e.target.value as ImportType); resetForNewImport(); }}
          disabled={!!isProcessing}
        >
          {Object.entries(FORMATS).map(([key, f]) => (
            <option key={key} value={key}>{f.label}</option>
          ))}
        </select>
      </div>

      <Card>
        <CardContent className="pt-4">
          <p className="text-sm font-semibold text-slate-700 mb-2">Expected CSV Format</p>
          <pre className="bg-slate-50 border rounded-lg p-3 text-xs font-mono text-slate-700 overflow-x-auto">
            {format.headers + "\n" + format.example}
          </pre>
          <div className="mt-2 text-xs text-slate-500">
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
                  ? "border-slate-200 bg-slate-50 cursor-not-allowed"
                  : "border-slate-200 hover:border-sky-400 hover:bg-sky-50/30 cursor-pointer"
              }`}
            >
              <Upload size={28} className="mx-auto text-slate-400 mb-2" />
              {file ? (
                <p className="text-sm text-slate-700 font-medium">{file.name} <span className="text-slate-400">({(file.size / 1024).toFixed(1)} KB)</span></p>
              ) : (
                <>
                  <p className="text-sm text-slate-600">Drag & drop a CSV file here</p>
                  <p className="text-xs text-slate-400 mt-1">or click to browse (max 50MB, 100,000 rows)</p>
                </>
              )}
            </div>
          </CardContent>
        </Card>
      ) : (
        <Card>
          <CardContent className="pt-4 text-center py-8 text-slate-400">
            Your role does not have permission to import {type}.
          </CardContent>
        </Card>
      )}

      {preview.length > 0 && !job && (
        <Card>
          <CardContent className="pt-4">
            <div className="flex items-center justify-between mb-2">
              <p className="text-sm font-semibold text-slate-700">Preview (first {Math.min(preview.length, 10)} of {totalRows} rows)</p>
              <Badge variant="secondary">{totalRows} rows total</Badge>
            </div>
            <div className="overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    {headers.map((h) => <TableHead key={h} className="text-xs">{h}</TableHead>)}
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {preview.map((row, i) => (
                    <TableRow key={i}>
                      {row.map((cell, j) => (
                        <TableCell key={j} className="text-xs text-slate-600">{cell || <span className="text-slate-300">—</span>}</TableCell>
                      ))}
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>

            <div className="flex items-center justify-between mt-4 pt-4 border-t">
              <p className="text-sm text-slate-500">{totalRows} rows will be imported. This cannot be undone.</p>
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

      {/* Active job — progress bar while running, final summary once terminal */}
      {job && (
        <Card className={
          job.status === "Completed" && job.errorCount === 0 ? "border-emerald-200"
            : job.status === "Completed" ? "border-amber-200"
            : job.status === "Failed" ? "border-red-200"
            : "border-sky-200"
        }>
          <CardContent className="pt-4 space-y-3">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                {job.status === "Queued" && <Clock size={18} className="text-slate-400" />}
                {job.status === "Running" && <Clock size={18} className="text-sky-500 animate-pulse" />}
                {job.status === "Completed" && job.errorCount === 0 && <CheckCircle size={18} className="text-emerald-500" />}
                {job.status === "Completed" && job.errorCount > 0 && <AlertTriangle size={18} className="text-amber-500" />}
                {job.status === "Failed" && <AlertTriangle size={18} className="text-red-500" />}
                <span className="text-sm font-semibold text-slate-900">
                  {job.status === "Queued" && "Queued — waiting to start"}
                  {job.status === "Running" && `Running — processing row ${job.processedRows.toLocaleString()} of ${job.totalRows.toLocaleString()}`}
                  {job.status === "Completed" && "Import complete"}
                  {job.status === "Failed" && "Import failed"}
                </span>
              </div>
              {isProcessing && (
                <span className="text-xs text-slate-500">{job.progressPercent}%</span>
              )}
            </div>

            {isProcessing && (
              <div className="w-full h-2 bg-slate-100 rounded-full overflow-hidden">
                <div
                  className="h-full bg-sky-500 transition-all duration-500 ease-out"
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
                <span className="text-xs text-slate-400">
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
                <p className="text-xs text-slate-400">
                  A WhatsApp message has been sent to the business owner with a summary.
                </p>
                <Button size="sm" variant="outline" onClick={resetForNewImport}>
                  Start another import
                </Button>
              </div>
            )}
          </CardContent>
        </Card>
      )}
    </div>
  );
}

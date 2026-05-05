"use client";

export const dynamic = "force-dynamic";

import { useState, useEffect } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { hasPermission, Permission } from "@/lib/permissions";
import { useBusiness } from "@/lib/data-sync";
import { useToast } from "@/components/toast";
import { formatNaira, formatDateTime } from "@/lib/format";
import type { PaginatedResult, SaleSummaryDto, SaleDto, ProductDto, ContactDto, ApiResponse } from "@/lib/types";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { SourceBadge } from "@/components/source-badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { PageHeader } from "@/components/page-header";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import { ChevronLeft, ChevronRight, Ban, Trash2, RotateCcw, Search, X, ShoppingCart, FileDown, Mail } from "lucide-react";
import { EmptyState } from "@/components/empty-state";

function statusBadgeClass(status: string) {
  if (status === "Paid") return "bg-emerald-50 text-emerald-700 ring-1 ring-inset ring-emerald-200";
  if (status === "Unpaid") return "bg-red-50 text-red-700 ring-1 ring-inset ring-red-200";
  return "bg-amber-50 text-amber-700 ring-1 ring-inset ring-amber-200"; // PartiallyPaid
}

export default function SalesPage() {
  const [page, setPage] = useState(1);
  const [recording, setRecording] = useState(false);
  const [viewing, setViewing] = useState<SaleSummaryDto | null>(null);
  const [voiding, setVoiding] = useState<SaleSummaryDto | null>(null);
  const [returning, setReturning] = useState<SaleSummaryDto | null>(null);
  const [emailingSale, setEmailingSale] = useState<SaleSummaryDto | null>(null);
  const [tab, setTab] = useState<"active" | "voided" | "returned">("active");
  const [statusFilter, setStatusFilter] = useState("");
  const [methodFilter, setMethodFilter] = useState("");
  const [sourceFilter, setSourceFilter] = useState("");
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");

  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search.trim()), 250);
    return () => clearTimeout(t);
  }, [search]);

  // Auto-open create dialog from ?new=1 query param (dashboard quick action)
  useEffect(() => {
    if (new URLSearchParams(window.location.search).get("new") === "1" && hasPermission(Permission.RecordSales)) {
      setRecording(true);
    }
  }, []);

  // Download receipt PDF from API
  const { toast } = useToast();
  async function downloadReceipt(saleId: string) {
    try {
      const res = await api.get(`/sales/${saleId}/receipt`, { responseType: "blob" });
      const blob = res.data as Blob;
      // Try to read filename from Content-Disposition header (e.g., "RCT-MTS-000123.pdf")
      const cd = res.headers["content-disposition"] as string | undefined;
      let filename = `receipt-${saleId.slice(0, 8)}.pdf`;
      if (cd) {
        const m = /filename="?([^";]+)"?/.exec(cd);
        if (m && m[1]) filename = m[1];
      }
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = filename;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
      toast.success("Receipt downloaded", filename);
    } catch (err) {
      console.error("Receipt download failed", err);
      toast.error("Failed to download receipt", "Try again or check your connection.");
    }
  }

  const { data, isLoading } = useQuery({
    queryKey: ["sales", tab, page, statusFilter, methodFilter, sourceFilter, debouncedSearch],
    queryFn: async () => {
      const endpoint = tab === "voided" ? "/sales/voided" : tab === "returned" ? "/sales/returned" : "/sales";
      let url = `${endpoint}?page=${page}&pageSize=20`;
      if (statusFilter) url += `&paymentStatus=${statusFilter}`;
      if (methodFilter) url += `&paymentMethod=${methodFilter}`;
      if (sourceFilter) url += `&source=${sourceFilter}`;
      if (debouncedSearch) url += `&search=${encodeURIComponent(debouncedSearch)}`;
      const { data } = await api.get<{ data: PaginatedResult<SaleSummaryDto> }>(url);
      return data.data!;
    },
  });

  return (
    <div className="space-y-6">
      <PageHeader
        title="Sales"
        subtitle="All recorded sales transactions"
        actions={
          tab === "active" && hasPermission(Permission.RecordSales) ? (
            <Button onClick={() => setRecording(true)}>+ Record Sale</Button>
          ) : null
        }
      />

      <Tabs value={tab} onValueChange={(v) => { setTab(v as "active" | "voided" | "returned"); setPage(1); }}>
        <TabsList>
          <TabsTrigger value="active">Active</TabsTrigger>
          <TabsTrigger value="voided">Voided</TabsTrigger>
          <TabsTrigger value="returned">Returned</TabsTrigger>
        </TabsList>
      </Tabs>

      <div className="flex items-center gap-3 flex-wrap">
        <div className="relative w-full sm:max-w-xs">
          <Search size={14} className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400 pointer-events-none" />
          <input
            type="search"
            placeholder="Search by product or customer..."
            value={search}
            onChange={(e) => { setSearch(e.target.value); setPage(1); }}
            className="h-8 w-full pl-8 pr-8 rounded-md border border-slate-200 text-xs focus:outline-none focus:ring-2 focus:ring-cyan-500"
          />
          {search && (
            <button onClick={() => { setSearch(""); setPage(1); }} className="absolute right-2 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-700 p-0.5 rounded" type="button">
              <X size={12} />
            </button>
          )}
        </div>
        <select value={statusFilter} onChange={(e) => { setStatusFilter(e.target.value); setPage(1); }}
          className="h-8 px-2 rounded-md border border-slate-200 text-xs">
          <option value="">All Statuses</option>
          <option value="Paid">Paid</option>
          <option value="Unpaid">Unpaid</option>
          <option value="PartiallyPaid">Partially Paid</option>
        </select>
        <select value={methodFilter} onChange={(e) => { setMethodFilter(e.target.value); setPage(1); }}
          className="h-8 px-2 rounded-md border border-slate-200 text-xs">
          <option value="">All Methods</option>
          <option value="Cash">Cash</option>
          <option value="Card">Card</option>
          <option value="Bank Transfer">Bank Transfer</option>
        </select>
        <select value={sourceFilter} onChange={(e) => { setSourceFilter(e.target.value); setPage(1); }}
          className="h-8 px-2 rounded-md border border-slate-200 text-xs">
          <option value="">All Sources</option>
          <option value="WhatsApp">WhatsApp</option>
          <option value="Manual">Dashboard</option>
          <option value="Import">Import</option>
        </select>
      </div>

      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-xs font-semibold text-slate-500 uppercase tracking-wider">
            {data ? `${data.totalCount} ${tab} transaction${data.totalCount !== 1 ? "s" : ""}` : "Loading…"}
          </CardTitle>
        </CardHeader>
        <CardContent>
          {isLoading ? (
            <div className="space-y-2">
              {Array.from({ length: 8 }).map((_, i) => (
                <Skeleton key={i} className="h-10" />
              ))}
            </div>
          ) : (
            <>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Date</TableHead>
                    <TableHead>Customer</TableHead>
                    <TableHead>Items</TableHead>
                    <TableHead>Status</TableHead>
                    <TableHead>Method</TableHead>
                    <TableHead>Source</TableHead>
                    <TableHead>By</TableHead>
                    <TableHead className="text-right">Amount</TableHead>
                    <TableHead></TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data?.items.map((sale) => (
                    <TableRow
                      key={sale.id}
                      className={`cursor-pointer hover:bg-slate-50 ${tab !== "active" ? "opacity-60" : ""}`}
                      onClick={() => setViewing(sale)}
                    >
                      <TableCell className="text-xs text-slate-500">
                        {tab === "voided" && sale.deletedAtUtc
                          ? `Voided ${formatDateTime(sale.deletedAtUtc)}`
                          : tab === "returned" && sale.deletedAtUtc
                          ? `Returned ${formatDateTime(sale.deletedAtUtc)}`
                          : formatDateTime(sale.createdAtUtc)}
                      </TableCell>
                      <TableCell className="text-sm">
                        {sale.customerName ?? <span className="text-slate-400">—</span>}
                      </TableCell>
                      <TableCell className="text-sm text-slate-500 max-w-[200px]">
                        <span className="truncate block" title={sale.itemSummary ?? undefined}>
                          {sale.itemSummary || `${sale.itemCount} item${sale.itemCount !== 1 ? "s" : ""}`}
                        </span>
                      </TableCell>
                      <TableCell>
                        <Badge className={statusBadgeClass(sale.paymentStatus)}>
                          {sale.paymentStatus}
                        </Badge>
                      </TableCell>
                      <TableCell className="text-xs text-slate-500">
                        {sale.paymentMethod ?? <span className="text-slate-300">—</span>}
                      </TableCell>
                      <TableCell>
                        <SourceBadge source={sale.source} />
                      </TableCell>
                      <TableCell className="text-xs text-slate-500">
                        {sale.recordedByName ?? <span className="text-slate-300">—</span>}
                      </TableCell>
                      <TableCell className={`text-right font-semibold tabular-nums ${tab !== "active" ? "text-slate-400 line-through" : "text-slate-900"}`}>
                        {formatNaira(sale.totalAmount)}
                      </TableCell>
                      <TableCell className="text-right">
                        <div className="flex items-center justify-end gap-1">
                          <button
                            onClick={(e) => { e.stopPropagation(); downloadReceipt(sale.id); }}
                            className="p-1 rounded hover:bg-cyan-50 text-slate-500 hover:text-cyan-600"
                            title="Download receipt PDF"
                          >
                            <FileDown size={14} />
                          </button>
                          <button
                            onClick={(e) => { e.stopPropagation(); setEmailingSale(sale); }}
                            className="p-1 rounded hover:bg-violet-50 text-slate-500 hover:text-violet-600"
                            title="Email receipt"
                          >
                            <Mail size={14} />
                          </button>
                          {tab === "active" && hasPermission(Permission.VoidSales) && (
                            <>
                              <button
                                onClick={(e) => { e.stopPropagation(); setReturning(sale); }}
                                className="p-1 rounded hover:bg-amber-50 text-slate-500 hover:text-amber-600"
                                title="Return sale (customer returned product)"
                              >
                                <RotateCcw size={14} />
                              </button>
                              <button
                                onClick={(e) => { e.stopPropagation(); setVoiding(sale); }}
                                className="p-1 rounded hover:bg-red-50 text-slate-500 hover:text-red-600"
                                title="Void sale (fix a mistake)"
                              >
                                <Ban size={14} />
                              </button>
                            </>
                          )}
                        </div>
                      </TableCell>
                    </TableRow>
                  ))}
                  {data?.items.length === 0 && (
                    <TableRow>
                      <TableCell colSpan={9} className="p-0">
                        <EmptyState
                          icon={<ShoppingCart size={22} />}
                          title={
                            tab === "voided"
                              ? "No voided sales"
                              : tab === "returned"
                              ? "No returned sales"
                              : "No sales yet"
                          }
                          description={
                            tab === "active"
                              ? "Record your first sale via WhatsApp or click + Record Sale above."
                              : undefined
                          }
                          action={
                            tab === "active" && hasPermission(Permission.RecordSales) ? (
                              <Button onClick={() => setRecording(true)}>+ Record Sale</Button>
                            ) : undefined
                          }
                        />
                      </TableCell>
                    </TableRow>
                  )}
                </TableBody>
              </Table>

              {data && data.totalPages > 1 && (
                <div className="flex items-center justify-between mt-4 pt-4 border-t">
                  <p className="text-xs text-slate-500">
                    Page {data.page} of {data.totalPages}
                  </p>
                  <div className="flex gap-2">
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => setPage((p) => p - 1)}
                      disabled={page === 1}
                    >
                      <ChevronLeft size={14} />
                    </Button>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => setPage((p) => p + 1)}
                      disabled={page === data.totalPages}
                    >
                      <ChevronRight size={14} />
                    </Button>
                  </div>
                </div>
              )}
            </>
          )}
        </CardContent>
      </Card>

      <RecordSaleDialog open={recording} onClose={() => setRecording(false)} />
      <EmailReceiptDialog
        sale={emailingSale}
        open={emailingSale !== null}
        onClose={() => setEmailingSale(null)}
      />
      <VoidSaleDialog
        sale={voiding}
        open={voiding !== null}
        onClose={() => setVoiding(null)}
      />
      <ReturnSaleDialog
        sale={returning}
        open={returning !== null}
        onClose={() => setReturning(null)}
      />
      <SaleDetailDialog
        sale={viewing}
        open={viewing !== null}
        onClose={() => setViewing(null)}
        onVoid={hasPermission(Permission.VoidSales) ? (s) => { setViewing(null); setVoiding(s); } : undefined}
        onReturn={hasPermission(Permission.VoidSales) ? (s) => { setViewing(null); setReturning(s); } : undefined}
      />
    </div>
  );
}

type SaleLine = { productId: string; quantity: string; unitPrice: string };

// ── Email receipt dialog ─────────────────────────────────────────────────────
function EmailReceiptDialog({
  sale,
  open,
  onClose,
}: {
  sale: SaleSummaryDto | null;
  open: boolean;
  onClose: () => void;
}) {
  const [email, setEmail] = useState("");
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const { toast } = useToast();

  // Pre-fill (best-effort) on open from existing customer email if we had it.
  useEffect(() => {
    if (open) {
      setEmail("");
      setError(null);
    }
  }, [open]);

  async function handleSend() {
    if (!sale || !email.includes("@")) {
      setError("Please enter a valid email address.");
      return;
    }
    setSending(true);
    setError(null);
    try {
      await api.post<{ message?: string; errors?: string[] }>(
        `/sales/${sale.id}/receipt/email`,
        { to: email.trim() }
      );
      toast.success("Receipt emailed", `Sent to ${email.trim()}`);
      onClose();
    } catch (err: unknown) {
      const ax = err as { response?: { status?: number; data?: { errors?: string[] } } };
      if (ax.response?.status === 503) {
        setError(ax.response?.data?.errors?.[0] ?? "Email is not configured for this server. Ask your admin to set SMTP credentials.");
      } else {
        setError(ax.response?.data?.errors?.[0] ?? "Failed to send. Try again.");
      }
    } finally {
      setSending(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Email receipt</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <p className="text-sm text-slate-500">
            Send the PDF receipt for {sale?.customerName ?? "this sale"} to an email address.
          </p>
          <div>
            <Label>Recipient email</Label>
            <Input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="customer@example.com"
              autoFocus
            />
          </div>
          {error && (
            <div className="bg-red-50 border border-red-200 rounded-md px-3 py-2 text-xs text-red-700">{error}</div>
          )}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={sending}>Cancel</Button>
          <Button onClick={handleSend} disabled={sending || !email}>
            {sending ? "Sending…" : "Send receipt"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function RecordSaleDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const qc = useQueryClient();
  const biz = useBusiness();
  const [lines, setLines] = useState<SaleLine[]>([{ productId: "", quantity: "", unitPrice: "" }]);
  const [contactId, setContactId] = useState<string>("");
  const [paymentStatus, setPaymentStatus] = useState<"Paid" | "Unpaid" | "PartiallyPaid">("Paid");
  const [paymentMethod, setPaymentMethod] = useState<string>("Cash");
  const [includeVat, setIncludeVat] = useState<boolean>(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Default VAT-include based on business setting when dialog opens
  useEffect(() => {
    if (open) setIncludeVat(biz?.vatEnabled ?? false);
  }, [open, biz?.vatEnabled]);

  const { data: products } = useQuery({
    queryKey: ["products-for-sale"],
    queryFn: async () => {
      const { data } = await api.get<{ data: PaginatedResult<ProductDto> }>("/products?page=1&pageSize=200");
      return data.data!.items;
    },
    enabled: open,
  });

  const { data: contacts } = useQuery({
    queryKey: ["contacts-for-sale"],
    queryFn: async () => {
      const { data } = await api.get<{ data: PaginatedResult<ContactDto> }>("/contacts?page=1&pageSize=200");
      return data.data!.items;
    },
    enabled: open,
  });

  function updateLine(idx: number, field: keyof SaleLine, value: string) {
    setLines((prev) => {
      const next = [...prev];
      next[idx] = { ...next[idx], [field]: value };
      // Auto-fill price when product is picked
      if (field === "productId") {
        const product = products?.find((p) => p.id === value);
        if (product?.sellingPrice) next[idx].unitPrice = product.sellingPrice.toString();
      }
      return next;
    });
  }

  function addLine() {
    setLines((prev) => [...prev, { productId: "", quantity: "", unitPrice: "" }]);
  }

  function removeLine(idx: number) {
    setLines((prev) => prev.filter((_, i) => i !== idx));
  }

  const subtotal = lines.reduce((sum, l) => {
    const q = Number(l.quantity) || 0;
    const p = Number(l.unitPrice) || 0;
    return sum + q * p;
  }, 0);
  const vatRate = biz?.vatRate ?? 7.5;
  const vatAmount = includeVat ? Math.round(subtotal * (vatRate / 100) * 100) / 100 : 0;
  const total = subtotal + vatAmount;

  async function handleSave() {
    setError(null);
    const validLines = lines.filter((l) => l.productId && Number(l.quantity) > 0 && Number(l.unitPrice) > 0);
    if (validLines.length === 0) {
      setError("Add at least one item with quantity and price");
      return;
    }
    setSaving(true);
    try {
      await api.post(`/sales`, {
        items: validLines.map((l) => ({
          productId: l.productId,
          quantity: Number(l.quantity),
          unitPrice: Number(l.unitPrice),
        })),
        contactId: contactId || null,
        paymentStatus,
        paymentMethod,
        vatAmount: includeVat ? vatAmount : undefined,
      });
      qc.invalidateQueries({ queryKey: ["sales"] });
      qc.invalidateQueries({ queryKey: ["products"] });
      qc.invalidateQueries({ queryKey: ["low-stock"] });
      handleClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to record sale");
    } finally {
      setSaving(false);
    }
  }

  function handleClose() {
    setLines([{ productId: "", quantity: "", unitPrice: "" }]);
    setContactId("");
    setPaymentStatus("Paid");
    setPaymentMethod("Cash");
    setError(null);
    onClose();
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && handleClose()}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>Record Sale</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <div className="space-y-2">
            <Label>Items</Label>
            {lines.map((line, idx) => (
              <div key={idx} className="grid grid-cols-12 gap-2 items-end">
                <div className="col-span-5">
                  <select
                    className="w-full h-9 px-2 rounded-md border border-slate-200 text-sm"
                    value={line.productId}
                    onChange={(e) => updateLine(idx, "productId", e.target.value)}
                  >
                    <option value="">Pick product</option>
                    {products?.map((p) => (
                      <option key={p.id} value={p.id}>
                        {p.name} ({p.currentStock} {p.unit})
                      </option>
                    ))}
                  </select>
                </div>
                <div className="col-span-3">
                  <Input
                    type="number"
                    placeholder="Qty"
                    value={line.quantity}
                    onChange={(e) => updateLine(idx, "quantity", e.target.value)}
                  />
                </div>
                <div className="col-span-3">
                  <Input
                    type="number"
                    placeholder="Price"
                    value={line.unitPrice}
                    onChange={(e) => updateLine(idx, "unitPrice", e.target.value)}
                  />
                </div>
                <div className="col-span-1 flex justify-center">
                  {lines.length > 1 && (
                    <button
                      onClick={() => removeLine(idx)}
                      className="p-1 text-slate-400 hover:text-red-500"
                    >
                      <Trash2 size={14} />
                    </button>
                  )}
                </div>
              </div>
            ))}
            <Button variant="outline" size="sm" onClick={addLine} className="w-full">
              + Add another item
            </Button>
          </div>

          <div>
            <Label>Customer (optional)</Label>
            <select
              className="w-full h-9 px-2 rounded-md border border-slate-200 text-sm"
              value={contactId}
              onChange={(e) => setContactId(e.target.value)}
            >
              <option value="">No customer</option>
              {contacts?.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          </div>

          <div>
            <Label>Payment Status</Label>
            <select
              className="w-full h-9 px-2 rounded-md border border-slate-200 text-sm"
              value={paymentStatus}
              onChange={(e) => setPaymentStatus(e.target.value as "Paid" | "Unpaid" | "PartiallyPaid")}
            >
              <option value="Paid">Paid</option>
              <option value="Unpaid">Unpaid (credit)</option>
              <option value="PartiallyPaid">Partially Paid</option>
            </select>
          </div>

          <div>
            <Label>Payment Method</Label>
            <select
              className="w-full h-9 px-2 rounded-md border border-slate-200 text-sm"
              value={paymentMethod}
              onChange={(e) => setPaymentMethod(e.target.value)}
            >
              <option value="Cash">Cash</option>
              <option value="Card">Card</option>
              <option value="Bank Transfer">Bank Transfer</option>
            </select>
          </div>

          {/* VAT toggle (shown if business has VAT enabled, or user wants to add it ad-hoc) */}
          <label className="flex items-center gap-2 cursor-pointer text-sm pt-2">
            <input
              type="checkbox"
              checked={includeVat}
              onChange={(e) => setIncludeVat(e.target.checked)}
              className="rounded border-slate-300"
            />
            <span className="text-slate-700">
              Add VAT ({vatRate.toFixed(1)}%)
            </span>
          </label>

          <div className="pt-2 border-t space-y-1">
            {includeVat && (
              <>
                <div className="flex justify-between text-sm">
                  <span className="text-slate-500">Subtotal</span>
                  <span className="text-slate-700 tabular-nums">{formatNaira(subtotal)}</span>
                </div>
                <div className="flex justify-between text-sm">
                  <span className="text-slate-500">VAT ({vatRate.toFixed(1)}%)</span>
                  <span className="text-slate-700 tabular-nums">{formatNaira(vatAmount)}</span>
                </div>
              </>
            )}
            <div className="flex justify-between">
              <span className="text-sm font-semibold text-slate-700">Total</span>
              <span className="text-lg font-bold text-slate-900 tabular-nums">{formatNaira(total)}</span>
            </div>
          </div>

          {error && <p className="text-xs text-red-500">{error}</p>}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={handleClose} disabled={saving}>Cancel</Button>
          <Button onClick={handleSave} disabled={saving}>{saving ? "Saving…" : "Record Sale"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function VoidSaleDialog({
  sale,
  open,
  onClose,
}: {
  sale: SaleSummaryDto | null;
  open: boolean;
  onClose: () => void;
}) {
  const qc = useQueryClient();
  const [voiding, setVoiding] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleVoid() {
    if (!sale) return;
    setVoiding(true);
    setError(null);
    try {
      await api.post(`/sales/${sale.id}/void`);
      qc.invalidateQueries({ queryKey: ["sales"] });
      qc.invalidateQueries({ queryKey: ["products"] });
      qc.invalidateQueries({ queryKey: ["low-stock"] });
      onClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to void sale");
    } finally {
      setVoiding(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Void Sale?</DialogTitle>
        </DialogHeader>
        <p className="text-sm text-slate-600">
          This will void the sale of <strong>{formatNaira(sale?.totalAmount ?? 0)}</strong>
          {sale?.customerName ? ` to ${sale.customerName}` : ""} and <strong>restore the stock</strong> for each item.
        </p>
        <p className="text-xs text-slate-500">
          The sale will not appear in reports. This is the recommended way to fix mistakes.
        </p>
        {error && <p className="text-xs text-red-500">{error}</p>}
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={voiding}>Cancel</Button>
          <Button onClick={handleVoid} disabled={voiding} className="bg-red-600 hover:bg-red-700 text-white">
            {voiding ? "Voiding…" : "Void Sale"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function ReturnSaleDialog({
  sale,
  open,
  onClose,
}: {
  sale: SaleSummaryDto | null;
  open: boolean;
  onClose: () => void;
}) {
  const qc = useQueryClient();
  const [processing, setProcessing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleReturn() {
    if (!sale) return;
    setProcessing(true);
    setError(null);
    try {
      await api.post(`/sales/${sale.id}/return`);
      qc.invalidateQueries({ queryKey: ["sales"] });
      qc.invalidateQueries({ queryKey: ["products"] });
      qc.invalidateQueries({ queryKey: ["low-stock"] });
      onClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to return sale");
    } finally {
      setProcessing(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Return Sale?</DialogTitle>
        </DialogHeader>
        <p className="text-sm text-slate-600">
          This will mark the sale of <strong>{formatNaira(sale?.totalAmount ?? 0)}</strong>
          {sale?.customerName ? ` to ${sale.customerName}` : ""} as returned and <strong>restore the stock</strong> for each item.
        </p>
        <p className="text-xs text-slate-500">
          Use this when a customer brings back a product. The sale will appear under the Returned tab.
        </p>
        {error && <p className="text-xs text-red-500">{error}</p>}
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={processing}>Cancel</Button>
          <Button onClick={handleReturn} disabled={processing} className="bg-amber-600 hover:bg-amber-700 text-white">
            {processing ? "Processing…" : "Return Sale"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function SaleDetailDialog({
  sale,
  open,
  onClose,
  onVoid,
  onReturn,
}: {
  sale: SaleSummaryDto | null;
  open: boolean;
  onClose: () => void;
  onVoid?: (sale: SaleSummaryDto) => void;
  onReturn?: (sale: SaleSummaryDto) => void;
}) {
  const { data: detail, isLoading } = useQuery({
    queryKey: ["sale-detail", sale?.id],
    queryFn: async () => {
      const { data } = await api.get<ApiResponse<SaleDto>>(`/sales/${sale!.id}`);
      return data.data!;
    },
    enabled: open && !!sale?.id,
  });

  return (
    <Dialog open={open} onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-w-md max-h-[85vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Sale Details</DialogTitle>
        </DialogHeader>
        {isLoading || !detail ? (
          <div className="space-y-3">
            {Array.from({ length: 4 }).map((_, i) => (
              <Skeleton key={i} className="h-5" />
            ))}
          </div>
        ) : (
          <div className="space-y-4">
            <div className="grid grid-cols-2 gap-y-2 text-sm">
              <span className="text-slate-500">Customer</span>
              <span className="font-medium">{detail.customerName ?? "Walk-in"}</span>
              <span className="text-slate-500">Date</span>
              <span className="font-medium">{formatDateTime(detail.createdAtUtc)}</span>
              <span className="text-slate-500">Recorded by</span>
              <span className="font-medium">{detail.recordedByName ?? "\u2014"}</span>
              <span className="text-slate-500">Source</span>
              <span className="font-medium capitalize">{detail.source ?? "Manual"}</span>
            </div>

            <div>
              <h4 className="text-xs font-semibold text-slate-500 uppercase mb-2">Items ({detail.items.length})</h4>
              <div className="space-y-1 max-h-60 overflow-y-auto pr-1">
                {detail.items.map((item) => (
                  <div key={item.productId} className="flex justify-between text-sm">
                    <span>
                      {item.quantity} {item.unit} {item.productName} @ {formatNaira(item.unitPrice)}
                    </span>
                    <span className="font-medium">{formatNaira(item.totalPrice)}</span>
                  </div>
                ))}
              </div>
            </div>

            <div className="border-t pt-3 space-y-2">
              <div className="flex justify-between text-sm">
                <span className="font-semibold">Total</span>
                <span className="text-lg font-bold text-emerald-600">{formatNaira(detail.totalAmount)}</span>
              </div>
              <div className="flex justify-between items-center text-sm">
                <span className="text-slate-500">Payment Status</span>
                <Badge className={statusBadgeClass(detail.paymentStatus)}>
                  {detail.paymentStatus}
                </Badge>
              </div>
              {detail.paymentMethod && (
                <div className="flex justify-between text-sm">
                  <span className="text-slate-500">Payment Method</span>
                  <span>{detail.paymentMethod}</span>
                </div>
              )}
            </div>

            {detail.customerName && detail.contactBalance != null && detail.contactBalance > 0 && (
              <div className="border-t pt-3">
                <div className="flex justify-between text-sm">
                  <span className="text-slate-500">Customer Outstanding</span>
                  <span className="font-semibold text-amber-600">{formatNaira(detail.contactBalance)}</span>
                </div>
                {detail.dueDate && (
                  <div className="flex justify-between text-sm mt-1">
                    <span className="text-slate-500">Earliest Due</span>
                    <span>{formatDateTime(detail.dueDate)}</span>
                  </div>
                )}
              </div>
            )}

            {detail.notes && (
              <div className="border-t pt-3">
                <span className="text-xs text-slate-500">Notes</span>
                <p className="text-sm mt-1">{detail.notes}</p>
              </div>
            )}
          </div>
        )}
        <DialogFooter>
          {sale && !sale.deletedAtUtc && onReturn && (
            <Button
              variant="outline"
              onClick={() => onReturn(sale)}
              className="text-amber-600 border-amber-200 hover:bg-amber-50"
            >
              <RotateCcw size={14} className="mr-1" /> Return
            </Button>
          )}
          {sale && !sale.deletedAtUtc && onVoid && (
            <Button
              variant="outline"
              onClick={() => onVoid(sale)}
              className="text-red-600 border-red-200 hover:bg-red-50"
            >
              <Ban size={14} className="mr-1" /> Void
            </Button>
          )}
          <Button variant="outline" onClick={onClose}>Close</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

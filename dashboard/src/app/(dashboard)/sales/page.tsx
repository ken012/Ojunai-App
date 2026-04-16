"use client";

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { hasPermission, Permission } from "@/lib/permissions";
import { formatNaira, formatDateTime } from "@/lib/format";
import type { PaginatedResult, SaleSummaryDto, ProductDto, ContactDto } from "@/lib/types";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { SourceBadge } from "@/components/source-badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
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
import { ChevronLeft, ChevronRight, Ban, Trash2 } from "lucide-react";

function statusVariant(status: string) {
  if (status === "Paid") return "default";
  if (status === "Unpaid") return "destructive";
  return "secondary";
}

export default function SalesPage() {
  const [page, setPage] = useState(1);
  const [recording, setRecording] = useState(false);
  const [voiding, setVoiding] = useState<SaleSummaryDto | null>(null);
  const [tab, setTab] = useState<"active" | "voided">("active");

  const { data, isLoading } = useQuery({
    queryKey: ["sales", tab, page],
    queryFn: async () => {
      const endpoint = tab === "voided" ? "/sales/voided" : "/sales";
      const { data } = await api.get<{ data: PaginatedResult<SaleSummaryDto> }>(
        `${endpoint}?page=${page}&pageSize=20`
      );
      return data.data!;
    },
  });

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold text-slate-900">Sales</h2>
          <p className="text-slate-500 text-sm mt-0.5">All recorded sales transactions</p>
        </div>
        {tab === "active" && hasPermission(Permission.RecordSales) && (
          <Button onClick={() => setRecording(true)}>+ Record Sale</Button>
        )}
      </div>

      <Tabs value={tab} onValueChange={(v) => { setTab(v as "active" | "voided"); setPage(1); }}>
        <TabsList>
          <TabsTrigger value="active">Active</TabsTrigger>
          <TabsTrigger value="voided">Voided</TabsTrigger>
        </TabsList>
      </Tabs>

      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700">
            {data ? `${data.totalCount} total transactions` : "Loading…"}
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
                    <TableHead>Source</TableHead>
                    <TableHead>By</TableHead>
                    <TableHead className="text-right">Amount</TableHead>
                    <TableHead></TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data?.items.map((sale) => (
                    <TableRow key={sale.id} className={tab === "voided" ? "opacity-60" : ""}>
                      <TableCell className="text-xs text-slate-500">
                        {tab === "voided" && sale.deletedAtUtc
                          ? `Voided ${formatDateTime(sale.deletedAtUtc)}`
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
                        <Badge variant={statusVariant(sale.paymentStatus)} className="text-xs">
                          {sale.paymentStatus}
                        </Badge>
                      </TableCell>
                      <TableCell>
                        <SourceBadge source={sale.source} />
                      </TableCell>
                      <TableCell className="text-xs text-slate-500">
                        {sale.recordedByName ?? <span className="text-slate-300">—</span>}
                      </TableCell>
                      <TableCell className={`text-right font-semibold ${tab === "voided" ? "text-slate-400 line-through" : "text-emerald-600"}`}>
                        {formatNaira(sale.totalAmount)}
                      </TableCell>
                      <TableCell className="text-right">
                        {tab === "active" && hasPermission(Permission.VoidSales) && (
                          <button
                            onClick={() => setVoiding(sale)}
                            className="p-1 rounded hover:bg-red-50 text-slate-500 hover:text-red-600"
                            title="Void sale (restore stock)"
                          >
                            <Ban size={14} />
                          </button>
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                  {data?.items.length === 0 && (
                    <TableRow>
                      <TableCell colSpan={8} className="text-center py-8 text-slate-400 text-sm">
                        {tab === "voided" ? "No voided sales" : "No sales yet"}
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
      <VoidSaleDialog
        sale={voiding}
        open={voiding !== null}
        onClose={() => setVoiding(null)}
      />
    </div>
  );
}

type SaleLine = { productId: string; quantity: string; unitPrice: string };

function RecordSaleDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const qc = useQueryClient();
  const [lines, setLines] = useState<SaleLine[]>([{ productId: "", quantity: "", unitPrice: "" }]);
  const [contactId, setContactId] = useState<string>("");
  const [paymentStatus, setPaymentStatus] = useState<"Paid" | "Unpaid" | "PartiallyPaid">("Paid");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

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

  const total = lines.reduce((sum, l) => {
    const q = Number(l.quantity) || 0;
    const p = Number(l.unitPrice) || 0;
    return sum + q * p;
  }, 0);

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

          <div className="flex justify-between pt-2 border-t">
            <span className="text-sm text-slate-500">Total</span>
            <span className="text-lg font-semibold text-emerald-600">{formatNaira(total)}</span>
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

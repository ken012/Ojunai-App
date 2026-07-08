"use client";

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { PageHeader } from "@/components/page-header";
import { formatNaira, formatDateTime } from "@/lib/format";
import { hasPermission, Permission } from "@/lib/permissions";
import type { PurchaseOrderDto, PurchaseOrderStatus, ContactDto, ProductDto, PaginatedResult } from "@/lib/types";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/empty-state";
import { useToast } from "@/components/toast";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";
import { BarcodeScanner } from "@/components/barcode-scanner";
import { Truck, Plus, Trash2, ScanLine, Send, PackageCheck, X } from "lucide-react";

const STATUS_FILTERS: { id: string; label: string }[] = [
  { id: "all", label: "All" },
  { id: "Draft", label: "Draft" },
  { id: "Sent", label: "Sent" },
  { id: "PartiallyReceived", label: "Partial" },
  { id: "Received", label: "Received" },
  { id: "Cancelled", label: "Cancelled" },
];

const STATUS_STYLE: Record<PurchaseOrderStatus, string> = {
  Draft: "bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-300",
  Sent: "bg-sky-100 text-sky-700 dark:bg-sky-950/40 dark:text-sky-300",
  PartiallyReceived: "bg-amber-100 text-amber-700 dark:bg-amber-950/40 dark:text-amber-300",
  Received: "bg-emerald-100 text-emerald-700 dark:bg-emerald-950/40 dark:text-emerald-300",
  Cancelled: "bg-rose-100 text-rose-700 dark:bg-rose-950/40 dark:text-rose-300",
};

const statusLabel = (s: PurchaseOrderStatus) => (s === "PartiallyReceived" ? "Partial" : s);

export default function PurchasingPage() {
  const canManage = hasPermission(Permission.ManageStock);
  const [status, setStatus] = useState("all");
  const [creating, setCreating] = useState(false);
  const [openId, setOpenId] = useState<string | null>(null);

  const { data, isLoading } = useQuery({
    queryKey: ["purchase-orders", status],
    queryFn: async () => {
      const { data } = await api.get<{ data: PaginatedResult<PurchaseOrderDto> }>(
        `/purchase-orders?status=${status}&pageSize=100`,
      );
      return data.data!;
    },
  });

  const orders = data?.items ?? [];

  return (
    <div className="space-y-5">
      <PageHeader
        title="Purchasing"
        subtitle="Order stock from suppliers and receive it into inventory"
        actions={canManage ? (
          <Button onClick={() => setCreating(true)} className="gap-1.5">
            <Plus size={16} /> New purchase order
          </Button>
        ) : undefined}
      />

      <div className="flex gap-1.5 overflow-x-auto pb-1">
        {STATUS_FILTERS.map((f) => (
          <button
            key={f.id}
            onClick={() => setStatus(f.id)}
            className={`px-3 py-1.5 rounded-md text-xs font-medium whitespace-nowrap transition-colors ${
              status === f.id
                ? "bg-cyan-100 text-cyan-700 border border-cyan-200 dark:bg-cyan-950/40 dark:text-cyan-300 dark:border-cyan-900"
                : "bg-slate-50 dark:bg-slate-950 text-slate-500 dark:text-slate-400 border border-slate-200 dark:border-slate-800 hover:bg-slate-100 dark:hover:bg-slate-800"
            }`}
          >
            {f.label}
          </button>
        ))}
      </div>

      {isLoading ? (
        <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-20 w-full rounded-lg" />)}</div>
      ) : orders.length === 0 ? (
        <EmptyState
          icon={<Truck size={40} className="text-slate-300" />}
          title="No purchase orders yet"
          description="Create a purchase order to track what you've ordered from a supplier, then receive it to add stock and record the payable."
        />
      ) : (
        <div className="space-y-2">
          {orders.map((po) => (
            <Card key={po.id} className="cursor-pointer hover:border-slate-300 dark:hover:border-slate-700 transition-colors" onClick={() => setOpenId(po.id)}>
              <CardContent className="p-4 flex items-center justify-between gap-3">
                <div className="min-w-0">
                  <div className="flex items-center gap-2">
                    <p className="font-semibold text-sm text-slate-900 dark:text-slate-50">{po.poNumber}</p>
                    <span className={`text-[10px] px-2 py-0.5 rounded-full font-medium ${STATUS_STYLE[po.status]}`}>{statusLabel(po.status)}</span>
                  </div>
                  <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5 truncate">
                    {po.supplierName || "No supplier"} · {po.items.length} item{po.items.length === 1 ? "" : "s"} · {formatDateTime(po.createdAtUtc)}
                  </p>
                </div>
                <p className="text-sm font-semibold text-slate-900 dark:text-slate-50 flex-shrink-0">{formatNaira(po.totalAmount)}</p>
              </CardContent>
            </Card>
          ))}
        </div>
      )}

      {creating && <CreatePODialog onClose={() => setCreating(false)} />}
      {openId && <PODetailDialog id={openId} canManage={canManage} onClose={() => setOpenId(null)} />}
    </div>
  );
}

// ── Create ───────────────────────────────────────────────────────────────────

type LineDraft = { key: string; productId?: string; productName: string; unit: string; qty: string; unitCost: string };

function newLine(): LineDraft {
  // Stable-enough key without Date.now/Math.random (avoid SSR mismatch); index-based fallback.
  return { key: Math.random().toString(36).slice(2), productId: undefined, productName: "", unit: "unit", qty: "", unitCost: "" };
}

function CreatePODialog({ onClose }: { onClose: () => void }) {
  const qc = useQueryClient();
  const { toast } = useToast();
  const [supplierId, setSupplierId] = useState("");
  const [notes, setNotes] = useState("");
  const [expected, setExpected] = useState("");
  const [lines, setLines] = useState<LineDraft[]>([newLine()]);
  const [saving, setSaving] = useState(false);
  const [scanForKey, setScanForKey] = useState<string | null>(null);

  const { data: suppliers } = useQuery({
    queryKey: ["po-suppliers"],
    queryFn: async () => {
      const { data } = await api.get<{ data: PaginatedResult<ContactDto> }>(`/contacts?type=supplier&pageSize=200`);
      return data.data!.items;
    },
  });
  const { data: products } = useQuery({
    queryKey: ["po-products"],
    queryFn: async () => {
      const { data } = await api.get<{ data: PaginatedResult<ProductDto> }>(`/products?pageSize=500`);
      return data.data!.items;
    },
  });

  function setLine(key: string, patch: Partial<LineDraft>) {
    setLines((ls) => ls.map((l) => (l.key === key ? { ...l, ...patch } : l)));
  }

  function pickProduct(key: string, productId: string) {
    const p = products?.find((x) => x.id === productId);
    if (!p) { setLine(key, { productId: undefined, productName: "" }); return; }
    setLine(key, { productId: p.id, productName: p.name, unit: p.unit, unitCost: p.costPrice != null ? String(p.costPrice) : "" });
  }

  function handleScanned(code: string) {
    const key = scanForKey;
    setScanForKey(null);
    if (!key) return;
    const p = products?.find((x) => x.barcode && x.barcode === code);
    if (p) {
      pickProduct(key, p.id);
      toast.success("Product matched", p.name);
    } else {
      toast.error("No match", `No product has barcode ${code}. Add it to a product first.`);
    }
  }

  const total = lines.reduce((sum, l) => sum + (Number(l.qty) || 0) * (Number(l.unitCost) || 0), 0);
  const validLines = lines.filter((l) => l.productName.trim() && Number(l.qty) > 0);

  async function handleSave() {
    if (validLines.length === 0) { toast.error("Add at least one item", "Each item needs a name and quantity."); return; }
    setSaving(true);
    try {
      await api.post("/purchase-orders", {
        supplierId: supplierId || null,
        supplierName: supplierId ? undefined : undefined,
        notes: notes || null,
        expectedAtUtc: expected ? new Date(expected).toISOString() : null,
        items: validLines.map((l) => ({
          productId: l.productId || null,
          productName: l.productName.trim(),
          unit: l.unit || "unit",
          quantityOrdered: Number(l.qty),
          unitCost: Number(l.unitCost) || 0,
        })),
      });
      qc.invalidateQueries({ queryKey: ["purchase-orders"] });
      toast.success("Purchase order created", "It's saved as a draft — send it, then receive when stock arrives.");
      onClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      toast.error("Couldn't create purchase order", ax.response?.data?.errors?.[0] ?? "Please try again.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-w-2xl">
        <DialogHeader><DialogTitle>New purchase order</DialogTitle></DialogHeader>

        <div className="space-y-4 max-h-[65vh] overflow-y-auto pr-1">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div>
              <Label className="text-xs">Supplier</Label>
              <select
                value={supplierId}
                onChange={(e) => setSupplierId(e.target.value)}
                className="mt-1 h-9 w-full px-2 rounded-md border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 text-sm"
              >
                <option value="">— No supplier —</option>
                {suppliers?.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
              </select>
              <p className="text-[10px] text-slate-400 mt-1">Linking a supplier records a payable when you receive.</p>
            </div>
            <div>
              <Label className="text-xs">Expected date (optional)</Label>
              <Input type="date" value={expected} onChange={(e) => setExpected(e.target.value)} className="mt-1" />
            </div>
          </div>

          <div>
            <Label className="text-xs">Items</Label>
            <div className="space-y-2 mt-1">
              {lines.map((l) => (
                <div key={l.key} className="flex flex-wrap items-end gap-2 rounded-lg border border-slate-200 dark:border-slate-800 p-2">
                  <div className="flex-1 min-w-[160px]">
                    <select
                      value={l.productId ?? ""}
                      onChange={(e) => e.target.value ? pickProduct(l.key, e.target.value) : setLine(l.key, { productId: undefined })}
                      className="h-9 w-full px-2 rounded-md border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 text-sm"
                    >
                      <option value="">Pick a product…</option>
                      {products?.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                    </select>
                    {!l.productId && (
                      <Input
                        value={l.productName}
                        onChange={(e) => setLine(l.key, { productName: e.target.value })}
                        placeholder="…or type a name"
                        className="mt-1 h-8 text-xs"
                      />
                    )}
                  </div>
                  <div className="w-20">
                    <Input type="number" min={0} value={l.qty} onChange={(e) => setLine(l.key, { qty: e.target.value })} placeholder="Qty" className="h-9" />
                  </div>
                  <div className="w-28">
                    <Input type="number" min={0} value={l.unitCost} onChange={(e) => setLine(l.key, { unitCost: e.target.value })} placeholder="Unit cost" className="h-9" />
                  </div>
                  <button type="button" onClick={() => setScanForKey(l.key)} title="Scan barcode" className="h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 text-slate-500 hover:text-cyan-600">
                    <ScanLine size={16} />
                  </button>
                  {lines.length > 1 && (
                    <button type="button" onClick={() => setLines((ls) => ls.filter((x) => x.key !== l.key))} className="h-9 px-2 text-slate-400 hover:text-rose-500">
                      <Trash2 size={16} />
                    </button>
                  )}
                </div>
              ))}
            </div>
            <button type="button" onClick={() => setLines((ls) => [...ls, newLine()])} className="mt-2 text-xs font-medium text-cyan-600 hover:underline inline-flex items-center gap-1">
              <Plus size={13} /> Add item
            </button>
          </div>

          <div>
            <Label className="text-xs">Notes (optional)</Label>
            <Input value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="e.g. deliver to back entrance" className="mt-1" />
          </div>
        </div>

        <DialogFooter className="items-center">
          <p className="text-sm mr-auto text-slate-600 dark:text-slate-300">Total: <span className="font-semibold text-slate-900 dark:text-slate-50">{formatNaira(total)}</span></p>
          <Button variant="outline" onClick={onClose}>Cancel</Button>
          <Button onClick={handleSave} disabled={saving}>{saving ? "Saving…" : "Create draft"}</Button>
        </DialogFooter>
      </DialogContent>

      <BarcodeScanner open={scanForKey !== null} onClose={() => setScanForKey(null)} onScan={handleScanned} />
    </Dialog>
  );
}

// ── Detail / receive ─────────────────────────────────────────────────────────

function PODetailDialog({ id, canManage, onClose }: { id: string; canManage: boolean; onClose: () => void }) {
  const qc = useQueryClient();
  const { toast } = useToast();
  const [receiveQty, setReceiveQty] = useState<Record<string, string>>({});
  const [createPayable, setCreatePayable] = useState(true);
  const [busy, setBusy] = useState<string | null>(null);
  const [mode, setMode] = useState<"view" | "receive">("view");

  const { data: po, isLoading } = useQuery({
    queryKey: ["purchase-order", id],
    queryFn: async () => {
      const { data } = await api.get<{ data: PurchaseOrderDto }>(`/purchase-orders/${id}`);
      return data.data!;
    },
  });

  async function act(path: string, body: unknown, label: string, successMsg: string) {
    setBusy(label);
    try {
      await api.post(`/purchase-orders/${id}/${path}`, body ?? {});
      qc.invalidateQueries({ queryKey: ["purchase-orders"] });
      qc.invalidateQueries({ queryKey: ["purchase-order", id] });
      if (path === "receive") {
        // Receiving changes product stock + supplier payable — refresh the inventory + ledger views
        // so the phone doesn't show stale numbers until the next background refetch.
        qc.invalidateQueries({ queryKey: ["products"] });
        qc.invalidateQueries({ queryKey: ["product-stock-stats"] });
        qc.invalidateQueries({ queryKey: ["low-stock"] });
        qc.invalidateQueries({ queryKey: ["contacts"] });
        qc.invalidateQueries({ queryKey: ["ledger"] });
      }
      toast.success(successMsg);
      if (path === "receive" || path === "cancel") onClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      toast.error("Action failed", ax.response?.data?.errors?.[0] ?? "Please try again.");
    } finally {
      setBusy(null);
    }
  }

  function submitReceive() {
    const lines = Object.entries(receiveQty)
      .map(([itemId, v]) => ({ itemId, quantityReceived: Number(v) || 0 }))
      .filter((l) => l.quantityReceived > 0);
    if (lines.length === 0) { toast.error("Nothing to receive", "Enter a received quantity for at least one line."); return; }
    act("receive", { lines, createPayable }, "receive", "Stock received");
  }

  const canEdit = canManage && po && (po.status === "Draft" || po.status === "Sent" || po.status === "PartiallyReceived");
  const isOpenForReceive = po && po.status !== "Received" && po.status !== "Cancelled";

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-w-lg">
        {isLoading || !po ? (
          <div className="space-y-2 py-4">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-10 w-full" />)}</div>
        ) : (
          <>
            <DialogHeader>
              <DialogTitle className="flex items-center gap-2">
                {po.poNumber}
                <span className={`text-[10px] px-2 py-0.5 rounded-full font-medium ${STATUS_STYLE[po.status]}`}>{statusLabel(po.status)}</span>
              </DialogTitle>
            </DialogHeader>

            <div className="space-y-3 max-h-[60vh] overflow-y-auto pr-1">
              <p className="text-xs text-slate-500 dark:text-slate-400">
                {po.supplierName || "No supplier"}{po.expectedAtUtc ? ` · expected ${formatDateTime(po.expectedAtUtc)}` : ""}
                {po.notes ? ` · ${po.notes}` : ""}
              </p>

              <div className="rounded-lg border border-slate-200 dark:border-slate-800 divide-y divide-slate-100 dark:divide-slate-800">
                {po.items.map((it) => {
                  const remaining = it.quantityOrdered - it.quantityReceived;
                  return (
                    <div key={it.id} className="p-2.5 flex items-center gap-3">
                      <div className="min-w-0 flex-1">
                        <p className="text-sm font-medium text-slate-800 dark:text-slate-200 truncate">{it.productName}</p>
                        <p className="text-[11px] text-slate-400">
                          {it.quantityReceived}/{it.quantityOrdered} {it.unit} received · {formatNaira(it.unitCost)} each
                        </p>
                      </div>
                      {mode === "receive" && remaining > 0 ? (
                        <Input
                          type="number" min={0} max={remaining}
                          value={receiveQty[it.id] ?? ""}
                          onChange={(e) => setReceiveQty((m) => ({ ...m, [it.id]: e.target.value }))}
                          placeholder={`${remaining}`}
                          className="w-20 h-8"
                        />
                      ) : (
                        <p className="text-sm font-semibold text-slate-700 dark:text-slate-300">{formatNaira(it.lineTotal)}</p>
                      )}
                    </div>
                  );
                })}
              </div>

              {mode === "receive" && po.supplierId && (
                <label className="flex items-center gap-2 text-xs text-slate-600 dark:text-slate-400 cursor-pointer">
                  <input type="checkbox" checked={createPayable} onChange={(e) => setCreatePayable(e.target.checked)} className="rounded" />
                  Record what I owe {po.supplierName} as a payable
                </label>
              )}

              <div className="flex justify-between text-sm pt-1">
                <span className="text-slate-500">Total</span>
                <span className="font-semibold text-slate-900 dark:text-slate-50">{formatNaira(po.totalAmount)}</span>
              </div>
            </div>

            <DialogFooter>
              {mode === "receive" ? (
                <>
                  <Button variant="outline" onClick={() => setMode("view")}>Back</Button>
                  <Button onClick={submitReceive} disabled={busy !== null} className="gap-1.5">
                    <PackageCheck size={15} /> {busy === "receive" ? "Receiving…" : "Confirm receive"}
                  </Button>
                </>
              ) : (
                <>
                  {canManage && po.status !== "Received" && po.status !== "Cancelled" && (
                    <Button variant="outline" onClick={() => act("cancel", {}, "cancel", "Purchase order cancelled")} disabled={busy !== null} className="mr-auto text-rose-600 hover:text-rose-700">
                      <X size={15} className="mr-1" /> Cancel PO
                    </Button>
                  )}
                  {canEdit && po.status === "Draft" && (
                    <Button variant="outline" onClick={() => act("send", {}, "send", "Marked as sent")} disabled={busy !== null} className="gap-1.5">
                      <Send size={15} /> Mark sent
                    </Button>
                  )}
                  {canManage && isOpenForReceive && (
                    <Button onClick={() => setMode("receive")} className="gap-1.5">
                      <PackageCheck size={15} /> Receive stock
                    </Button>
                  )}
                </>
              )}
            </DialogFooter>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}

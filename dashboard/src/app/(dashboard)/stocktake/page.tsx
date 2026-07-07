"use client";

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { PageHeader } from "@/components/page-header";
import { formatNaira, formatDateTime } from "@/lib/format";
import { hasPermission, Permission } from "@/lib/permissions";
import { CATEGORY_NAMES } from "@/lib/categories";
import type { StocktakeDto, StocktakeStatus, PaginatedResult } from "@/lib/types";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/empty-state";
import { useToast } from "@/components/toast";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";
import { ClipboardCheck, Plus, ArrowLeft, CheckCircle2, X } from "lucide-react";

const STATUS_STYLE: Record<StocktakeStatus, string> = {
  Draft: "bg-amber-100 text-amber-700 dark:bg-amber-950/40 dark:text-amber-300",
  Completed: "bg-emerald-100 text-emerald-700 dark:bg-emerald-950/40 dark:text-emerald-300",
  Cancelled: "bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400",
};

export default function StocktakePage() {
  const canManage = hasPermission(Permission.ManageStock);
  const [creating, setCreating] = useState(false);
  const [openId, setOpenId] = useState<string | null>(null);

  const { data, isLoading } = useQuery({
    queryKey: ["stocktakes"],
    queryFn: async () => {
      const { data } = await api.get<{ data: PaginatedResult<StocktakeDto> }>("/stocktakes?pageSize=100");
      return data.data!;
    },
  });

  if (openId) return <CountView id={openId} canManage={canManage} onBack={() => setOpenId(null)} />;

  const takes = data?.items ?? [];

  return (
    <div className="space-y-5">
      <PageHeader
        title="Stock count"
        subtitle="Count physical stock, review the variance, and reconcile inventory"
        actions={canManage ? (
          <Button onClick={() => setCreating(true)} className="gap-1.5"><Plus size={16} /> New count</Button>
        ) : undefined}
      />

      {isLoading ? (
        <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-20 w-full rounded-lg" />)}</div>
      ) : takes.length === 0 ? (
        <EmptyState
          icon={<ClipboardCheck size={40} className="text-slate-300" />}
          title="No stock counts yet"
          description="Start a count to reconcile what's physically on your shelves against what the system thinks you have. Variances become auditable adjustments."
        />
      ) : (
        <div className="space-y-2">
          {takes.map((t) => (
            <Card key={t.id} className="cursor-pointer hover:border-slate-300 dark:hover:border-slate-700 transition-colors" onClick={() => setOpenId(t.id)}>
              <CardContent className="p-4 flex items-center justify-between gap-3">
                <div className="min-w-0">
                  <div className="flex items-center gap-2">
                    <p className="font-semibold text-sm text-slate-900 dark:text-slate-50">{t.reference}</p>
                    <span className={`text-[10px] px-2 py-0.5 rounded-full font-medium ${STATUS_STYLE[t.status]}`}>{t.status}</span>
                  </div>
                  <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5 truncate">
                    {t.scope} · {t.countedItems}/{t.totalItems} counted · {formatDateTime(t.createdAtUtc)}
                  </p>
                </div>
                {t.status === "Completed" && t.netVarianceValue !== 0 && (
                  <p className={`text-sm font-semibold flex-shrink-0 ${t.netVarianceValue < 0 ? "text-rose-600" : "text-emerald-600"}`}>
                    {t.netVarianceValue < 0 ? "−" : "+"}{formatNaira(Math.abs(t.netVarianceValue))}
                  </p>
                )}
              </CardContent>
            </Card>
          ))}
        </div>
      )}

      {creating && <NewCountDialog onClose={() => setCreating(false)} onCreated={(id) => { setCreating(false); setOpenId(id); }} />}
    </div>
  );
}

function NewCountDialog({ onClose, onCreated }: { onClose: () => void; onCreated: (id: string) => void }) {
  const { toast } = useToast();
  const [category, setCategory] = useState("");
  const [notes, setNotes] = useState("");
  const [saving, setSaving] = useState(false);

  async function create() {
    setSaving(true);
    try {
      const { data } = await api.post<{ data: StocktakeDto }>("/stocktakes", { category: category || null, notes: notes || null });
      toast.success("Stock count started", `${data.data!.totalItems} products to count.`);
      onCreated(data.data!.id);
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      toast.error("Couldn't start count", ax.response?.data?.errors?.[0] ?? "Please try again.");
      setSaving(false);
    }
  }

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-w-sm">
        <DialogHeader><DialogTitle>New stock count</DialogTitle></DialogHeader>
        <div className="space-y-3">
          <div>
            <Label className="text-xs">What to count</Label>
            <select
              value={category}
              onChange={(e) => setCategory(e.target.value)}
              className="mt-1 h-9 w-full px-2 rounded-md border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 text-sm"
            >
              <option value="">All products</option>
              {CATEGORY_NAMES.map((c) => <option key={c} value={c}>Category: {c}</option>)}
            </select>
            <p className="text-[10px] text-slate-400 mt-1">Snapshots current stock for these products so you can count against it.</p>
          </div>
          <div>
            <Label className="text-xs">Notes (optional)</Label>
            <Input value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="e.g. month-end count" className="mt-1" />
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={saving}>Cancel</Button>
          <Button onClick={create} disabled={saving}>{saving ? "Starting…" : "Start count"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function CountView({ id, canManage, onBack }: { id: string; canManage: boolean; onBack: () => void }) {
  const qc = useQueryClient();
  const { toast } = useToast();
  const [counts, setCounts] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState<string | null>(null);
  const [seeded, setSeeded] = useState(false);

  const { data: st, isLoading } = useQuery({
    queryKey: ["stocktake", id],
    queryFn: async () => {
      const { data } = await api.get<{ data: StocktakeDto }>(`/stocktakes/${id}`);
      return data.data!;
    },
  });

  // Seed local count inputs from any saved counts (once).
  if (st && !seeded) {
    const initial: Record<string, string> = {};
    for (const it of st.items) if (it.countedQuantity != null) initial[it.id] = String(it.countedQuantity);
    setCounts(initial);
    setSeeded(true);
  }

  const isDraft = st?.status === "Draft";

  function countsPayload() {
    return Object.entries(counts)
      .filter(([, v]) => v !== "")
      .map(([itemId, v]) => ({ itemId, countedQuantity: Number(v) }))
      .filter((c) => !Number.isNaN(c.countedQuantity));
  }

  async function save(silent = false) {
    setBusy("save");
    try {
      await api.put(`/stocktakes/${id}/counts`, { counts: countsPayload() });
      qc.invalidateQueries({ queryKey: ["stocktake", id] });
      if (!silent) toast.success("Progress saved");
    } catch {
      toast.error("Couldn't save", "Please try again.");
    } finally { setBusy(null); }
  }

  async function complete() {
    if (countsPayload().length === 0) { toast.error("Nothing counted", "Enter at least one counted quantity."); return; }
    if (!confirm("Apply this count? Counted products will be adjusted to match, and adjustments are logged. This can't be undone.")) return;
    setBusy("complete");
    try {
      await api.put(`/stocktakes/${id}/counts`, { counts: countsPayload() });
      await api.post(`/stocktakes/${id}/complete`);
      qc.invalidateQueries({ queryKey: ["stocktakes"] });
      qc.invalidateQueries({ queryKey: ["stocktake", id] });
      qc.invalidateQueries({ queryKey: ["products"] });
      toast.success("Stock count applied", "Inventory reconciled to your counts.");
      onBack();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      toast.error("Couldn't complete", ax.response?.data?.errors?.[0] ?? "Please try again.");
      setBusy(null);
    }
  }

  async function cancel() {
    if (!confirm("Cancel this stock count? Nothing will change in inventory.")) return;
    setBusy("cancel");
    try {
      await api.post(`/stocktakes/${id}/cancel`);
      qc.invalidateQueries({ queryKey: ["stocktakes"] });
      toast.success("Stock count cancelled");
      onBack();
    } catch { toast.error("Couldn't cancel", "Please try again."); setBusy(null); }
  }

  // Live net variance value from the current inputs (Draft) or the server figure (finished).
  const liveNet = st && isDraft
    ? st.items.reduce((sum, it) => {
        const v = counts[it.id];
        if (v === "" || v == null) return sum;
        const n = Number(v);
        if (Number.isNaN(n)) return sum;
        return sum + (n - it.systemQuantity) * it.unitCost;
      }, 0)
    : (st?.netVarianceValue ?? 0);

  return (
    <div className="space-y-4">
      <button onClick={onBack} className="inline-flex items-center gap-1 text-sm text-slate-500 hover:text-slate-700 dark:hover:text-slate-300">
        <ArrowLeft size={15} /> Back to stock counts
      </button>

      {isLoading || !st ? (
        <div className="space-y-2">{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-12 w-full" />)}</div>
      ) : (
        <>
          <div className="flex items-center justify-between gap-3">
            <div>
              <div className="flex items-center gap-2">
                <h2 className="text-lg font-semibold text-slate-900 dark:text-slate-50">{st.reference}</h2>
                <span className={`text-[10px] px-2 py-0.5 rounded-full font-medium ${STATUS_STYLE[st.status]}`}>{st.status}</span>
              </div>
              <p className="text-xs text-slate-500 dark:text-slate-400">{st.scope} · {st.totalItems} products</p>
            </div>
            <div className="text-right">
              <p className="text-[11px] text-slate-400 uppercase tracking-wide">Net variance</p>
              <p className={`text-lg font-semibold ${liveNet < 0 ? "text-rose-600" : liveNet > 0 ? "text-emerald-600" : "text-slate-500"}`}>
                {liveNet < 0 ? "−" : liveNet > 0 ? "+" : ""}{formatNaira(Math.abs(liveNet))}
              </p>
            </div>
          </div>

          <div className="rounded-lg border border-slate-200 dark:border-slate-800 overflow-hidden">
            <div className="grid grid-cols-12 gap-2 px-3 py-2 bg-slate-50 dark:bg-slate-900/50 text-[11px] font-medium uppercase tracking-wide text-slate-400">
              <div className="col-span-5">Product</div>
              <div className="col-span-2 text-right">System</div>
              <div className="col-span-3 text-right">Counted</div>
              <div className="col-span-2 text-right">Variance</div>
            </div>
            <div className="divide-y divide-slate-100 dark:divide-slate-800 max-h-[60vh] overflow-y-auto">
              {st.items.map((it) => {
                const raw = isDraft ? counts[it.id] : (it.countedQuantity != null ? String(it.countedQuantity) : "");
                const counted = raw === "" || raw == null ? null : Number(raw);
                const variance = counted == null || Number.isNaN(counted) ? null : counted - it.systemQuantity;
                return (
                  <div key={it.id} className="grid grid-cols-12 gap-2 px-3 py-2 items-center">
                    <div className="col-span-5 min-w-0">
                      <p className="text-sm text-slate-800 dark:text-slate-200 truncate">{it.productName}</p>
                    </div>
                    <div className="col-span-2 text-right text-sm text-slate-500 tabular-nums">{it.systemQuantity} <span className="text-[10px] text-slate-400">{it.unit}</span></div>
                    <div className="col-span-3">
                      {isDraft && canManage ? (
                        <Input
                          type="number" min={0}
                          value={counts[it.id] ?? ""}
                          onChange={(e) => setCounts((m) => ({ ...m, [it.id]: e.target.value }))}
                          placeholder="—"
                          className="h-8 text-right"
                        />
                      ) : (
                        <p className="text-right text-sm tabular-nums">{it.countedQuantity ?? "—"}</p>
                      )}
                    </div>
                    <div className={`col-span-2 text-right text-sm tabular-nums font-medium ${variance == null ? "text-slate-300" : variance < 0 ? "text-rose-600" : variance > 0 ? "text-emerald-600" : "text-slate-400"}`}>
                      {variance == null ? "—" : `${variance > 0 ? "+" : ""}${variance}`}
                    </div>
                  </div>
                );
              })}
            </div>
          </div>

          {isDraft && canManage && (
            <div className="flex items-center gap-2">
              <Button variant="outline" onClick={cancel} disabled={busy !== null} className="mr-auto text-rose-600 hover:text-rose-700">
                <X size={15} className="mr-1" /> Cancel count
              </Button>
              <Button variant="outline" onClick={() => save(false)} disabled={busy !== null}>
                {busy === "save" ? "Saving…" : "Save progress"}
              </Button>
              <Button onClick={complete} disabled={busy !== null} className="gap-1.5">
                <CheckCircle2 size={15} /> {busy === "complete" ? "Applying…" : "Complete & apply"}
              </Button>
            </div>
          )}
        </>
      )}
    </div>
  );
}

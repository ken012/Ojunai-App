"use client";

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { PageHeader } from "@/components/page-header";
import { formatNaira } from "@/lib/format";
import { hasPermission, Permission } from "@/lib/permissions";
import { useBusiness, useDataSync } from "@/lib/data-sync";
import type { VariantGroupDto } from "@/lib/types";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/empty-state";
import { useToast } from "@/components/toast";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";
import { Shirt, Plus, ArrowLeft, Trash2, Layers } from "lucide-react";

export default function VariantsPage() {
  const business = useBusiness();
  const canManage = hasPermission(Permission.ManageStock);
  const [creating, setCreating] = useState(false);
  const [openId, setOpenId] = useState<string | null>(null);

  const { data, isLoading } = useQuery({
    queryKey: ["variant-groups"],
    queryFn: async () => {
      const { data } = await api.get<{ data: VariantGroupDto[] }>("/variant-groups");
      return data.data!;
    },
    enabled: !!business?.variantsEnabled,
  });

  if (!business?.variantsEnabled) return <EnableView canManage={canManage} />;
  if (openId) return <StyleView id={openId} canManage={canManage} onBack={() => setOpenId(null)} />;

  const groups = data ?? [];

  return (
    <div className="space-y-5">
      <PageHeader
        title="Product variants"
        subtitle="Manage a style once — its sizes, colours, etc. are each a real product"
        actions={canManage ? (
          <Button onClick={() => setCreating(true)} className="gap-1.5"><Plus size={16} /> New variant product</Button>
        ) : undefined}
      />

      {isLoading ? (
        <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-20 w-full rounded-lg" />)}</div>
      ) : groups.length === 0 ? (
        <EmptyState
          icon={<Shirt size={40} className="text-slate-300" />}
          title="No variant products yet"
          description="Create a style like a t-shirt with sizes and colours. Each combination becomes its own product you can stock, price, and sell."
        />
      ) : (
        <div className="space-y-2">
          {groups.map((g) => (
            <Card key={g.id} className="cursor-pointer hover:border-slate-300 dark:hover:border-slate-700 transition-colors" onClick={() => setOpenId(g.id)}>
              <CardContent className="p-4 flex items-center justify-between gap-3">
                <div className="min-w-0">
                  <p className="font-semibold text-sm text-slate-900 dark:text-slate-50 flex items-center gap-2">
                    <Layers size={14} className="text-violet-500" /> {g.name}
                  </p>
                  <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5 truncate">
                    {g.axes.map((a) => a.name).join(" · ")} · {g.variantCount} variants · {g.totalStock} in stock
                  </p>
                </div>
                <p className="text-sm text-slate-500 dark:text-slate-400 flex-shrink-0">
                  {g.minPrice != null ? (g.minPrice === g.maxPrice ? formatNaira(g.minPrice) : `${formatNaira(g.minPrice)}–${formatNaira(g.maxPrice ?? g.minPrice)}`) : "—"}
                </p>
              </CardContent>
            </Card>
          ))}
        </div>
      )}

      {creating && <CreateStyleDialog onClose={() => setCreating(false)} onCreated={(id) => { setCreating(false); setOpenId(id); }} />}
    </div>
  );
}

function EnableView({ canManage }: { canManage: boolean }) {
  const { refresh } = useDataSync();
  const { toast } = useToast();
  const [saving, setSaving] = useState(false);

  async function enable() {
    setSaving(true);
    try {
      await api.put("/business", { variantsEnabled: true });
      await refresh();
      toast.success("Variants enabled", "Create your first variant product.");
    } catch {
      toast.error("Couldn't enable variants", "Please try again.");
      setSaving(false);
    }
  }

  return (
    <div className="space-y-5">
      <PageHeader title="Product variants" subtitle="Sizes, colours and other options for a product" />
      <Card>
        <CardContent className="p-6 text-center space-y-3">
          <Shirt size={40} className="text-violet-400 mx-auto" />
          <p className="text-sm text-slate-600 dark:text-slate-300 max-w-md mx-auto">
            Sell a product that comes in variations — like a t-shirt in several sizes and colours.
            Each combination becomes its own product with its own stock, price and barcode, grouped under one style.
          </p>
          {canManage ? (
            <Button onClick={enable} disabled={saving}>{saving ? "Enabling…" : "Enable product variants"}</Button>
          ) : (
            <p className="text-xs text-slate-400">Ask an owner or admin to enable this.</p>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

type AxisDraft = { name: string; values: string };

function CreateStyleDialog({ onClose, onCreated }: { onClose: () => void; onCreated: (id: string) => void }) {
  const { toast } = useToast();
  const [name, setName] = useState("");
  const [category, setCategory] = useState("");
  const [unit, setUnit] = useState("");
  const [basePrice, setBasePrice] = useState("");
  const [baseCost, setBaseCost] = useState("");
  const [axes, setAxes] = useState<AxisDraft[]>([{ name: "Size", values: "" }, { name: "Color", values: "" }]);
  const [saving, setSaving] = useState(false);

  const parsedAxes = axes
    .map((a) => ({ name: a.name.trim(), values: a.values.split(",").map((v) => v.trim()).filter(Boolean) }))
    .filter((a) => a.name && a.values.length > 0);
  const comboCount = parsedAxes.reduce((n, a) => n * a.values.length, parsedAxes.length ? 1 : 0);

  async function create() {
    if (!name.trim()) { toast.error("Name required", "Give the style a name."); return; }
    if (parsedAxes.length === 0) { toast.error("Add an option", "Add at least one option with values."); return; }
    if (comboCount > 200) { toast.error("Too many variants", `${comboCount} exceeds the 200 limit. Trim your options.`); return; }
    setSaving(true);
    try {
      const { data } = await api.post<{ data: VariantGroupDto }>("/variant-groups", {
        name: name.trim(),
        category: category || null,
        unit: unit || null,
        axes: parsedAxes,
        baseSellingPrice: basePrice ? Number(basePrice) : null,
        baseCostPrice: baseCost ? Number(baseCost) : null,
      });
      toast.success("Variant product created", `${data.data!.variantCount} variants generated.`);
      onCreated(data.data!.id);
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      toast.error("Couldn't create", ax.response?.data?.errors?.[0] ?? "Please try again.");
      setSaving(false);
    }
  }

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-w-lg">
        <DialogHeader><DialogTitle>New variant product</DialogTitle></DialogHeader>
        <div className="space-y-3 max-h-[65vh] overflow-y-auto pr-1">
          <div>
            <Label className="text-xs">Style name</Label>
            <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Classic Tee" className="mt-1" />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <Label className="text-xs">Category (optional)</Label>
              <Input value={category} onChange={(e) => setCategory(e.target.value)} placeholder="e.g. Clothing" className="mt-1" />
            </div>
            <div>
              <Label className="text-xs">Unit (optional)</Label>
              <Input value={unit} onChange={(e) => setUnit(e.target.value)} placeholder="e.g. piece" className="mt-1" />
            </div>
          </div>

          <div>
            <Label className="text-xs">Options</Label>
            <div className="space-y-2 mt-1">
              {axes.map((a, i) => (
                <div key={i} className="flex items-center gap-2">
                  <Input value={a.name} onChange={(e) => setAxes((xs) => xs.map((x, j) => j === i ? { ...x, name: e.target.value } : x))} placeholder="Option (e.g. Size)" className="w-32" />
                  <Input value={a.values} onChange={(e) => setAxes((xs) => xs.map((x, j) => j === i ? { ...x, values: e.target.value } : x))} placeholder="Values, comma-separated (S, M, L)" className="flex-1" />
                  {axes.length > 1 && (
                    <button type="button" onClick={() => setAxes((xs) => xs.filter((_, j) => j !== i))} className="p-1 text-slate-400 hover:text-rose-500"><Trash2 size={14} /></button>
                  )}
                </div>
              ))}
            </div>
            <button type="button" onClick={() => setAxes((xs) => [...xs, { name: "", values: "" }])} className="mt-2 text-xs font-medium text-cyan-600 hover:underline inline-flex items-center gap-1">
              <Plus size={13} /> Add option
            </button>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <Label className="text-xs">Base selling price (optional)</Label>
              <Input type="number" value={basePrice} onChange={(e) => setBasePrice(e.target.value)} placeholder="Applied to all" className="mt-1" />
            </div>
            <div>
              <Label className="text-xs">Base cost (optional)</Label>
              <Input type="number" value={baseCost} onChange={(e) => setBaseCost(e.target.value)} placeholder="Applied to all" className="mt-1" />
            </div>
          </div>
        </div>
        <DialogFooter className="items-center">
          <p className="text-xs text-slate-500 mr-auto">{comboCount > 0 ? `${comboCount} variant${comboCount === 1 ? "" : "s"} will be created` : "Add options to preview"}</p>
          <Button variant="outline" onClick={onClose}>Cancel</Button>
          <Button onClick={create} disabled={saving}>{saving ? "Creating…" : "Create"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function StyleView({ id, canManage, onBack }: { id: string; canManage: boolean; onBack: () => void }) {
  const qc = useQueryClient();
  const { toast } = useToast();
  const [busy, setBusy] = useState(false);
  const [restock, setRestock] = useState<Record<string, string>>({});

  const { data: g, isLoading } = useQuery({
    queryKey: ["variant-group", id],
    queryFn: async () => {
      const { data } = await api.get<{ data: VariantGroupDto }>(`/variant-groups/${id}`);
      return data.data!;
    },
  });

  function invalidate() {
    qc.invalidateQueries({ queryKey: ["variant-group", id] });
    qc.invalidateQueries({ queryKey: ["variant-groups"] });
  }

  async function savePrice(productId: string, value: string) {
    const price = Number(value);
    if (Number.isNaN(price)) return;
    try { await api.patch(`/products/${productId}/price`, { sellingPrice: price }); invalidate(); }
    catch { toast.error("Couldn't save price", "Please try again."); }
  }

  async function addStock(productId: string) {
    const qty = Number(restock[productId]);
    if (!qty || qty <= 0) return;
    setBusy(true);
    try {
      await api.post("/inventory/stock-in", { productId, quantity: qty });
      setRestock((m) => ({ ...m, [productId]: "" }));
      invalidate();
      toast.success("Stock added");
    } catch { toast.error("Couldn't add stock", "Please try again."); }
    finally { setBusy(false); }
  }

  async function ungroup() {
    if (!confirm("Dissolve this style? The variants stay as standalone products; only the grouping is removed.")) return;
    setBusy(true);
    try { await api.post(`/variant-groups/${id}/ungroup`); qc.invalidateQueries({ queryKey: ["variant-groups"] }); qc.invalidateQueries({ queryKey: ["products"] }); toast.success("Style dissolved"); onBack(); }
    catch { toast.error("Couldn't dissolve", "Please try again."); setBusy(false); }
  }

  return (
    <div className="space-y-4">
      <button onClick={onBack} className="inline-flex items-center gap-1 text-sm text-slate-500 hover:text-slate-700 dark:hover:text-slate-300">
        <ArrowLeft size={15} /> Back to variants
      </button>

      {isLoading || !g ? (
        <div className="space-y-2">{[0, 1, 2, 3].map((i) => <Skeleton key={i} className="h-12 w-full" />)}</div>
      ) : (
        <>
          <div className="flex items-center justify-between gap-3">
            <div>
              <h2 className="text-lg font-semibold text-slate-900 dark:text-slate-50">{g.name}</h2>
              <p className="text-xs text-slate-500 dark:text-slate-400">{g.axes.map((a) => `${a.name}: ${a.values.join(", ")}`).join(" · ")}</p>
            </div>
            {canManage && (
              <Button variant="outline" onClick={ungroup} disabled={busy} className="text-rose-600 hover:text-rose-700">Dissolve style</Button>
            )}
          </div>

          <div className="rounded-lg border border-slate-200 dark:border-slate-800 overflow-hidden">
            <div className="grid grid-cols-12 gap-2 px-3 py-2 bg-slate-50 dark:bg-slate-900/50 text-[11px] font-medium uppercase tracking-wide text-slate-400">
              <div className="col-span-5">Variant</div>
              <div className="col-span-2 text-right">Stock</div>
              <div className="col-span-2 text-right">Price</div>
              <div className="col-span-3 text-right">Add stock</div>
            </div>
            <div className="divide-y divide-slate-100 dark:divide-slate-800 max-h-[60vh] overflow-y-auto">
              {g.variants.map((v) => (
                <div key={v.productId} className="grid grid-cols-12 gap-2 px-3 py-2 items-center">
                  <div className="col-span-5 min-w-0">
                    <p className="text-sm text-slate-800 dark:text-slate-200 truncate">{Object.values(v.options).join(" / ") || v.name}</p>
                    {v.sku && <p className="text-[10px] text-slate-400 font-mono">SKU {v.sku}</p>}
                  </div>
                  <div className={`col-span-2 text-right text-sm tabular-nums ${v.isLowStock ? "text-amber-600 font-medium" : "text-slate-600 dark:text-slate-300"}`}>
                    {v.currentStock} <span className="text-[10px] text-slate-400">{v.unit}</span>
                  </div>
                  <div className="col-span-2">
                    {canManage ? (
                      <Input type="number" defaultValue={v.sellingPrice ?? ""} onBlur={(e) => { if (e.target.value !== String(v.sellingPrice ?? "")) savePrice(v.productId, e.target.value); }} className="h-8 text-right" placeholder="—" />
                    ) : (
                      <p className="text-right text-sm">{v.sellingPrice != null ? formatNaira(v.sellingPrice) : "—"}</p>
                    )}
                  </div>
                  <div className="col-span-3 flex items-center gap-1 justify-end">
                    {canManage && (
                      <>
                        <Input type="number" min={0} value={restock[v.productId] ?? ""} onChange={(e) => setRestock((m) => ({ ...m, [v.productId]: e.target.value }))} className="h-8 w-16 text-right" placeholder="Qty" />
                        <Button size="sm" variant="outline" onClick={() => addStock(v.productId)} disabled={busy} className="h-8 px-2">Add</Button>
                      </>
                    )}
                  </div>
                </div>
              ))}
            </div>
          </div>
          <p className="text-[11px] text-slate-400 dark:text-slate-500">Each variant is a full product — sell or scan it like any other. Barcodes are set per variant in Inventory.</p>
        </>
      )}
    </div>
  );
}

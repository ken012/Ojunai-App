"use client";

export const dynamic = "force-dynamic";

import { useState, useMemo, useEffect } from "react";
import { useStickyState } from "@/lib/sticky-state";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api, fetchAllPaged } from "@/lib/api";
import { formatNaira } from "@/lib/format";
import type { PaginatedResult, ProductDto, StockHoldDto } from "@/lib/types";
import { CATEGORIES, CATEGORY_NAMES } from "@/lib/categories";
import { useBusiness } from "@/lib/data-sync";
import { hasPermission, Permission } from "@/lib/permissions";
import { Card, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import { Drawer, DrawerHeader, DrawerBody, DrawerFooter } from "@/components/ui/drawer";
import { useToast } from "@/components/toast";
import { AlertTriangle, Package, Pencil, Trash2, Minus, Plus, Lock, Unlock, ShoppingCart, Ban, Search, X, LayoutList, LayoutGrid } from "lucide-react";
import { formatDateTime } from "@/lib/format";
import { usePlanStatus } from "@/lib/use-plan-status";
import { UpgradeInline } from "@/components/upgrade-prompt";
import { PageHeader } from "@/components/page-header";
import { EmptyState } from "@/components/empty-state";

// ─── Category picker (reused in Add + Edit dialogs) ─────────────────────────
function CategoryPicker({
  category,
  subcategory,
  onCategoryChange,
  onSubcategoryChange,
}: {
  category: string;
  subcategory: string;
  onCategoryChange: (v: string) => void;
  onSubcategoryChange: (v: string) => void;
}) {
  const syncBiz = useBusiness();
  const customCats = syncBiz?.customCategories ?? [];
  const allCategoryNames = [...CATEGORY_NAMES, ...customCats.filter((c) => !CATEGORY_NAMES.includes(c))];

  const subcategories = CATEGORIES[category] ?? [];

  return (
    <div className="grid grid-cols-2 gap-3">
      <div>
        <Label>Category</Label>
        <select
          className="w-full h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 text-sm bg-white dark:bg-slate-900"
          value={allCategoryNames.includes(category) ? category : ""}
          onChange={(e) => {
            onCategoryChange(e.target.value);
            onSubcategoryChange("");
          }}
        >
          <option value="">Select category</option>
          {allCategoryNames.map((c) => (
            <option key={c} value={c}>{c}</option>
          ))}
        </select>
        {category && !allCategoryNames.includes(category) && (
          <p className="text-xs text-slate-400 dark:text-slate-500 mt-1">Current: {category}</p>
        )}
      </div>
      <div>
        <Label>Subcategory</Label>
        {subcategories.length > 0 ? (
          <select
            className="w-full h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 text-sm bg-white dark:bg-slate-900"
            value={subcategories.includes(subcategory) ? subcategory : ""}
            onChange={(e) => onSubcategoryChange(e.target.value)}
          >
            <option value="">Select subcategory</option>
            {subcategories.map((s) => (
              <option key={s} value={s}>{s}</option>
            ))}
          </select>
        ) : (
          <Input
            value={subcategory}
            onChange={(e) => onSubcategoryChange(e.target.value)}
            placeholder="Subcategory (optional)"
          />
        )}
      </div>
    </div>
  );
}

// ─── Product card ────────────────────────────────────────────────────────────
// ─── Legacy product card (used when the user toggles to grid view) ──────────
function ProductCard({
  product,
  onEdit,
  onDelete,
  onDamaged,
  onStockOut,
  onRestock,
}: {
  product: ProductDto;
  onEdit: (p: ProductDto) => void;
  onDelete: (p: ProductDto) => void;
  onDamaged: (p: ProductDto) => void;
  onStockOut: (p: ProductDto) => void;
  onRestock: (p: ProductDto) => void;
}) {
  return (
    <Card className={product.isLowStock ? "border-amber-300 dark:border-amber-700 bg-amber-50/40 dark:bg-amber-950/20" : ""}>
      <CardContent className="p-4">
        <div className="flex items-start justify-between">
          <div className="flex-1 min-w-0">
            <p className="font-semibold text-slate-900 dark:text-slate-50 truncate" title={product.name}>{product.name}</p>
            {product.category && (
              <p className="text-xs text-slate-400 dark:text-slate-500 mt-0.5">
                {product.category}{product.subcategory ? ` / ${product.subcategory}` : ""}
              </p>
            )}
            {!product.category && (
              <p className="text-xs text-slate-300 dark:text-slate-600 mt-0.5 italic">Uncategorized</p>
            )}
            {product.sku && (
              <p className="text-xs text-slate-400 dark:text-slate-500 font-mono mt-0.5">SKU: {product.sku}</p>
            )}
          </div>
          <div className="flex items-center gap-1 flex-shrink-0 ml-2">
            {product.isLowStock && (
              <AlertTriangle size={14} className="text-amber-500 mt-0.5" />
            )}
            {hasPermission(Permission.ManageStock) && (
              <>
                <button
                  onClick={() => onRestock(product)}
                  className="p-1 rounded hover:bg-emerald-50 dark:hover:bg-emerald-950/30 text-slate-500 dark:text-slate-400 hover:text-emerald-600 dark:hover:text-emerald-400"
                  title="Restock"
                >
                  <Plus size={14} />
                </button>
                <button
                  onClick={() => onEdit(product)}
                  className="p-1 rounded hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-50"
                  title="Edit"
                >
                  <Pencil size={14} />
                </button>
                {product.currentStock > 0 && (
                  <>
                    <button
                      onClick={() => onStockOut(product)}
                      className="p-1 rounded hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-300"
                      title="Remove stock"
                    >
                      <Minus size={14} />
                    </button>
                    <button
                      onClick={() => onDamaged(product)}
                      className="p-1 rounded hover:bg-amber-50 dark:hover:bg-amber-950/30 text-slate-500 dark:text-slate-400 hover:text-amber-600 dark:hover:text-amber-400"
                      title="Mark damaged"
                    >
                      <Ban size={14} />
                    </button>
                  </>
                )}
                <button
                  onClick={() => onDelete(product)}
                  className="p-1 rounded hover:bg-rose-50 dark:hover:bg-rose-950/30 text-slate-500 dark:text-slate-400 hover:text-rose-600 dark:hover:text-rose-400"
                  title="Delete"
                >
                  <Trash2 size={14} />
                </button>
              </>
            )}
          </div>
        </div>

        <div className="mt-3 flex items-end justify-between">
          <div>
            <p className="text-2xl font-bold text-slate-900 dark:text-slate-50 tabular-nums">
              {product.currentStock}
              <span className="text-sm font-normal text-slate-500 dark:text-slate-400 ml-1">{product.unit}</span>
            </p>
            <p className="text-xs text-slate-400 dark:text-slate-500">
              Threshold: {product.lowStockThreshold} {product.unit}
            </p>
          </div>
          <div className="text-right">
            {product.sellingPrice && (
              <p className="text-sm font-semibold text-slate-900 dark:text-slate-50 tabular-nums">
                {formatNaira(product.sellingPrice)}
              </p>
            )}
            {product.costPrice && (
              <p className="text-xs text-slate-400 dark:text-slate-500 tabular-nums">Cost: {formatNaira(product.costPrice)}</p>
            )}
          </div>
        </div>

        {product.lowStockThreshold > 0 && (() => {
          const ratio = Math.max(0, Math.min(2, product.currentStock / Math.max(1, product.lowStockThreshold)));
          const pct = Math.min(100, (ratio / 2) * 100);
          const tone = ratio < 0.5 ? "bg-rose-500" : ratio < 1 ? "bg-amber-500" : "bg-emerald-500";
          return (
            <div className="mt-2.5">
              <div className="h-1 w-full bg-slate-100 dark:bg-slate-800 rounded-full overflow-hidden">
                <div className={`h-full rounded-full transition-all ${tone}`} style={{ width: `${pct}%` }} />
              </div>
            </div>
          );
        })()}

        <div className="mt-2 flex items-center gap-2 flex-wrap">
          {product.isLowStock && (
            <Badge variant="outline" className="text-xs text-amber-600 dark:text-amber-400 border-amber-300 dark:border-amber-700">
              Low Stock
            </Badge>
          )}
          {product.recordedByName && (
            <span className="text-xs text-slate-400 dark:text-slate-500">by {product.recordedByName}</span>
          )}
        </div>
      </CardContent>
    </Card>
  );
}

// ─── Add Product dialog ──────────────────────────────────────────────────────
function AddProductDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const qc = useQueryClient();
  const [form, setForm] = useState({
    name: "",
    unit: "",
    sellingPrice: "",
    costPrice: "",
    initialStock: "",
    lowStockThreshold: "5",
    category: "",
    subcategory: "",
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSave() {
    setSaving(true);
    setError(null);
    try {
      await api.post(`/products`, {
        name: form.name,
        unit: form.unit || null,
        sellingPrice: form.sellingPrice ? Number(form.sellingPrice) : null,
        costPrice: form.costPrice ? Number(form.costPrice) : null,
        initialStock: form.initialStock ? Number(form.initialStock) : 0,
        lowStockThreshold: form.lowStockThreshold ? Number(form.lowStockThreshold) : 5,
        category: form.category || null,
        subcategory: form.subcategory || null,
      });
      qc.invalidateQueries({ queryKey: ["products"] });
      qc.invalidateQueries({ queryKey: ["low-stock"] });
      handleClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to add product");
    } finally {
      setSaving(false);
    }
  }

  function handleClose() {
    setForm({ name: "", unit: "", sellingPrice: "", costPrice: "", initialStock: "", lowStockThreshold: "5", category: "", subcategory: "" });
    setError(null);
    onClose();
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && handleClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Add Product</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label>Name</Label>
            <Input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="e.g. Rice" />
          </div>
          <div>
            <Label>Unit</Label>
            <Input value={form.unit} onChange={(e) => setForm({ ...form, unit: e.target.value })} placeholder="Auto-detected if left blank" />
          </div>
          <CategoryPicker
            category={form.category}
            subcategory={form.subcategory}
            onCategoryChange={(v) => setForm(f => ({ ...f, category: v }))}
            onSubcategoryChange={(v) => setForm(f => ({ ...f, subcategory: v }))}
          />
          <div className="grid grid-cols-2 gap-3">
            <div>
              <Label>Selling Price</Label>
              <Input type="number" value={form.sellingPrice} onChange={(e) => setForm({ ...form, sellingPrice: e.target.value })} />
            </div>
            <div>
              <Label>Cost Price</Label>
              <Input type="number" value={form.costPrice} onChange={(e) => setForm({ ...form, costPrice: e.target.value })} />
            </div>
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <Label>Opening Stock</Label>
              <Input type="number" value={form.initialStock} onChange={(e) => setForm({ ...form, initialStock: e.target.value })} placeholder="0" />
            </div>
            <div>
              <Label>Low Stock Alert</Label>
              <Input type="number" value={form.lowStockThreshold} onChange={(e) => setForm({ ...form, lowStockThreshold: e.target.value })} />
            </div>
          </div>
          {error && <p className="text-xs text-red-500">{error}</p>}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={handleClose} disabled={saving}>Cancel</Button>
          <Button onClick={handleSave} disabled={saving || !form.name}>{saving ? "Saving…" : "Add Product"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ─── Edit Product dialog ─────────────────────────────────────────────────────
function EditProductDialog({
  product,
  open,
  onClose,
}: {
  product: ProductDto | null;
  open: boolean;
  onClose: () => void;
}) {
  const qc = useQueryClient();
  const { toast } = useToast();
  const [form, setForm] = useState({
    name: "",
    unit: "",
    costPrice: "",
    sellingPrice: "",
    lowStockThreshold: "",
    category: "",
    subcategory: "",
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Re-populate form when a different product is opened for editing
  const productId = product?.id ?? "";
  const [lastProductId, setLastProductId] = useState("");
  if (open && product && productId !== lastProductId) {
    setForm({
      name: product.name,
      unit: product.unit,
      costPrice: product.costPrice?.toString() ?? "",
      sellingPrice: product.sellingPrice?.toString() ?? "",
      lowStockThreshold: product.lowStockThreshold.toString(),
      category: product.category ?? "",
      subcategory: product.subcategory ?? "",
    });
    setLastProductId(productId);
  }

  async function handleSave() {
    if (!product) return;
    setSaving(true);
    setError(null);
    try {
      await api.put(`/products/${product.id}`, {
        name: form.name,
        unit: form.unit,
        costPrice: form.costPrice ? Number(form.costPrice) : null,
        sellingPrice: form.sellingPrice ? Number(form.sellingPrice) : null,
        lowStockThreshold: form.lowStockThreshold ? Number(form.lowStockThreshold) : null,
        category: form.category || null,
        subcategory: form.subcategory || null,
      });
      qc.invalidateQueries({ queryKey: ["products"] });
      qc.invalidateQueries({ queryKey: ["low-stock"] });
      toast.success("Product updated", form.name);
      handleClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to save");
    } finally {
      setSaving(false);
    }
  }

  function handleClose() {
    setForm({ name: "", unit: "", costPrice: "", sellingPrice: "", lowStockThreshold: "", category: "", subcategory: "" });
    setLastProductId("");
    setError(null);
    onClose();
  }

  return (
    <Drawer open={open} onClose={handleClose} width="md">
      {product && (
        <>
          <DrawerHeader
            title="Edit product"
            subtitle={product.name}
            onClose={handleClose}
          />
          <DrawerBody>
            <div className="space-y-4">
              <div>
                <Label className="text-xs text-slate-500 dark:text-slate-400">Name</Label>
                <Input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
              </div>
              <div>
                <Label className="text-xs text-slate-500 dark:text-slate-400">Unit</Label>
                <Input value={form.unit} onChange={(e) => setForm({ ...form, unit: e.target.value })} />
              </div>
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <Label className="text-xs text-slate-500 dark:text-slate-400">Category</Label>
                  <select
                    className="w-full h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 text-sm bg-white dark:bg-slate-900"
                    value={form.category}
                    onChange={(e) => setForm(f => ({ ...f, category: e.target.value, subcategory: "" }))}
                  >
                    <option value="">Select category</option>
                    {CATEGORY_NAMES.map((c) => (
                      <option key={c} value={c}>{c}</option>
                    ))}
                  </select>
                </div>
                <div>
                  <Label className="text-xs text-slate-500 dark:text-slate-400">Subcategory</Label>
                  {(CATEGORIES[form.category] ?? []).length > 0 ? (
                    <select
                      className="w-full h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 text-sm bg-white dark:bg-slate-900"
                      value={form.subcategory}
                      onChange={(e) => setForm(f => ({ ...f, subcategory: e.target.value }))}
                    >
                      <option value="">Select subcategory</option>
                      {(CATEGORIES[form.category] ?? []).map((s) => (
                        <option key={s} value={s}>{s}</option>
                      ))}
                    </select>
                  ) : (
                    <Input
                      value={form.subcategory}
                      onChange={(e) => setForm(f => ({ ...f, subcategory: e.target.value }))}
                      placeholder="Subcategory (optional)"
                    />
                  )}
                </div>
              </div>
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <Label className="text-xs text-slate-500 dark:text-slate-400">Selling Price</Label>
                  <Input type="number" value={form.sellingPrice} onChange={(e) => setForm(f => ({ ...f, sellingPrice: e.target.value }))} />
                </div>
                <div>
                  <Label className="text-xs text-slate-500 dark:text-slate-400">Cost Price</Label>
                  <Input type="number" value={form.costPrice} onChange={(e) => setForm(f => ({ ...f, costPrice: e.target.value }))} />
                </div>
              </div>
              <div>
                <Label className="text-xs text-slate-500 dark:text-slate-400">Low Stock Threshold</Label>
                <Input type="number" value={form.lowStockThreshold} onChange={(e) => setForm({ ...form, lowStockThreshold: e.target.value })} />
              </div>
              {error && <p className="text-xs text-rose-500">{error}</p>}

              {/* Current stock (read-only, since stock is changed via inline edit / Restock / Stock-out) */}
              <div className="border-t border-slate-200 dark:border-slate-800 pt-4 mt-2">
                <p className="text-xs font-semibold text-slate-500 dark:text-slate-400 uppercase tracking-wider mb-2">Current stock</p>
                <p className="text-2xl font-bold text-slate-900 dark:text-slate-50 tabular-nums">
                  {product.currentStock}
                  <span className="text-sm font-normal text-slate-500 dark:text-slate-400 ml-1.5">{product.unit}</span>
                </p>
                <p className="text-xs text-slate-400 dark:text-slate-500 mt-1">
                  Use the inline cell, Restock, or Stock-out actions to change stock — keeps the audit trail clean.
                </p>
              </div>
            </div>
          </DrawerBody>
          <DrawerFooter>
            <Button variant="outline" onClick={handleClose} disabled={saving}>Cancel</Button>
            <Button onClick={handleSave} disabled={saving}>{saving ? "Saving…" : "Save Changes"}</Button>
          </DrawerFooter>
        </>
      )}
    </Drawer>
  );
}

// ─── Add Hold dialog ─────────────────────────────────────────────────────────
function AddHoldDialog({
  open,
  onClose,
  products,
}: {
  open: boolean;
  onClose: () => void;
  products: ProductDto[];
}) {
  const qc = useQueryClient();
  const [form, setForm] = useState({ productId: "", contactName: "", quantity: "", notes: "" });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const selectedProduct = products.find((p) => p.id === form.productId);

  async function handleSave() {
    setSaving(true);
    setError(null);
    try {
      await api.post("/stock-holds", {
        productId: form.productId,
        contactName: form.contactName,
        quantity: Number(form.quantity),
        notes: form.notes || undefined,
      });
      qc.invalidateQueries({ queryKey: ["stock-holds"] });
      qc.invalidateQueries({ queryKey: ["products"] });
      handleClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to create hold");
    } finally {
      setSaving(false);
    }
  }

  function handleClose() {
    setForm({ productId: "", contactName: "", quantity: "", notes: "" });
    setError(null);
    onClose();
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && handleClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Hold Stock for Customer</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label>Product</Label>
            <select
              className="w-full h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 text-sm"
              value={form.productId}
              onChange={(e) => setForm({ ...form, productId: e.target.value })}
            >
              <option value="">Select product</option>
              {products.filter((p) => p.currentStock > 0).map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name} ({p.currentStock} {p.unit} available)
                </option>
              ))}
            </select>
          </div>
          <div>
            <Label>Customer Name</Label>
            <Input
              value={form.contactName}
              onChange={(e) => setForm({ ...form, contactName: e.target.value })}
              placeholder="e.g. Ada, Tunde"
            />
          </div>
          <div>
            <Label>Quantity to Hold</Label>
            <Input
              type="number"
              value={form.quantity}
              onChange={(e) => setForm({ ...form, quantity: e.target.value })}
              placeholder={selectedProduct ? `Max: ${selectedProduct.currentStock}` : ""}
            />
          </div>
          <div>
            <Label>Notes (optional)</Label>
            <Input
              value={form.notes}
              onChange={(e) => setForm({ ...form, notes: e.target.value })}
              placeholder="e.g. picking up tomorrow"
            />
          </div>
          {error && <p className="text-xs text-red-500">{error}</p>}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={handleClose} disabled={saving}>Cancel</Button>
          <Button
            onClick={handleSave}
            disabled={saving || !form.productId || !form.contactName || !form.quantity}
          >
            {saving ? "Holding…" : "Hold Stock"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ─── Delete Product dialog ───────────────────────────────────────────────────
function DeleteProductDialog({
  product,
  open,
  onClose,
}: {
  product: ProductDto | null;
  open: boolean;
  onClose: () => void;
}) {
  const qc = useQueryClient();
  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleDelete() {
    if (!product) return;
    setDeleting(true);
    setError(null);
    try {
      await api.delete(`/products/${product.id}`);
      qc.invalidateQueries({ queryKey: ["products"] });
      qc.invalidateQueries({ queryKey: ["low-stock"] });
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
          <DialogTitle>Delete Product?</DialogTitle>
        </DialogHeader>
        <p className="text-sm text-slate-600 dark:text-slate-400">
          This will mark <strong>{product?.name}</strong> as inactive. Past sales involving this
          product will still appear in reports.
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

// ─── Mark Damaged dialog ────────────────────────────────────────────────────
function DamagedDialog({ product, open, onClose }: { product: ProductDto | null; open: boolean; onClose: () => void }) {
  const qc = useQueryClient();
  const [qty, setQty] = useState("");
  const [notes, setNotes] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSave() {
    if (!product || !qty) return;
    const quantity = Number(qty);
    if (quantity <= 0 || quantity > product.currentStock) {
      setError(`Quantity must be between 1 and ${product.currentStock}`);
      return;
    }
    setSaving(true);
    setError(null);
    try {
      await api.post("/inventory/damaged", {
        productId: product.id,
        quantity,
        notes: notes || null,
      });
      qc.invalidateQueries({ queryKey: ["products"] });
      qc.invalidateQueries({ queryKey: ["low-stock"] });
      handleClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to record damaged stock");
    } finally {
      setSaving(false);
    }
  }

  function handleClose() {
    setQty("");
    setNotes("");
    setError(null);
    onClose();
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && handleClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Mark Damaged Stock</DialogTitle>
        </DialogHeader>
        {product && (
          <div className="space-y-3">
            <div className="rounded-lg bg-amber-50 border border-amber-200 p-3">
              <p className="text-sm font-medium text-amber-900">{product.name}</p>
              <p className="text-xs text-amber-700 mt-0.5">
                Current stock: {product.currentStock} {product.unit}
              </p>
            </div>
            <div>
              <Label>Damaged Quantity</Label>
              <Input
                type="number"
                value={qty}
                onChange={(e) => setQty(e.target.value)}
                placeholder={`Max ${product.currentStock}`}
                min={1}
                max={product.currentStock}
              />
            </div>
            <div>
              <Label>Notes (optional)</Label>
              <Input
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
                placeholder="e.g. Broken in transit"
              />
            </div>
            {error && <p className="text-xs text-red-500">{error}</p>}
          </div>
        )}
        <DialogFooter>
          <Button variant="outline" onClick={handleClose} disabled={saving}>Cancel</Button>
          <Button onClick={handleSave} disabled={saving || !qty} className="bg-amber-600 hover:bg-amber-700 text-white">
            {saving ? "Saving..." : "Mark Damaged"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ─── Stock Out dialog ───────────────────────────────────────────────────────
function StockOutDialog({ product, open, onClose }: { product: ProductDto | null; open: boolean; onClose: () => void }) {
  const qc = useQueryClient();
  const [qty, setQty] = useState("");
  const [notes, setNotes] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSave() {
    if (!product || !qty) return;
    const quantity = Number(qty);
    if (quantity <= 0 || quantity > product.currentStock) {
      setError(`Quantity must be between 1 and ${product.currentStock}`);
      return;
    }
    setSaving(true);
    setError(null);
    try {
      await api.post("/inventory/stock-out", {
        productId: product.id,
        quantity,
        notes: notes || null,
      });
      qc.invalidateQueries({ queryKey: ["products"] });
      qc.invalidateQueries({ queryKey: ["low-stock"] });
      handleClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to remove stock");
    } finally {
      setSaving(false);
    }
  }

  function handleClose() {
    setQty("");
    setNotes("");
    setError(null);
    onClose();
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && handleClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Remove Stock</DialogTitle>
        </DialogHeader>
        {product && (
          <div className="space-y-3">
            <div className="rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800 p-3">
              <p className="text-sm font-medium text-slate-900 dark:text-slate-50">{product.name}</p>
              <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
                Current stock: {product.currentStock} {product.unit}
              </p>
            </div>
            <div>
              <Label>Quantity to Remove</Label>
              <Input
                type="number"
                value={qty}
                onChange={(e) => setQty(e.target.value)}
                placeholder={`Max ${product.currentStock}`}
                min={1}
                max={product.currentStock}
              />
            </div>
            <div>
              <Label>Reason (optional)</Label>
              <Input
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
                placeholder="e.g. Sold offline, expired"
              />
            </div>
            {error && <p className="text-xs text-red-500">{error}</p>}
          </div>
        )}
        <DialogFooter>
          <Button variant="outline" onClick={handleClose} disabled={saving}>Cancel</Button>
          <Button onClick={handleSave} disabled={saving || !qty}>
            {saving ? "Removing..." : "Remove Stock"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ─── Restock dialog ─────────────────────────────────────────────────────────
function RestockDialog({ product, open, onClose }: { product: ProductDto | null; open: boolean; onClose: () => void }) {
  const qc = useQueryClient();
  const biz = useBusiness();
  const currencySymbol = biz?.currency ?? "\u20A6";
  const [qty, setQty] = useState("");
  const [unitCost, setUnitCost] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSave() {
    if (!product || !qty) return;
    const quantity = Number(qty);
    if (quantity <= 0) { setError("Quantity must be greater than 0"); return; }
    setSaving(true);
    setError(null);
    try {
      await api.post("/inventory/stock-in", {
        productId: product.id,
        quantity,
        unitCost: unitCost ? Number(unitCost) : undefined,
      });
      qc.invalidateQueries({ queryKey: ["products"] });
      qc.invalidateQueries({ queryKey: ["low-stock"] });
      handleClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to restock");
    } finally {
      setSaving(false);
    }
  }

  function handleClose() {
    setQty("");
    setUnitCost("");
    setError(null);
    onClose();
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && handleClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Restock</DialogTitle>
        </DialogHeader>
        {product && (
          <div className="space-y-3">
            <div className="rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800 p-3">
              <p className="text-sm font-medium text-slate-900 dark:text-slate-50">{product.name}</p>
              <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
                Current stock: {product.currentStock} {product.unit}
              </p>
            </div>
            <div>
              <Label>Quantity to Add</Label>
              <Input
                type="number"
                value={qty}
                onChange={(e) => setQty(e.target.value)}
                placeholder="e.g. 50"
                min={1}
              />
            </div>
            <div>
              <Label>Unit Cost ({currencySymbol}) — optional</Label>
              <Input
                type="number"
                value={unitCost}
                onChange={(e) => setUnitCost(e.target.value)}
                placeholder={product.costPrice ? String(product.costPrice) : "Cost per unit"}
                min={0}
              />
              <p className="text-[10px] text-slate-400 dark:text-slate-500 mt-1">Updates the product cost price if provided</p>
            </div>
            {error && <p className="text-xs text-red-500">{error}</p>}
          </div>
        )}
        <DialogFooter>
          <Button variant="outline" onClick={handleClose} disabled={saving}>Cancel</Button>
          <Button onClick={handleSave} disabled={saving || !qty} className="bg-emerald-600 hover:bg-emerald-700 text-white">
            {saving ? "Restocking..." : "Restock"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ─── Wastage dialog ────────────────────────────────────────────────────────
function WastageDialog({ open, onClose, products }: { open: boolean; onClose: () => void; products: ProductDto[] }) {
  const qc = useQueryClient();
  const [productId, setProductId] = useState("");
  const [qty, setQty] = useState("");
  const [notes, setNotes] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const selected = products.find((p) => p.id === productId);

  async function handleSave() {
    if (!productId || !qty) return;
    const quantity = Number(qty);
    if (quantity <= 0 || (selected && quantity > selected.currentStock)) {
      setError(`Quantity must be between 1 and ${selected?.currentStock ?? 0}`);
      return;
    }
    setSaving(true);
    setError(null);
    try {
      await api.post("/inventory/wastage", {
        productId,
        quantity,
        notes: notes || null,
      });
      qc.invalidateQueries({ queryKey: ["products"] });
      qc.invalidateQueries({ queryKey: ["low-stock"] });
      qc.invalidateQueries({ queryKey: ["inventory-losses"] });
      handleClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to record wastage");
    } finally {
      setSaving(false);
    }
  }

  function handleClose() {
    setProductId("");
    setQty("");
    setNotes("");
    setError(null);
    onClose();
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && handleClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Record Wastage</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label>Product</Label>
            <select
              className="w-full h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 text-sm bg-white dark:bg-slate-900"
              value={productId}
              onChange={(e) => setProductId(e.target.value)}
            >
              <option value="">Select product</option>
              {products.filter((p) => p.currentStock > 0).map((p) => (
                <option key={p.id} value={p.id}>{p.name} ({p.currentStock} {p.unit} available)</option>
              ))}
            </select>
          </div>
          <div>
            <Label>Quantity Wasted</Label>
            <Input
              type="number"
              value={qty}
              onChange={(e) => setQty(e.target.value)}
              placeholder={selected ? `Max ${selected.currentStock}` : ""}
              min={1}
              max={selected?.currentStock}
            />
          </div>
          <div>
            <Label>Reason / Notes</Label>
            <Input
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder="e.g. Expired, spoiled, spilled"
            />
          </div>
          {error && <p className="text-xs text-red-500">{error}</p>}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={handleClose} disabled={saving}>Cancel</Button>
          <Button onClick={handleSave} disabled={saving || !productId || !qty} className="bg-orange-600 hover:bg-orange-700 text-white">
            {saving ? "Saving..." : "Record Wastage"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ─── Main page ───────────────────────────────────────────────────────────────
type StockFilter = "all" | "low" | "out" | "sufficient" | "wastage";

// ─── Status-first table row with bulk select + inline edit ──────────────────
type ProductStatus = "out" | "low" | "ok";
function statusOf(p: ProductDto): ProductStatus {
  if (p.currentStock <= 0) return "out";
  if (p.isLowStock) return "low";
  return "ok";
}
function StatusPill({ s }: { s: ProductStatus }) {
  if (s === "out") return <span className="inline-flex items-center gap-1 text-[10px] font-semibold uppercase tracking-wider text-rose-600 dark:text-rose-400 bg-rose-50 dark:bg-rose-950/40 px-1.5 py-0.5 rounded ring-1 ring-rose-200 dark:ring-rose-900">Out</span>;
  if (s === "low") return <span className="inline-flex items-center gap-1 text-[10px] font-semibold uppercase tracking-wider text-amber-600 dark:text-amber-400 bg-amber-50 dark:bg-amber-950/40 px-1.5 py-0.5 rounded ring-1 ring-amber-200 dark:ring-amber-900">Low</span>;
  return <span className="inline-flex items-center gap-1 text-[10px] font-semibold uppercase tracking-wider text-emerald-600 dark:text-emerald-400 bg-emerald-50 dark:bg-emerald-950/40 px-1.5 py-0.5 rounded ring-1 ring-emerald-200 dark:ring-emerald-900">OK</span>;
}

/**
 * Inline-editable numeric cell. Click to edit, Enter / blur saves, Escape cancels.
 * Uses optimistic update via the mutate fn passed in by the parent.
 */
function InlineNumericCell({
  value,
  format,
  prefix,
  suffix,
  step = 1,
  min = 0,
  onSave,
  align = "right",
  disabled,
  className = "",
}: {
  value: number;
  format?: (n: number) => string;
  prefix?: string;
  suffix?: string;
  step?: number;
  min?: number;
  onSave: (n: number) => Promise<void>;
  align?: "left" | "right";
  disabled?: boolean;
  className?: string;
}) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(String(value));
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!editing) setDraft(String(value));
  }, [value, editing]);

  const display = format ? format(value) : `${prefix ?? ""}${value}${suffix ?? ""}`;

  if (disabled || !editing) {
    return (
      <button
        type="button"
        onClick={(e) => { e.stopPropagation(); if (!disabled) setEditing(true); }}
        className={`px-2 py-1 -mx-2 rounded transition-colors text-${align} tabular-nums ${
          disabled
            ? "cursor-default"
            : "cursor-text hover:bg-cyan-50 dark:hover:bg-cyan-950/30 hover:ring-1 hover:ring-cyan-200 dark:hover:ring-cyan-900"
        } ${className}`}
        title={disabled ? undefined : "Click to edit"}
      >
        {display}
      </button>
    );
  }

  const commit = async () => {
    const n = Number(draft);
    if (!Number.isFinite(n) || n < min || n === value) {
      setEditing(false);
      setDraft(String(value));
      return;
    }
    setSaving(true);
    try {
      await onSave(n);
    } finally {
      setSaving(false);
      setEditing(false);
    }
  };

  return (
    <input
      type="number"
      autoFocus
      step={step}
      min={min}
      value={draft}
      disabled={saving}
      onChange={(e) => setDraft(e.target.value)}
      onClick={(e) => e.stopPropagation()}
      onBlur={commit}
      onKeyDown={(e) => {
        if (e.key === "Enter") { e.preventDefault(); (e.target as HTMLInputElement).blur(); }
        if (e.key === "Escape") { setEditing(false); setDraft(String(value)); }
      }}
      className={`w-20 px-2 py-1 -mx-2 rounded text-${align} tabular-nums bg-white dark:bg-slate-900 ring-1 ring-cyan-400 outline-none ${className}`}
    />
  );
}

function ProductRow({
  product,
  selected,
  onToggleSelect,
  onClickRow,
  onSavePrice,
  onSaveStock,
  onSaveThreshold,
  canEdit,
}: {
  product: ProductDto;
  selected: boolean;
  onToggleSelect: () => void;
  onClickRow: () => void;
  onSavePrice: (n: number) => Promise<void>;
  onSaveStock: (n: number) => Promise<void>;
  onSaveThreshold: (n: number) => Promise<void>;
  canEdit: boolean;
}) {
  const status = statusOf(product);
  return (
    <div
      onClick={onClickRow}
      className={`group grid grid-cols-[auto_minmax(0,1fr)_auto_auto_auto_auto] sm:grid-cols-[auto_minmax(0,1.6fr)_auto_minmax(0,0.8fr)_auto_auto_auto_auto] items-center gap-3 px-3 py-2.5 border-b border-slate-100 dark:border-slate-800 transition-colors cursor-pointer ${
        selected ? "bg-cyan-50/60 dark:bg-cyan-950/20" : "hover:bg-slate-50 dark:hover:bg-slate-800/50"
      }`}
    >
      <input
        type="checkbox"
        checked={selected}
        onClick={(e) => e.stopPropagation()}
        onChange={onToggleSelect}
        className="h-4 w-4 rounded border-slate-300 dark:border-slate-700 accent-cyan-500"
        aria-label={`Select ${product.name}`}
      />
      <div className="min-w-0">
        <div className="flex items-center gap-2 min-w-0">
          <p className="text-sm font-medium text-slate-900 dark:text-slate-50 truncate">{product.name}</p>
          <StatusPill s={status} />
        </div>
        {product.sku && <p className="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5 truncate">SKU {product.sku}</p>}
      </div>
      <div className="hidden sm:block">
        <StatusPill s={status} />
      </div>
      <div className="hidden sm:block min-w-0">
        {product.category && (
          <span className="text-xs text-slate-500 dark:text-slate-400 truncate block">{product.category}</span>
        )}
      </div>
      <div className="text-right">
        <p className="text-[10px] text-slate-400 dark:text-slate-500 uppercase tracking-wider">Price</p>
        <InlineNumericCell
          value={product.sellingPrice ?? 0}
          format={(n) => formatNaira(n)}
          step={1}
          onSave={onSavePrice}
          disabled={!canEdit}
        />
      </div>
      <div className="text-right">
        <p className="text-[10px] text-slate-400 dark:text-slate-500 uppercase tracking-wider">Stock</p>
        <InlineNumericCell
          value={product.currentStock}
          suffix={` ${product.unit ?? ""}`.trimEnd()}
          step={1}
          min={0}
          onSave={onSaveStock}
          disabled={!canEdit}
          className={status === "out" ? "text-rose-600 dark:text-rose-400 font-semibold" : status === "low" ? "text-amber-600 dark:text-amber-400 font-semibold" : ""}
        />
      </div>
      <div className="hidden sm:block text-right">
        <p className="text-[10px] text-slate-400 dark:text-slate-500 uppercase tracking-wider">Threshold</p>
        <InlineNumericCell
          value={product.lowStockThreshold ?? 0}
          step={1}
          min={0}
          onSave={onSaveThreshold}
          disabled={!canEdit}
        />
      </div>
      <div className="opacity-0 group-hover:opacity-100 transition-opacity">
        <Pencil size={14} className="text-slate-400 dark:text-slate-500" />
      </div>
    </div>
  );
}

export default function InventoryPage() {
  const { toast } = useToast();
  const [adding, setAdding] = useState(false);
  const [addingHold, setAddingHold] = useState(false);
  const [editing, setEditing] = useState<ProductDto | null>(null);
  const [deleting, setDeleting] = useState<ProductDto | null>(null);
  const [damaging, setDamaging] = useState<ProductDto | null>(null);
  const [removingStock, setRemovingStock] = useState<ProductDto | null>(null);
  const [restocking, setRestocking] = useState<ProductDto | null>(null);
  const [wastaging, setWastaging] = useState(false);
  const [stockFilter, setStockFilter] = useStickyState<StockFilter>(
    "inventory-stock-filter",
    "all",
    (v): v is StockFilter => v === "all" || v === "low" || v === "out" || v === "sufficient" || v === "wastage",
  );
  const [categoryFilter, setCategoryFilter] = useStickyState<string>("inventory-category-filter", "");
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [page, setPage] = useState(1);
  const PAGE_SIZE = 50;
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [bulkConfirmDelete, setBulkConfirmDelete] = useState(false);
  // View mode: dense list (new) vs card grid (legacy). Persisted across visits.
  const [viewMode, setViewMode] = useStickyState<"list" | "grid">(
    "ojunai-inventory-view",
    "list",
    (v): v is "list" | "grid" => v === "list" || v === "grid",
  );
  const { data: planStatus } = usePlanStatus();
  const hasHolds = planStatus?.hasStockHolds ?? true;

  // Auto-open create dialog from ?new=1 (dashboard quick action)
  useEffect(() => {
    if (new URLSearchParams(window.location.search).get("new") === "1" && hasPermission(Permission.ManageStock)) {
      setAdding(true);
    }
  }, []);

  // Debounce search so each keystroke doesn't fire a request. 250ms matches the rest of
  // the dashboard's search inputs.
  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search.trim()), 250);
    return () => clearTimeout(t);
  }, [search]);

  // Any filter / search change resets pagination to page 1 — otherwise you'd land on page 7
  // of a result set that only has 2 pages after narrowing.
  useEffect(() => { setPage(1); }, [debouncedSearch, stockFilter, categoryFilter]);

  // Map the UI's stock-filter chip to the API's stockLevel query param.
  const stockLevelParam = stockFilter === "low" ? "low"
    : stockFilter === "out" ? "out"
    : stockFilter === "sufficient" ? "sufficient"
    : "";

  // Wastage is a separate view (not a stock-level filter), so when that chip is active we
  // intentionally suppress the products query.
  const productsQueryEnabled = stockFilter !== "wastage";

  const { data: productsData, isLoading } = useQuery({
    queryKey: ["products", debouncedSearch, categoryFilter, stockLevelParam, page],
    queryFn: async () => {
      const params = new URLSearchParams();
      params.set("page", String(page));
      params.set("pageSize", String(PAGE_SIZE));
      if (debouncedSearch) params.set("search", debouncedSearch);
      if (categoryFilter) params.set("category", categoryFilter);
      if (stockLevelParam) params.set("stockLevel", stockLevelParam);
      const { data } = await api.get<{ data: PaginatedResult<ProductDto> }>(`/products?${params.toString()}`);
      return data.data;
    },
    enabled: productsQueryEnabled,
    staleTime: 30_000,
  });

  const { data: lowStock } = useQuery({
    queryKey: ["low-stock"],
    queryFn: async () => {
      const { data } = await api.get<{ data: ProductDto[] }>("/products/low-stock");
      return data.data!;
    },
  });

  // Filter-chip count source. Honors the same search + category as the products list so the
  // chip numbers stay in sync with what the user is searching. Stock-level filter is NOT
  // applied here — we want the count of OTHER stock levels too, not just the current one.
  const { data: stockStats } = useQuery({
    queryKey: ["product-stock-stats", debouncedSearch, categoryFilter],
    queryFn: async () => {
      const params = new URLSearchParams();
      if (debouncedSearch) params.set("search", debouncedSearch);
      if (categoryFilter) params.set("category", categoryFilter);
      const url = `/products/stats${params.toString() ? `?${params.toString()}` : ""}`;
      const { data } = await api.get<{ data: { total: number; outOfStock: number; low: number; sufficient: number } }>(url);
      return data.data;
    },
    staleTime: 30_000,
  });

  const qc = useQueryClient();
  const { data: holds } = useQuery({
    queryKey: ["stock-holds"],
    queryFn: async () => {
      const { data } = await api.get<{ data: StockHoldDto[] }>("/stock-holds");
      return data.data!;
    },
    enabled: hasHolds,
  });

  // Wastage & damaged history
  interface LossEntry { id: string; productId: string; productName: string; type: string; quantity: number; notes?: string; createdAtUtc: string; }
  const { data: lossesData } = useQuery({
    queryKey: ["inventory-losses"],
    queryFn: async () => {
      const all = await fetchAllPaged<LossEntry>(
        (p, ps) => `/inventory/transactions?page=${p}&pageSize=${ps}`
      );
      // Filter to only Damaged and Wastage
      const filtered = all.filter(
        (t) => t.type === "Damaged" || t.type === "Wastage"
      );
      return filtered;
    },
  });

  const [holdAction, setHoldAction] = useState<{ id: string; type: "release" | "convert" } | null>(null);

  async function handleHoldAction(holdId: string, action: "release" | "convert") {
    setHoldAction({ id: holdId, type: action });
    try {
      await api.post(`/stock-holds/${holdId}/${action}`);
      qc.invalidateQueries({ queryKey: ["stock-holds"] });
      qc.invalidateQueries({ queryKey: ["products"] });
      qc.invalidateQueries({ queryKey: ["sales"] });
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      toast.error(`Couldn't ${action === "release" ? "release" : "convert"} hold`, ax.response?.data?.errors?.[0] ?? "Please try again.");
    } finally {
      setHoldAction(null);
    }
  }

  // Compute filtered products. allProducts is the current PAGE from the server — filtering by
  // stock level + category + search is already applied server-side, so this view-list is the
  // visible result for the active filters.
  const allProducts = useMemo(() => productsData?.items ?? [], [productsData]);
  const totalPages = productsData ? Math.max(1, Math.ceil(productsData.totalCount / PAGE_SIZE)) : 1;

  // lowStockIds comes from a separate /products/low-stock query — used by the per-row
  // "low stock" badge and outOfStockIds is derived per-row from currentStock. Both still
  // work because they're computed per-product on the visible page.
  const lowStockIds = useMemo(() => new Set((lowStock ?? []).map((p) => p.id)), [lowStock]);
  const outOfStockIds = useMemo(() => new Set(allProducts.filter((p) => p.currentStock <= 0).map((p) => p.id)), [allProducts]);
  const lowOnlyIds = useMemo(() => {
    const s = new Set<string>();
    lowStockIds.forEach((id) => { if (!outOfStockIds.has(id)) s.add(id); });
    return s;
  }, [lowStockIds, outOfStockIds]);
  // Filter-chip count: total matching the active filter (from server response). When no
  // stockLevel filter is active, this is the total product count; when a stockLevel is
  // active, it's the count for THAT level.
  const totalForCurrentFilter = productsData?.totalCount ?? 0;

  // Inline edit save — uses existing PUT /products/:id
  async function saveProductPatch(p: ProductDto, patch: Partial<Pick<ProductDto, "sellingPrice" | "currentStock" | "lowStockThreshold">>) {
    await api.put(`/products/${p.id}`, {
      name: p.name,
      sku: p.sku,
      unit: p.unit,
      costPrice: p.costPrice,
      sellingPrice: patch.sellingPrice ?? p.sellingPrice,
      currentStock: patch.currentStock ?? p.currentStock,
      lowStockThreshold: patch.lowStockThreshold ?? p.lowStockThreshold,
      category: p.category,
      subcategory: p.subcategory,
      voiceDescription: p.voiceDescription,
      aliases: p.aliases,
      isActive: p.isActive,
    });
    qc.invalidateQueries({ queryKey: ["products"] });
    qc.invalidateQueries({ queryKey: ["low-stock"] });
  }

  function toggleSelect(id: string) {
    setSelectedIds((s) => {
      const n = new Set(s);
      if (n.has(id)) n.delete(id); else n.add(id);
      return n;
    });
  }
  function clearSelection() { setSelectedIds(new Set()); }
  function toggleSelectAll(visible: ProductDto[]) {
    setSelectedIds((s) => {
      const allVisibleSelected = visible.every((p) => s.has(p.id));
      if (allVisibleSelected) {
        const n = new Set(s);
        visible.forEach((p) => n.delete(p.id));
        return n;
      }
      const n = new Set(s);
      visible.forEach((p) => n.add(p.id));
      return n;
    });
  }

  async function bulkDelete() {
    const ids = Array.from(selectedIds);
    await Promise.all(ids.map((id) => api.delete(`/products/${id}`).catch(() => null)));
    qc.invalidateQueries({ queryKey: ["products"] });
    qc.invalidateQueries({ queryKey: ["low-stock"] });
    clearSelection();
    setBulkConfirmDelete(false);
  }

  function bulkExportCsv() {
    const ids = selectedIds;
    const rows = allProducts.filter((p) => ids.has(p.id));
    const headers = ["Name", "SKU", "Category", "Unit", "Cost", "Price", "Stock", "Threshold", "Status"];
    const csv = [headers.join(",")]
      .concat(
        rows.map((p) =>
          [
            JSON.stringify(p.name ?? ""),
            JSON.stringify(p.sku ?? ""),
            JSON.stringify(p.category ?? ""),
            JSON.stringify(p.unit ?? ""),
            p.costPrice ?? 0,
            p.sellingPrice ?? 0,
            p.currentStock ?? 0,
            p.lowStockThreshold ?? 0,
            statusOf(p),
          ].join(",")
        )
      )
      .join("\n");
    const blob = new Blob([csv], { type: "text/csv;charset=utf-8;" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `inventory-${new Date().toISOString().slice(0, 10)}.csv`;
    document.body.appendChild(a); a.click(); a.remove();
    URL.revokeObjectURL(url);
  }

  // Get unique categories + staff names from actual products for filter dropdowns
  const usedCategories = useMemo(() => {
    const cats = new Set<string>();
    allProducts.forEach((p) => { if (p.category) cats.add(p.category); });
    return Array.from(cats).sort();
  }, [allProducts]);

  const isWastageView = stockFilter === "wastage";
  // Server-side filtering: search + stockLevel + category are all applied via query params on
  // the /products fetch. The visible list is exactly the items on the current page that match.
  // Wastage view doesn't list products at all — it renders the losses table elsewhere.
  const filteredProducts = useMemo(() => {
    return stockFilter === "wastage" ? [] : allProducts;
  }, [allProducts, stockFilter]);

  return (
    <div className="space-y-6">
      <PageHeader
        title="Inventory"
        subtitle="Current stock levels for all products"
        actions={
          hasPermission(Permission.ManageStock) ? (
            <>
              <Button variant="outline" onClick={() => setWastaging(true)} className="text-orange-600 border-orange-200 hover:bg-orange-50">
                <Ban size={14} className="mr-1" /> Wastage
              </Button>
              {hasHolds && (
                <Button variant="outline" onClick={() => setAddingHold(true)}>
                  <Lock size={14} className="mr-1" /> Hold Stock
                </Button>
              )}
              <Button onClick={() => setAdding(true)}>+ Add Product</Button>
            </>
          ) : null
        }
      />

      {/* Low stock alert banner */}
      {lowStock && lowStock.length > 0 && (
        <div className="bg-amber-50 border border-amber-200 rounded-xl p-4 flex items-start gap-3">
          <AlertTriangle size={18} className="text-amber-500 flex-shrink-0 mt-0.5" />
          <div>
            <p className="text-sm font-semibold text-amber-800">
              {lowStock.length} product{lowStock.length !== 1 ? "s" : ""} running low
            </p>
            <p className="text-xs text-amber-600 mt-0.5">
              {lowStock.map((p) => p.name).join(", ")}
            </p>
          </div>
        </div>
      )}

      {/* Active Holds */}
      {!hasHolds && (
        <UpgradeInline feature="Stock Holds" plan="Shop" />
      )}
      {hasHolds && holds && holds.length > 0 && (
        <Card>
          <CardContent className="pt-4">
            <div className="flex items-center gap-2 mb-3">
              <Lock size={16} className="text-cyan-500" />
              <h3 className="text-sm font-semibold text-slate-700 dark:text-slate-300">
                Active Holds ({holds.length})
              </h3>
            </div>
            <div className="space-y-2">
              {holds.map((hold) => (
                <div
                  key={hold.id}
                  className="flex items-center justify-between border rounded-lg px-3 py-2"
                >
                  <div className="flex-1 min-w-0">
                    <p className="text-sm font-medium text-slate-900 dark:text-slate-50 truncate">
                      {hold.quantity} {hold.unit} of {hold.productName}
                    </p>
                    <p className="text-xs text-slate-500 dark:text-slate-400">
                      For <strong>{hold.contactName}</strong> — {formatDateTime(hold.createdAtUtc)}
                    </p>
                  </div>
                  <div className="flex items-center gap-1 ml-2 flex-shrink-0">
                    {hasPermission(Permission.RecordSales) && (
                      <button
                        onClick={() => handleHoldAction(hold.id, "convert")}
                        disabled={holdAction?.id === hold.id}
                        className="flex items-center gap-1 px-2 py-1 rounded text-xs font-medium bg-emerald-50 text-emerald-700 hover:bg-emerald-100 transition-colors"
                        title="Convert to sale"
                      >
                        <ShoppingCart size={12} />
                        Sell
                      </button>
                    )}
                    {hasPermission(Permission.ManageStock) && (
                      <button
                        onClick={() => handleHoldAction(hold.id, "release")}
                        disabled={holdAction?.id === hold.id}
                        className="flex items-center gap-1 px-2 py-1 rounded text-xs font-medium bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400 hover:bg-slate-200 dark:hover:bg-slate-700 transition-colors"
                        title="Release hold"
                      >
                        <Unlock size={12} />
                        Release
                      </button>
                    )}
                  </div>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      )}

      {/* Status tabs (left) + view toggle (right) */}
      <div className="flex items-center justify-between gap-3 flex-wrap">
        <div className="overflow-x-auto -mx-1 px-1 flex-1">
          <div className="inline-flex items-center gap-1 bg-slate-100 dark:bg-slate-800 p-1 rounded-lg">
            {([
              { id: "all", label: "All", count: stockStats?.total ?? 0, dot: "bg-slate-400" },
              { id: "sufficient", label: "In stock", count: stockStats?.sufficient ?? 0, dot: "bg-emerald-500" },
              { id: "low", label: "Low", count: stockStats?.low ?? 0, dot: "bg-amber-500" },
              { id: "out", label: "Out", count: stockStats?.outOfStock ?? 0, dot: "bg-rose-500" },
              { id: "wastage", label: "Damaged / Wastage", count: lossesData?.length ?? 0, dot: "bg-orange-500" },
            ] as { id: StockFilter; label: string; count: number; dot: string }[]).map((t) => {
              const active = stockFilter === t.id;
              return (
                <button
                  key={t.id}
                  onClick={() => setStockFilter(t.id)}
                  className={`flex items-center gap-2 px-3 py-1.5 rounded-md text-sm font-medium whitespace-nowrap transition-colors ${
                    active
                      ? "bg-white dark:bg-slate-900 text-slate-900 dark:text-slate-50 shadow-sm ring-1 ring-slate-200 dark:ring-slate-700"
                      : "text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-50"
                  }`}
                >
                  <span className={`inline-block w-1.5 h-1.5 rounded-full ${t.dot}`} />
                  {t.label}
                  <span className={`text-[11px] tabular-nums ${active ? "text-slate-500 dark:text-slate-400" : "text-slate-400 dark:text-slate-500"}`}>
                    {t.count}
                  </span>
                </button>
              );
            })}
          </div>
        </div>
        {/* View toggle — list (dense) vs grid (legacy cards). Hidden on the wastage tab. */}
        {!isWastageView && (
          <div className="inline-flex items-center bg-slate-100 dark:bg-slate-800 p-1 rounded-lg flex-shrink-0">
            <button
              onClick={() => setViewMode("list")}
              aria-label="Switch to list view"
              title="List view"
              className={`flex items-center gap-1.5 px-2.5 py-1.5 rounded-md text-xs font-medium transition-colors ${
                viewMode === "list"
                  ? "bg-white dark:bg-slate-900 text-slate-900 dark:text-slate-50 shadow-sm ring-1 ring-slate-200 dark:ring-slate-700"
                  : "text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-50"
              }`}
            >
              <LayoutList size={14} />
              <span className="hidden sm:inline">List</span>
            </button>
            <button
              onClick={() => setViewMode("grid")}
              aria-label="Switch to grid view"
              title="Grid view"
              className={`flex items-center gap-1.5 px-2.5 py-1.5 rounded-md text-xs font-medium transition-colors ${
                viewMode === "grid"
                  ? "bg-white dark:bg-slate-900 text-slate-900 dark:text-slate-50 shadow-sm ring-1 ring-slate-200 dark:ring-slate-700"
                  : "text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-50"
              }`}
            >
              <LayoutGrid size={14} />
              <span className="hidden sm:inline">Grid</span>
            </button>
          </div>
        )}
      </div>

      {/* Search + Filters */}
      <div className="flex items-center gap-4 flex-wrap">
          <div className="relative w-full sm:max-w-xs">
            <Search size={14} className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400 dark:text-slate-500 pointer-events-none" />
            <Input
              type="search"
              placeholder="Search by name or SKU..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="pl-9 pr-9"
            />
            {search && (
              <button onClick={() => setSearch("")} className="absolute right-2 top-1/2 -translate-y-1/2 text-slate-400 dark:text-slate-500 hover:text-slate-700 dark:hover:text-slate-300 p-1 rounded" type="button">
                <X size={14} />
              </button>
            )}
          </div>
          {usedCategories.length > 0 && (
            <div className="flex items-center gap-2">
              <Label className="text-sm text-slate-500 dark:text-slate-400 whitespace-nowrap">Category:</Label>
              <select
                className="h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 text-sm"
                value={categoryFilter}
                onChange={(e) => setCategoryFilter(e.target.value)}
              >
                <option value="">All</option>
                {usedCategories.map((c) => (
                  <option key={c} value={c}>{c}</option>
                ))}
              </select>
            </div>
          )}
          {(categoryFilter || search) && (
            <button
              onClick={() => { setCategoryFilter(""); setSearch(""); }}
              className="text-xs text-cyan-600 hover:underline"
            >
              Clear filters
            </button>
          )}
        </div>

      {/* Wastage & Damaged History */}
      {(isWastageView || (!isWastageView && lossesData && lossesData.length > 0)) && (
        <Card>
          <CardContent className="pt-4">
            <div className="flex items-center gap-2 mb-3">
              <Ban size={16} className="text-orange-500" />
              <h3 className="text-sm font-semibold text-slate-700 dark:text-slate-300">
                Recent Wastage & Damaged Stock
              </h3>
            </div>
            <div className="space-y-2">
              {(isWastageView ? lossesData ?? [] : (lossesData ?? []).slice(0, 8)).map((entry) => (
                <div
                  key={entry.id}
                  className="flex items-center justify-between border rounded-lg px-3 py-2"
                >
                  <div className="flex-1 min-w-0">
                    <p className="text-sm font-medium text-slate-900 dark:text-slate-50 truncate">
                      {entry.quantity} of {entry.productName}
                    </p>
                    <p className="text-xs text-slate-500 dark:text-slate-400">
                      {entry.notes ?? "No notes"} — {formatDateTime(entry.createdAtUtc)}
                    </p>
                  </div>
                  <Badge
                    variant="outline"
                    className={`text-xs ml-2 flex-shrink-0 ${
                      entry.type === "Wastage"
                        ? "text-orange-600 border-orange-300"
                        : "text-amber-600 border-amber-300"
                    }`}
                  >
                    {entry.type}
                  </Badge>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      )}

      {/* Product list / grid */}
      {!isWastageView && (
        isLoading ? (
          viewMode === "list" ? (
            <div className="space-y-2">
              {Array.from({ length: 8 }).map((_, i) => (
                <Skeleton key={i} className="h-12 rounded-lg" />
              ))}
            </div>
          ) : (
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
              {Array.from({ length: 9 }).map((_, i) => (
                <Skeleton key={i} className="h-36 rounded-xl" />
              ))}
            </div>
          )
        ) : filteredProducts.length === 0 ? (
          <EmptyState
            icon={<Package size={22} />}
            title={
              stockFilter !== "all" || categoryFilter || search
                ? "No products match this filter"
                : "No products yet"
            }
            description={
              stockFilter !== "all" || categoryFilter || search
                ? "Try clearing filters or searching a different term."
                : "Add your first product via WhatsApp or click + Add Product above."
            }
            action={
              !(stockFilter !== "all" || categoryFilter || search) && hasPermission(Permission.ManageStock) ? (
                <Button onClick={() => setAdding(true)}>+ Add Product</Button>
              ) : undefined
            }
          />
        ) : viewMode === "list" ? (
          <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden">
            {/* List header with select-all */}
            <div className="grid grid-cols-[auto_minmax(0,1fr)_auto_auto_auto_auto] sm:grid-cols-[auto_minmax(0,1.6fr)_auto_minmax(0,0.8fr)_auto_auto_auto_auto] items-center gap-3 px-3 py-2 border-b border-slate-200 dark:border-slate-800 bg-slate-50/50 dark:bg-slate-950/50">
              <input
                type="checkbox"
                checked={filteredProducts.length > 0 && filteredProducts.every((p) => selectedIds.has(p.id))}
                onChange={() => toggleSelectAll(filteredProducts)}
                className="h-4 w-4 rounded border-slate-300 dark:border-slate-700 accent-cyan-500"
                aria-label="Select all visible"
              />
              <p className="text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">
                Product · {filteredProducts.length} item{filteredProducts.length === 1 ? "" : "s"}
              </p>
              <div className="hidden sm:block" />
              <div className="hidden sm:block text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">Category</div>
              <div className="text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 text-right">Price</div>
              <div className="text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 text-right">Stock</div>
              <div className="hidden sm:block text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 text-right">Threshold</div>
              <div />
            </div>
            {filteredProducts.map((product) => (
              <ProductRow
                key={product.id}
                product={product}
                selected={selectedIds.has(product.id)}
                onToggleSelect={() => toggleSelect(product.id)}
                onClickRow={() => setEditing(product)}
                onSavePrice={(n) => saveProductPatch(product, { sellingPrice: n })}
                onSaveStock={(n) => saveProductPatch(product, { currentStock: n })}
                onSaveThreshold={(n) => saveProductPatch(product, { lowStockThreshold: n })}
                canEdit={hasPermission(Permission.ManageStock)}
              />
            ))}
          </div>
        ) : (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
            {filteredProducts.map((product) => (
              <ProductCard
                key={product.id}
                product={product}
                onEdit={setEditing}
                onDelete={setDeleting}
                onDamaged={setDamaging}
                onStockOut={setRemovingStock}
                onRestock={setRestocking}
              />
            ))}
          </div>
        )
      )}

      {/* Pagination — only renders when more than one page of products matches the filter.
          Inventory below the PAGE_SIZE threshold (50 today) stays uncluttered. */}
      {!isWastageView && totalPages > 1 && (
        <div className="flex items-center justify-between text-xs text-slate-500 dark:text-slate-400 mt-3">
          <span>
            Page {page} of {totalPages} · {totalForCurrentFilter} product{totalForCurrentFilter === 1 ? "" : "s"}
          </span>
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              onClick={() => setPage(p => Math.max(1, p - 1))}
              disabled={page <= 1}
            >
              Previous
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setPage(p => Math.min(totalPages, p + 1))}
              disabled={page >= totalPages}
            >
              Next
            </Button>
          </div>
        </div>
      )}

      {/* Sticky bulk action bar */}
      {selectedIds.size > 0 && (
        <div className="sticky bottom-4 z-20 mx-auto max-w-2xl">
          <div className="bg-slate-900 dark:bg-slate-100 text-white dark:text-slate-900 rounded-xl shadow-2xl ring-1 ring-slate-700 dark:ring-slate-300 px-4 py-3 flex items-center gap-3">
            <span className="text-sm font-semibold tabular-nums">
              {selectedIds.size} selected
            </span>
            <span className="flex-1" />
            <button
              onClick={bulkExportCsv}
              className="text-xs font-semibold px-3 py-1.5 rounded-md bg-white/10 dark:bg-slate-900/10 hover:bg-white/20 dark:hover:bg-slate-900/20 transition-colors"
            >
              Export CSV
            </button>
            {hasPermission(Permission.ManageStock) && (
              <button
                onClick={() => setBulkConfirmDelete(true)}
                className="text-xs font-semibold px-3 py-1.5 rounded-md bg-rose-500/90 hover:bg-rose-500 text-white transition-colors"
              >
                Delete
              </button>
            )}
            <button
              onClick={clearSelection}
              className="text-xs font-medium text-slate-300 dark:text-slate-600 hover:text-white dark:hover:text-slate-900 transition-colors"
            >
              Clear
            </button>
          </div>
        </div>
      )}

      {/* Bulk delete confirmation */}
      {bulkConfirmDelete && (
        <Dialog open={bulkConfirmDelete} onOpenChange={(o) => !o && setBulkConfirmDelete(false)}>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>Delete {selectedIds.size} product{selectedIds.size === 1 ? "" : "s"}?</DialogTitle>
            </DialogHeader>
            <p className="text-sm text-slate-600 dark:text-slate-400">
              This action can&rsquo;t be undone. Products with sales history will be soft-deleted.
            </p>
            <DialogFooter>
              <Button variant="outline" onClick={() => setBulkConfirmDelete(false)}>Cancel</Button>
              <Button onClick={bulkDelete} className="bg-rose-600 hover:bg-rose-700 text-white">
                Delete {selectedIds.size}
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      )}

      <AddProductDialog open={adding} onClose={() => setAdding(false)} />
      <AddHoldDialog open={addingHold} onClose={() => setAddingHold(false)} products={allProducts} />
      <EditProductDialog
        product={editing}
        open={editing !== null}
        onClose={() => setEditing(null)}
      />
      <DeleteProductDialog
        product={deleting}
        open={deleting !== null}
        onClose={() => setDeleting(null)}
      />
      <DamagedDialog
        product={damaging}
        open={damaging !== null}
        onClose={() => setDamaging(null)}
      />
      <StockOutDialog
        product={removingStock}
        open={removingStock !== null}
        onClose={() => setRemovingStock(null)}
      />
      <RestockDialog
        product={restocking}
        open={restocking !== null}
        onClose={() => setRestocking(null)}
      />
      <WastageDialog
        open={wastaging}
        onClose={() => setWastaging(false)}
        products={allProducts}
      />
    </div>
  );
}

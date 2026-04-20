"use client";

import { useState, useMemo } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
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
import { AlertTriangle, Package, Pencil, Trash2, Lock, Unlock, ShoppingCart, Ban, Minus, RotateCcw } from "lucide-react";
import { formatDateTime } from "@/lib/format";
import { usePlanStatus } from "@/lib/use-plan-status";
import { UpgradeInline } from "@/components/upgrade-prompt";

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
          className="w-full h-9 px-2 rounded-md border border-slate-200 text-sm bg-white"
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
          <p className="text-xs text-slate-400 mt-1">Current: {category}</p>
        )}
      </div>
      <div>
        <Label>Subcategory</Label>
        {subcategories.length > 0 ? (
          <select
            className="w-full h-9 px-2 rounded-md border border-slate-200 text-sm bg-white"
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
function ProductCard({
  product,
  onEdit,
  onDelete,
  onDamaged,
  onStockOut,
}: {
  product: ProductDto;
  onEdit: (p: ProductDto) => void;
  onDelete: (p: ProductDto) => void;
  onDamaged: (p: ProductDto) => void;
  onStockOut: (p: ProductDto) => void;
}) {
  return (
    <Card className={product.isLowStock ? "border-amber-300 bg-amber-50" : ""}>
      <CardContent className="p-4">
        <div className="flex items-start justify-between">
          <div className="flex-1 min-w-0">
            <p className="font-semibold text-slate-900 truncate">{product.name}</p>
            {product.category && (
              <p className="text-xs text-slate-400 mt-0.5">
                {product.category}{product.subcategory ? ` / ${product.subcategory}` : ""}
              </p>
            )}
            {!product.category && (
              <p className="text-xs text-slate-300 mt-0.5 italic">Uncategorized</p>
            )}
            {product.sku && (
              <p className="text-xs text-slate-400 font-mono mt-0.5">SKU: {product.sku}</p>
            )}
          </div>
          <div className="flex items-center gap-1 flex-shrink-0 ml-2">
            {product.isLowStock && (
              <AlertTriangle size={14} className="text-amber-500 mt-0.5" />
            )}
            {hasPermission(Permission.ManageStock) && (
              <>
                <button
                  onClick={() => onEdit(product)}
                  className="p-1 rounded hover:bg-slate-100 text-slate-500 hover:text-slate-900"
                  title="Edit"
                >
                  <Pencil size={14} />
                </button>
                {product.currentStock > 0 && (
                  <>
                    <button
                      onClick={() => onStockOut(product)}
                      className="p-1 rounded hover:bg-slate-100 text-slate-500 hover:text-slate-700"
                      title="Remove stock"
                    >
                      <Minus size={14} />
                    </button>
                    <button
                      onClick={() => onDamaged(product)}
                      className="p-1 rounded hover:bg-amber-50 text-slate-500 hover:text-amber-600"
                      title="Mark damaged"
                    >
                      <Ban size={14} />
                    </button>
                  </>
                )}
                <button
                  onClick={() => onDelete(product)}
                  className="p-1 rounded hover:bg-red-50 text-slate-500 hover:text-red-600"
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
            <p className="text-2xl font-bold text-slate-900">
              {product.currentStock}
              <span className="text-sm font-normal text-slate-500 ml-1">{product.unit}</span>
            </p>
            <p className="text-xs text-slate-400">
              Threshold: {product.lowStockThreshold} {product.unit}
            </p>
          </div>
          <div className="text-right">
            {product.sellingPrice && (
              <p className="text-sm font-semibold text-emerald-600">
                {formatNaira(product.sellingPrice)}
              </p>
            )}
            {product.costPrice && (
              <p className="text-xs text-slate-400">Cost: {formatNaira(product.costPrice)}</p>
            )}
          </div>
        </div>

        <div className="mt-2 flex items-center gap-2 flex-wrap">
          {product.isLowStock && (
            <Badge variant="outline" className="text-xs text-amber-600 border-amber-300">
              Low Stock
            </Badge>
          )}
          {product.recordedByName && (
            <span className="text-xs text-slate-400">by {product.recordedByName}</span>
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
    <Dialog open={open} onOpenChange={(o) => !o && handleClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Edit Product</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label>Name</Label>
            <Input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
          </div>
          <div>
            <Label>Unit</Label>
            <Input value={form.unit} onChange={(e) => setForm({ ...form, unit: e.target.value })} />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <Label>Category</Label>
              <select
                className="w-full h-9 px-2 rounded-md border border-slate-200 text-sm bg-white"
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
              <Label>Subcategory</Label>
              {(CATEGORIES[form.category] ?? []).length > 0 ? (
                <select
                  className="w-full h-9 px-2 rounded-md border border-slate-200 text-sm bg-white"
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
              <Label>Selling Price</Label>
              <Input type="number" value={form.sellingPrice} onChange={(e) => setForm(f => ({ ...f, sellingPrice: e.target.value }))} />
            </div>
            <div>
              <Label>Cost Price</Label>
              <Input type="number" value={form.costPrice} onChange={(e) => setForm(f => ({ ...f, costPrice: e.target.value }))} />
            </div>
          </div>
          <div>
            <Label>Low Stock Threshold</Label>
            <Input type="number" value={form.lowStockThreshold} onChange={(e) => setForm({ ...form, lowStockThreshold: e.target.value })} />
          </div>
          {error && <p className="text-xs text-red-500">{error}</p>}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={handleClose} disabled={saving}>Cancel</Button>
          <Button onClick={handleSave} disabled={saving}>{saving ? "Saving…" : "Save Changes"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
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
              className="w-full h-9 px-2 rounded-md border border-slate-200 text-sm"
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
        <p className="text-sm text-slate-600">
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
            <div className="rounded-lg bg-slate-50 border border-slate-200 p-3">
              <p className="text-sm font-medium text-slate-900">{product.name}</p>
              <p className="text-xs text-slate-500 mt-0.5">
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

// ─── Return Product dialog ──────────────────────────────────────────────────
function ReturnDialog({ open, onClose, products }: { open: boolean; onClose: () => void; products: ProductDto[] }) {
  const qc = useQueryClient();
  const [productId, setProductId] = useState("");
  const [qty, setQty] = useState("");
  const [customerName, setCustomerName] = useState("");
  const [notes, setNotes] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const selected = products.find((p) => p.id === productId);

  async function handleSave() {
    if (!productId || !qty) return;
    setSaving(true);
    setError(null);
    try {
      await api.post("/inventory/return", {
        productId,
        quantity: Number(qty),
        customerName: customerName || null,
        notes: notes || null,
      });
      qc.invalidateQueries({ queryKey: ["products"] });
      qc.invalidateQueries({ queryKey: ["low-stock"] });
      handleClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to process return");
    } finally {
      setSaving(false);
    }
  }

  function handleClose() {
    setProductId("");
    setQty("");
    setCustomerName("");
    setNotes("");
    setError(null);
    onClose();
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && handleClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Return Product</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label>Product</Label>
            <select
              className="w-full h-9 px-2 rounded-md border border-slate-200 text-sm bg-white"
              value={productId}
              onChange={(e) => setProductId(e.target.value)}
            >
              <option value="">Select product</option>
              {products.map((p) => (
                <option key={p.id} value={p.id}>{p.name} ({p.currentStock} {p.unit})</option>
              ))}
            </select>
          </div>
          <div>
            <Label>Quantity Returned</Label>
            <Input
              type="number"
              value={qty}
              onChange={(e) => setQty(e.target.value)}
              placeholder={selected ? `in ${selected.unit}` : ""}
              min={1}
            />
          </div>
          <div>
            <Label>Customer Name (optional)</Label>
            <Input
              value={customerName}
              onChange={(e) => setCustomerName(e.target.value)}
              placeholder="Who returned it?"
            />
          </div>
          <div>
            <Label>Reason (optional)</Label>
            <Input
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder="e.g. Defective, wrong size"
            />
          </div>
          {error && <p className="text-xs text-red-500">{error}</p>}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={handleClose} disabled={saving}>Cancel</Button>
          <Button onClick={handleSave} disabled={saving || !productId || !qty}>
            {saving ? "Processing..." : "Process Return"}
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
              className="w-full h-9 px-2 rounded-md border border-slate-200 text-sm bg-white"
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
type StockFilter = "all" | "low" | "sufficient" | "wastage";

export default function InventoryPage() {
  const [adding, setAdding] = useState(false);
  const [addingHold, setAddingHold] = useState(false);
  const [editing, setEditing] = useState<ProductDto | null>(null);
  const [deleting, setDeleting] = useState<ProductDto | null>(null);
  const [damaging, setDamaging] = useState<ProductDto | null>(null);
  const [removingStock, setRemovingStock] = useState<ProductDto | null>(null);
  const [returning, setReturning] = useState(false);
  const [wastaging, setWastaging] = useState(false);
  const [stockFilter, setStockFilter] = useState<StockFilter>("all");
  const [categoryFilter, setCategoryFilter] = useState<string>("");
  const { data: planStatus } = usePlanStatus();
  const hasHolds = planStatus?.hasStockHolds ?? true;

  const { data: productsData, isLoading } = useQuery({
    queryKey: ["products"],
    queryFn: async () => {
      const { data } = await api.get<{ data: PaginatedResult<ProductDto> }>(
        "/products?page=1&pageSize=100"
      );
      return data.data!;
    },
  });

  const { data: lowStock } = useQuery({
    queryKey: ["low-stock"],
    queryFn: async () => {
      const { data } = await api.get<{ data: ProductDto[] }>("/products/low-stock");
      return data.data!;
    },
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
      const { data } = await api.get<{ data: { items: LossEntry[]; totalCount: number } }>(
        "/inventory/transactions?page=1&pageSize=20"
      );
      // Filter to only Damaged and Wastage
      const filtered = (data.data?.items ?? []).filter(
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
    } catch { /* silent */ } finally {
      setHoldAction(null);
    }
  }

  // Compute filtered products
  const allProducts = useMemo(() => productsData?.items ?? [], [productsData]);
  const lowStockIds = useMemo(() => new Set((lowStock ?? []).map((p) => p.id)), [lowStock]);
  const sufficientCount = allProducts.filter((p) => !lowStockIds.has(p.id)).length;

  // Get unique categories + staff names from actual products for filter dropdowns
  const usedCategories = useMemo(() => {
    const cats = new Set<string>();
    allProducts.forEach((p) => { if (p.category) cats.add(p.category); });
    return Array.from(cats).sort();
  }, [allProducts]);

  const isWastageView = stockFilter === "wastage";
  const filteredProducts = useMemo(() => {
    if (stockFilter === "wastage") return [];
    let items = allProducts;
    if (stockFilter === "low") items = items.filter((p) => lowStockIds.has(p.id));
    if (stockFilter === "sufficient") items = items.filter((p) => !lowStockIds.has(p.id));
    if (categoryFilter) items = items.filter((p) => p.category === categoryFilter);
    return items;
  }, [allProducts, stockFilter, categoryFilter, lowStockIds]);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold text-slate-900">Inventory</h2>
          <p className="text-slate-500 text-sm mt-0.5">Current stock levels for all products</p>
        </div>
        {hasPermission(Permission.ManageStock) && (
          <div className="flex gap-2">
            <Button variant="outline" onClick={() => setWastaging(true)} className="text-orange-600 border-orange-200 hover:bg-orange-50">
              <Ban size={14} className="mr-1" /> Wastage
            </Button>
            <Button variant="outline" onClick={() => setReturning(true)}>
              <RotateCcw size={14} className="mr-1" /> Return
            </Button>
            {hasHolds && (
              <Button variant="outline" onClick={() => setAddingHold(true)}>
                <Lock size={14} className="mr-1" /> Hold Stock
              </Button>
            )}
            <Button onClick={() => setAdding(true)}>+ Add Product</Button>
          </div>
        )}
      </div>

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
              <Lock size={16} className="text-sky-500" />
              <h3 className="text-sm font-semibold text-slate-700">
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
                    <p className="text-sm font-medium text-slate-900 truncate">
                      {hold.quantity} {hold.unit} of {hold.productName}
                    </p>
                    <p className="text-xs text-slate-500">
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
                        className="flex items-center gap-1 px-2 py-1 rounded text-xs font-medium bg-slate-100 text-slate-600 hover:bg-slate-200 transition-colors"
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

      {/* Clickable summary cards */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
        <Card
          className={`cursor-pointer transition-all ${stockFilter === "all" ? "ring-2 ring-sky-500" : "hover:shadow-md"}`}
          onClick={() => setStockFilter("all")}
        >
          <CardContent className="p-4 text-center">
            <Package size={20} className="mx-auto text-slate-400 mb-1" />
            <p className="text-xl font-bold text-slate-900">{productsData?.totalCount ?? "—"}</p>
            <p className="text-xs text-slate-500">All Products</p>
          </CardContent>
        </Card>
        <Card
          className={`cursor-pointer transition-all ${stockFilter === "low" ? "ring-2 ring-amber-500" : "hover:shadow-md"}`}
          onClick={() => setStockFilter("low")}
        >
          <CardContent className="p-4 text-center">
            <AlertTriangle size={20} className="mx-auto text-amber-400 mb-1" />
            <p className="text-xl font-bold text-amber-600">{lowStock?.length ?? "—"}</p>
            <p className="text-xs text-slate-500">Low Stock</p>
          </CardContent>
        </Card>
        <Card
          className={`cursor-pointer transition-all ${stockFilter === "sufficient" ? "ring-2 ring-emerald-500" : "hover:shadow-md"}`}
          onClick={() => setStockFilter("sufficient")}
        >
          <CardContent className="p-4 text-center">
            <Package size={20} className="mx-auto text-emerald-400 mb-1" />
            <p className="text-xl font-bold text-emerald-600">{sufficientCount}</p>
            <p className="text-xs text-slate-500">Sufficient</p>
          </CardContent>
        </Card>
        <Card
          className={`cursor-pointer transition-all ${stockFilter === "wastage" ? "ring-2 ring-orange-500" : "hover:shadow-md"}`}
          onClick={() => setStockFilter("wastage")}
        >
          <CardContent className="p-4 text-center">
            <Ban size={20} className="mx-auto text-orange-400 mb-1" />
            <p className="text-xl font-bold text-orange-600">{lossesData?.length ?? "—"}</p>
            <p className="text-xs text-slate-500">Wasted / Damaged</p>
          </CardContent>
        </Card>
      </div>

      {/* Filters */}
      <div className="flex items-center gap-4 flex-wrap">
          {usedCategories.length > 0 && (
            <div className="flex items-center gap-2">
              <Label className="text-sm text-slate-500 whitespace-nowrap">Category:</Label>
              <select
                className="h-9 px-2 rounded-md border border-slate-200 text-sm"
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
          {categoryFilter && (
            <button
              onClick={() => setCategoryFilter("")}
              className="text-xs text-sky-600 hover:underline"
            >
              Clear
            </button>
          )}
        </div>

      {/* Wastage & Damaged History */}
      {(isWastageView || (!isWastageView && lossesData && lossesData.length > 0)) && (
        <Card>
          <CardContent className="pt-4">
            <div className="flex items-center gap-2 mb-3">
              <Ban size={16} className="text-orange-500" />
              <h3 className="text-sm font-semibold text-slate-700">
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
                    <p className="text-sm font-medium text-slate-900 truncate">
                      {entry.quantity} of {entry.productName}
                    </p>
                    <p className="text-xs text-slate-500">
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

      {/* Product grid */}
      {isLoading ? (
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
          {Array.from({ length: 9 }).map((_, i) => (
            <Skeleton key={i} className="h-36 rounded-xl" />
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
            />
          ))}
          {filteredProducts.length === 0 && (
            <div className="col-span-3 text-center py-12 text-slate-400">
              <Package size={32} className="mx-auto mb-2 opacity-30" />
              <p>
                {stockFilter !== "all" || categoryFilter
                  ? "No products match this filter."
                  : "No products yet. Add them by messaging BizPilot on WhatsApp."}
              </p>
            </div>
          )}
        </div>
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
      <ReturnDialog
        open={returning}
        onClose={() => setReturning(false)}
        products={allProducts}
      />
      <WastageDialog
        open={wastaging}
        onClose={() => setWastaging(false)}
        products={allProducts}
      />
    </div>
  );
}

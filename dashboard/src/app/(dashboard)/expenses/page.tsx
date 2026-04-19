"use client";

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { formatNaira, formatDateTime } from "@/lib/format";
import { useBusiness } from "@/lib/data-sync";
import type { PaginatedResult, ExpenseDto } from "@/lib/types";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
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
import { ChevronLeft, ChevronRight, Pencil, Trash2 } from "lucide-react";
import { SourceBadge } from "@/components/source-badge";
import { hasPermission, Permission } from "@/lib/permissions";

const CATEGORY_COLORS: Record<string, string> = {
  Transport: "bg-blue-100 text-blue-700",
  Utilities: "bg-yellow-100 text-yellow-700",
  Rent: "bg-purple-100 text-purple-700",
  Salary: "bg-green-100 text-green-700",
  General: "bg-slate-100 text-slate-700",
};

function getCategoryClass(category: string) {
  return CATEGORY_COLORS[category] ?? "bg-slate-100 text-slate-700";
}

const CURRENCY_SYMBOLS: Record<string, string> = { NGN: "\u20A6", GHS: "GH\u20B5", USD: "$", GBP: "\u00A3", KES: "KSh", ZAR: "R", TZS: "TSh", UGX: "USh", RWF: "RF", XAF: "FCFA", XOF: "CFA", EGP: "E\u00A3", ETB: "Br" };

export default function ExpensesPage() {
  const [page, setPage] = useState(1);
  const [adding, setAdding] = useState(false);
  const [editing, setEditing] = useState<ExpenseDto | null>(null);
  const [deleting, setDeleting] = useState<ExpenseDto | null>(null);

  const { data, isLoading } = useQuery({
    queryKey: ["expenses", page],
    queryFn: async () => {
      const { data } = await api.get<{ data: PaginatedResult<ExpenseDto> }>(
        `/expenses?page=${page}&pageSize=20`
      );
      return data.data!;
    },
  });

  const totalOnPage = data?.items.reduce((s, e) => s + e.amount, 0) ?? 0;

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold text-slate-900">Expenses</h2>
          <p className="text-slate-500 text-sm mt-0.5">All recorded business expenses</p>
        </div>
        {hasPermission(Permission.RecordExpenses) && <Button onClick={() => setAdding(true)}>+ Add Expense</Button>}
      </div>

      {data && (
        <div className="grid grid-cols-2 gap-4">
          <Card>
            <CardContent className="p-5">
              <p className="text-xs text-slate-500 uppercase tracking-wide">Total Expenses</p>
              <p className="text-2xl font-bold text-red-500 mt-1">{formatNaira(totalOnPage)}</p>
              <p className="text-xs text-slate-400">This page</p>
            </CardContent>
          </Card>
          <Card>
            <CardContent className="p-5">
              <p className="text-xs text-slate-500 uppercase tracking-wide">Total Records</p>
              <p className="text-2xl font-bold text-slate-900 mt-1">{data.totalCount}</p>
              <p className="text-xs text-slate-400">All time</p>
            </CardContent>
          </Card>
        </div>
      )}

      <Card>
        <CardContent className="pt-4">
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
                    <TableHead>Category</TableHead>
                    <TableHead>Paid To</TableHead>
                    <TableHead>Notes</TableHead>
                    <TableHead>Source</TableHead>
                    <TableHead className="text-right">Amount</TableHead>
                    <TableHead></TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data?.items.map((expense) => (
                    <TableRow key={expense.id}>
                      <TableCell className="text-xs text-slate-500">
                        {formatDateTime(expense.createdAtUtc)}
                      </TableCell>
                      <TableCell>
                        <span className={`text-xs font-medium px-2 py-0.5 rounded-full ${getCategoryClass(expense.category)}`}>
                          {expense.category}
                        </span>
                      </TableCell>
                      <TableCell className="text-sm text-slate-500">
                        {expense.paidTo ?? "—"}
                      </TableCell>
                      <TableCell className="text-sm text-slate-500 max-w-xs truncate">
                        {expense.notes ?? "—"}
                      </TableCell>
                      <TableCell>
                        <SourceBadge source={expense.source} />
                      </TableCell>
                      <TableCell className="text-right font-semibold text-red-500">
                        {formatNaira(expense.amount)}
                      </TableCell>
                      {hasPermission(Permission.RecordExpenses) && (
                        <TableCell className="text-right">
                          <div className="flex items-center justify-end gap-1">
                            <button
                              onClick={() => setEditing(expense)}
                              className="p-1 rounded hover:bg-slate-100 text-slate-500 hover:text-slate-900"
                              title="Edit"
                            >
                              <Pencil size={14} />
                            </button>
                            <button
                              onClick={() => setDeleting(expense)}
                              className="p-1 rounded hover:bg-red-50 text-slate-500 hover:text-red-600"
                              title="Delete"
                            >
                              <Trash2 size={14} />
                            </button>
                          </div>
                        </TableCell>
                      )}
                    </TableRow>
                  ))}
                </TableBody>
              </Table>

              {data && data.totalPages > 1 && (
                <div className="flex items-center justify-between mt-4 pt-4 border-t">
                  <p className="text-xs text-slate-500">
                    Page {data.page} of {data.totalPages}
                  </p>
                  <div className="flex gap-2">
                    <Button variant="outline" size="sm" onClick={() => setPage((p) => p - 1)} disabled={page === 1}>
                      <ChevronLeft size={14} />
                    </Button>
                    <Button variant="outline" size="sm" onClick={() => setPage((p) => p + 1)} disabled={page === data.totalPages}>
                      <ChevronRight size={14} />
                    </Button>
                  </div>
                </div>
              )}
            </>
          )}
        </CardContent>
      </Card>

      <AddExpenseDialog open={adding} onClose={() => setAdding(false)} />
      <EditExpenseDialog
        expense={editing}
        open={editing !== null}
        onClose={() => setEditing(null)}
      />
      <DeleteExpenseDialog
        expense={deleting}
        open={deleting !== null}
        onClose={() => setDeleting(null)}
      />
    </div>
  );
}

function AddExpenseDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const qc = useQueryClient();
  const biz = useBusiness();
  const currencySymbol = CURRENCY_SYMBOLS[biz?.currency?.toUpperCase() ?? "NGN"] ?? biz?.currency ?? "\u20A6";
  const [form, setForm] = useState({ category: "General", amount: "", paidTo: "", notes: "" });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSave() {
    setSaving(true);
    setError(null);
    try {
      await api.post(`/expenses`, {
        category: form.category || "General",
        amount: Number(form.amount),
        paidTo: form.paidTo || undefined,
        notes: form.notes || undefined,
      });
      qc.invalidateQueries({ queryKey: ["expenses"] });
      handleClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to add expense");
    } finally {
      setSaving(false);
    }
  }

  function handleClose() {
    setForm({ category: "General", amount: "", paidTo: "", notes: "" });
    setError(null);
    onClose();
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && handleClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Add Expense</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label>Category</Label>
            <Input value={form.category} onChange={(e) => setForm({ ...form, category: e.target.value })} placeholder="e.g. Transport, Fuel, Rent" />
          </div>
          <div>
            <Label>Amount ({currencySymbol})</Label>
            <Input type="number" value={form.amount} onChange={(e) => setForm({ ...form, amount: e.target.value })} placeholder="5000" />
          </div>
          <div>
            <Label>Paid To (optional)</Label>
            <Input value={form.paidTo} onChange={(e) => setForm({ ...form, paidTo: e.target.value })} placeholder="e.g. Tunde, NEPA" />
          </div>
          <div>
            <Label>Notes (optional)</Label>
            <Input value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} />
          </div>
          {error && <p className="text-xs text-red-500">{error}</p>}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={handleClose} disabled={saving}>Cancel</Button>
          <Button onClick={handleSave} disabled={saving || !form.amount}>{saving ? "Saving…" : "Add Expense"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function EditExpenseDialog({
  expense,
  open,
  onClose,
}: {
  expense: ExpenseDto | null;
  open: boolean;
  onClose: () => void;
}) {
  const qc = useQueryClient();
  const biz = useBusiness();
  const currencySymbol = CURRENCY_SYMBOLS[biz?.currency?.toUpperCase() ?? "NGN"] ?? biz?.currency ?? "\u20A6";
  const [form, setForm] = useState({ category: "", amount: "", paidTo: "", notes: "" });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (expense && form.category === "" && form.amount === "") {
    setForm({
      category: expense.category,
      amount: expense.amount.toString(),
      paidTo: expense.paidTo ?? "",
      notes: expense.notes ?? "",
    });
  }

  async function handleSave() {
    if (!expense) return;
    setSaving(true);
    setError(null);
    try {
      await api.put(`/expenses/${expense.id}`, {
        category: form.category,
        amount: form.amount ? Number(form.amount) : null,
        paidTo: form.paidTo,
        notes: form.notes,
      });
      qc.invalidateQueries({ queryKey: ["expenses"] });
      handleClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to save");
    } finally {
      setSaving(false);
    }
  }

  function handleClose() {
    setForm({ category: "", amount: "", paidTo: "", notes: "" });
    setError(null);
    onClose();
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && handleClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Edit Expense</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label>Category</Label>
            <Input value={form.category} onChange={(e) => setForm({ ...form, category: e.target.value })} />
          </div>
          <div>
            <Label>Amount ({currencySymbol})</Label>
            <Input
              type="number"
              value={form.amount}
              onChange={(e) => setForm({ ...form, amount: e.target.value })}
            />
          </div>
          <div>
            <Label>Paid To</Label>
            <Input value={form.paidTo} onChange={(e) => setForm({ ...form, paidTo: e.target.value })} />
          </div>
          <div>
            <Label>Notes</Label>
            <Input value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} />
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

function DeleteExpenseDialog({
  expense,
  open,
  onClose,
}: {
  expense: ExpenseDto | null;
  open: boolean;
  onClose: () => void;
}) {
  const qc = useQueryClient();
  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleDelete() {
    if (!expense) return;
    setDeleting(true);
    setError(null);
    try {
      await api.delete(`/expenses/${expense.id}`);
      qc.invalidateQueries({ queryKey: ["expenses"] });
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
          <DialogTitle>Delete Expense?</DialogTitle>
        </DialogHeader>
        <p className="text-sm text-slate-600">
          This will remove <strong>{expense?.category}</strong> ({formatNaira(expense?.amount ?? 0)}) from your records.
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

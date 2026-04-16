"use client";

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { formatNaira, formatDateTime } from "@/lib/format";
import type { PaginatedResult, ContactDto, LedgerEntryDto } from "@/lib/types";
import { Card, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
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
import { Users, Pencil, CreditCard, Banknote } from "lucide-react";
import { hasPermission, Permission } from "@/lib/permissions";

export default function ContactsPage() {
  const [typeFilter, setTypeFilter] = useState<string>("all");
  const [adding, setAdding] = useState(false);
  const [editing, setEditing] = useState<ContactDto | null>(null);
  const [recordingDebt, setRecordingDebt] = useState(false);
  const [recordingPayment, setRecordingPayment] = useState<ContactDto | null>(null);
  const [viewingLedger, setViewingLedger] = useState<ContactDto | null>(null);

  const { data, isLoading } = useQuery({
    queryKey: ["contacts", typeFilter],
    queryFn: async () => {
      const typeParam = typeFilter !== "all" ? `&type=${typeFilter}` : "";
      const { data } = await api.get<{ data: PaginatedResult<ContactDto> }>(
        `/contacts?page=1&pageSize=100${typeParam}`
      );
      return data.data!;
    },
  });

  const totalReceivable = data?.items.reduce((s, c) => s + c.outstandingReceivable, 0) ?? 0;
  const totalPayable = data?.items.reduce((s, c) => s + c.outstandingPayable, 0) ?? 0;
  const contacts = data?.items ?? [];

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold text-slate-900">Contacts & Ledger</h2>
          <p className="text-slate-500 text-sm mt-0.5">Customers, suppliers, and outstanding balances</p>
        </div>
        {hasPermission(Permission.ManageDebts) && (
          <div className="flex gap-2">
            <Button variant="outline" onClick={() => setRecordingDebt(true)}>
              <CreditCard size={14} className="mr-1" /> Record Debt
            </Button>
            <Button onClick={() => setAdding(true)}>+ Add Contact</Button>
          </div>
        )}
      </div>

      <div className="grid grid-cols-2 gap-4">
        <Card>
          <CardContent className="p-5">
            <p className="text-xs text-slate-500 uppercase tracking-wide">Total Receivable</p>
            <p className="text-2xl font-bold text-sky-600 mt-1">{formatNaira(totalReceivable)}</p>
            <p className="text-xs text-slate-400">Owed to you</p>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="p-5">
            <p className="text-xs text-slate-500 uppercase tracking-wide">Total Payable</p>
            <p className="text-2xl font-bold text-orange-500 mt-1">{formatNaira(totalPayable)}</p>
            <p className="text-xs text-slate-400">You owe</p>
          </CardContent>
        </Card>
      </div>

      <Tabs value={typeFilter} onValueChange={setTypeFilter}>
        <TabsList>
          <TabsTrigger value="all">All</TabsTrigger>
          <TabsTrigger value="Customer">Customers</TabsTrigger>
          <TabsTrigger value="Supplier">Suppliers</TabsTrigger>
        </TabsList>
      </Tabs>

      <Card>
        <CardContent className="pt-4">
          {isLoading ? (
            <div className="space-y-2">
              {Array.from({ length: 6 }).map((_, i) => (
                <Skeleton key={i} className="h-10" />
              ))}
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead>Type</TableHead>
                  <TableHead>Phone</TableHead>
                  <TableHead className="text-right">Receivable</TableHead>
                  <TableHead className="text-right">Payable</TableHead>
                  <TableHead></TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data?.items.map((contact) => (
                  <TableRow key={contact.id}>
                    <TableCell className="font-medium">{contact.name}</TableCell>
                    <TableCell>
                      <Badge
                        variant={contact.type === "Customer" ? "default" : "secondary"}
                        className="text-xs"
                      >
                        {contact.type}
                      </Badge>
                    </TableCell>
                    <TableCell className="text-sm text-slate-500">
                      {contact.phoneNumber ?? "—"}
                    </TableCell>
                    <TableCell className="text-right">
                      {contact.outstandingReceivable > 0 ? (
                        <button
                          onClick={() => setViewingLedger(contact)}
                          className="text-sm font-medium text-sky-600 hover:underline"
                        >
                          {formatNaira(contact.outstandingReceivable)}
                        </button>
                      ) : (
                        <span className="text-slate-300">—</span>
                      )}
                    </TableCell>
                    <TableCell className="text-right">
                      {contact.outstandingPayable > 0 ? (
                        <button
                          onClick={() => setViewingLedger(contact)}
                          className="text-sm font-medium text-orange-500 hover:underline"
                        >
                          {formatNaira(contact.outstandingPayable)}
                        </button>
                      ) : (
                        <span className="text-slate-300">—</span>
                      )}
                    </TableCell>
                    <TableCell className="text-right">
                      <div className="flex items-center justify-end gap-1">
                        {hasPermission(Permission.ManageDebts) && (contact.outstandingReceivable > 0 || contact.outstandingPayable > 0) && (
                          <button
                            onClick={() => setRecordingPayment(contact)}
                            className="p-1 rounded hover:bg-emerald-50 text-slate-500 hover:text-emerald-600"
                            title="Record payment"
                          >
                            <Banknote size={14} />
                          </button>
                        )}
                        {hasPermission(Permission.ManageDebts) && (
                          <button
                            onClick={() => setEditing(contact)}
                            className="p-1 rounded hover:bg-slate-100 text-slate-500 hover:text-slate-900"
                            title="Edit contact"
                          >
                            <Pencil size={14} />
                          </button>
                        )}
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
                {data?.items.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={6} className="text-center py-8 text-slate-400">
                      <Users size={24} className="mx-auto mb-2 opacity-30" />
                      No contacts yet
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      <AddContactDialog open={adding} onClose={() => setAdding(false)} />
      <EditContactDialog
        contact={editing}
        open={editing !== null}
        onClose={() => setEditing(null)}
      />
      <RecordDebtDialog
        open={recordingDebt}
        onClose={() => setRecordingDebt(false)}
        contacts={contacts}
      />
      <RecordPaymentDialog
        contact={recordingPayment}
        open={recordingPayment !== null}
        onClose={() => setRecordingPayment(null)}
      />
      <LedgerHistoryDialog
        contact={viewingLedger}
        open={viewingLedger !== null}
        onClose={() => setViewingLedger(null)}
      />
    </div>
  );
}

function RecordDebtDialog({ open, onClose, contacts }: { open: boolean; onClose: () => void; contacts: ContactDto[] }) {
  const qc = useQueryClient();
  const [form, setForm] = useState({ contactId: "", type: "receivable", amount: "", notes: "" });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSave() {
    if (!form.contactId || !form.amount || !form.notes) return;
    setSaving(true);
    setError(null);
    try {
      const endpoint = form.type === "receivable" ? "/ledger/receivables" : "/ledger/payables";
      await api.post(endpoint, {
        contactId: form.contactId,
        amount: Number(form.amount),
        notes: form.notes,
      });
      qc.invalidateQueries({ queryKey: ["contacts"] });
      handleClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to record debt");
    } finally {
      setSaving(false);
    }
  }

  function handleClose() {
    setForm({ contactId: "", type: "receivable", amount: "", notes: "" });
    setError(null);
    onClose();
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && handleClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Record Debt</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label>Type</Label>
            <select
              className="w-full h-9 px-2 rounded-md border border-slate-200 text-sm bg-white"
              value={form.type}
              onChange={(e) => setForm({ ...form, type: e.target.value })}
            >
              <option value="receivable">They owe me (Receivable)</option>
              <option value="payable">I owe them (Payable)</option>
            </select>
          </div>
          <div>
            <Label>Contact</Label>
            <select
              className="w-full h-9 px-2 rounded-md border border-slate-200 text-sm bg-white"
              value={form.contactId}
              onChange={(e) => setForm({ ...form, contactId: e.target.value })}
            >
              <option value="">Select contact</option>
              {contacts.map((c) => (
                <option key={c.id} value={c.id}>{c.name} ({c.type})</option>
              ))}
            </select>
          </div>
          <div>
            <Label>Amount</Label>
            <Input
              type="number"
              value={form.amount}
              onChange={(e) => setForm({ ...form, amount: e.target.value })}
              placeholder="e.g. 15000"
            />
          </div>
          <div>
            <Label>What is this for?</Label>
            <Input
              value={form.notes}
              onChange={(e) => setForm({ ...form, notes: e.target.value })}
              placeholder="e.g. 10 bags of rice on credit"
            />
          </div>
          {error && <p className="text-xs text-red-500">{error}</p>}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={handleClose} disabled={saving}>Cancel</Button>
          <Button onClick={handleSave} disabled={saving || !form.contactId || !form.amount || !form.notes}>
            {saving ? "Saving..." : "Record Debt"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function RecordPaymentDialog({ contact, open, onClose }: { contact: ContactDto | null; open: boolean; onClose: () => void }) {
  const qc = useQueryClient();
  const hasReceivable = (contact?.outstandingReceivable ?? 0) > 0;
  const hasPayable = (contact?.outstandingPayable ?? 0) > 0;
  const defaultType = hasReceivable ? "receivable" : "payable";

  const [form, setForm] = useState({ type: defaultType, amount: "", notes: "" });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [initialized, setInitialized] = useState(false);

  if (contact && open && !initialized) {
    const t = (contact.outstandingReceivable ?? 0) > 0 ? "receivable" : "payable";
    setForm({ type: t, amount: "", notes: "" });
    setInitialized(true);
  }

  const outstanding = form.type === "receivable" ? contact?.outstandingReceivable ?? 0 : contact?.outstandingPayable ?? 0;

  async function handleSave() {
    if (!contact || !form.amount) return;
    setSaving(true);
    setError(null);
    try {
      await api.post("/ledger/payments", {
        contactId: contact.id,
        amount: Number(form.amount),
        paymentType: form.type,
        notes: form.notes || null,
      });
      qc.invalidateQueries({ queryKey: ["contacts"] });
      handleClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to record payment");
    } finally {
      setSaving(false);
    }
  }

  function handleClose() {
    setForm({ type: defaultType, amount: "", notes: "" });
    setError(null);
    setInitialized(false);
    onClose();
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && handleClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Record Payment — {contact?.name}</DialogTitle>
        </DialogHeader>
        {contact && (
          <div className="space-y-3">
            {hasReceivable && hasPayable && (
              <div>
                <Label>Payment Type</Label>
                <select
                  className="w-full h-9 px-2 rounded-md border border-slate-200 text-sm bg-white"
                  value={form.type}
                  onChange={(e) => setForm({ ...form, type: e.target.value })}
                >
                  <option value="receivable">They paid me ({formatNaira(contact.outstandingReceivable)} owed)</option>
                  <option value="payable">I paid them ({formatNaira(contact.outstandingPayable)} owed)</option>
                </select>
              </div>
            )}
            {!hasReceivable && hasPayable && (
              <div className="rounded-lg bg-orange-50 border border-orange-200 p-3">
                <p className="text-sm text-orange-800">You owe {contact.name} <strong>{formatNaira(contact.outstandingPayable)}</strong></p>
              </div>
            )}
            {hasReceivable && !hasPayable && (
              <div className="rounded-lg bg-sky-50 border border-sky-200 p-3">
                <p className="text-sm text-sky-800">{contact.name} owes you <strong>{formatNaira(contact.outstandingReceivable)}</strong></p>
              </div>
            )}
            <div>
              <Label>Amount</Label>
              <Input
                type="number"
                value={form.amount}
                onChange={(e) => setForm({ ...form, amount: e.target.value })}
                placeholder={`Max ${formatNaira(outstanding)}`}
              />
              <button
                type="button"
                onClick={() => setForm({ ...form, amount: outstanding.toString() })}
                className="text-xs text-sky-600 hover:underline mt-1"
              >
                Pay full balance ({formatNaira(outstanding)})
              </button>
            </div>
            <div>
              <Label>Note (optional)</Label>
              <Input
                value={form.notes}
                onChange={(e) => setForm({ ...form, notes: e.target.value })}
                placeholder="e.g. Cash payment for rice delivery"
              />
            </div>
            {error && <p className="text-xs text-red-500">{error}</p>}
          </div>
        )}
        <DialogFooter>
          <Button variant="outline" onClick={handleClose} disabled={saving}>Cancel</Button>
          <Button onClick={handleSave} disabled={saving || !form.amount} className="bg-emerald-600 hover:bg-emerald-700 text-white">
            {saving ? "Saving..." : "Record Payment"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function LedgerHistoryDialog({ contact, open, onClose }: { contact: ContactDto | null; open: boolean; onClose: () => void }) {
  const { data: entries, isLoading } = useQuery({
    queryKey: ["contact-ledger", contact?.id],
    queryFn: async () => {
      const { data } = await api.get<{ data: LedgerEntryDto[] }>(`/contacts/${contact!.id}/ledger`);
      return data.data!;
    },
    enabled: open && !!contact,
  });

  const typeLabel = (t: string) => {
    switch (t) {
      case "Receivable": return "Debt owed to you";
      case "ReceivablePayment": return "Payment received";
      case "Payable": return "You owe";
      case "PayablePayment": return "You paid";
      default: return t;
    }
  };

  const typeColor = (t: string) => {
    if (t === "Receivable") return "text-sky-600";
    if (t === "Payable") return "text-orange-500";
    if (t.includes("Payment")) return "text-emerald-600";
    return "text-slate-600";
  };

  return (
    <Dialog open={open} onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>Ledger — {contact?.name}</DialogTitle>
        </DialogHeader>
        <div className="max-h-[400px] overflow-y-auto space-y-2">
          {isLoading ? (
            <div className="space-y-2">{Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-16" />)}</div>
          ) : entries && entries.length > 0 ? (
            entries.map((e) => (
              <div key={e.id} className="border rounded-lg px-3 py-2">
                <div className="flex items-center justify-between">
                  <span className={`text-sm font-medium ${typeColor(e.entryType)}`}>
                    {typeLabel(e.entryType)}
                  </span>
                  <span className={`text-sm font-semibold ${typeColor(e.entryType)}`}>
                    {e.entryType.includes("Payment") ? "-" : "+"}{formatNaira(e.amount)}
                  </span>
                </div>
                {e.notes && (
                  <p className="text-xs text-slate-600 mt-1">{e.notes}</p>
                )}
                <div className="flex items-center gap-2 mt-1">
                  <span className="text-xs text-slate-400">{formatDateTime(e.createdAtUtc)}</span>
                  <Badge variant="secondary" className="text-[10px] px-1.5 py-0">{e.source}</Badge>
                </div>
              </div>
            ))
          ) : (
            <p className="text-sm text-slate-400 text-center py-6">No ledger entries for this contact.</p>
          )}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Close</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function EditContactDialog({
  contact,
  open,
  onClose,
}: {
  contact: ContactDto | null;
  open: boolean;
  onClose: () => void;
}) {
  const qc = useQueryClient();
  const [form, setForm] = useState({ name: "", phoneNumber: "", type: "Customer" as "Customer" | "Supplier" | "Both" });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (contact && form.name === "" && !error) {
    setForm({
      name: contact.name,
      phoneNumber: contact.phoneNumber ?? "",
      type: (contact.type as "Customer" | "Supplier" | "Both") ?? "Customer",
    });
  }

  async function handleSave() {
    if (!contact) return;
    setSaving(true);
    setError(null);
    try {
      await api.put(`/contacts/${contact.id}`, {
        name: form.name,
        phoneNumber: form.phoneNumber || null,
        type: form.type,
      });
      qc.invalidateQueries({ queryKey: ["contacts"] });
      handleClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to save");
    } finally {
      setSaving(false);
    }
  }

  function handleClose() {
    setForm({ name: "", phoneNumber: "", type: "Customer" });
    setError(null);
    onClose();
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && handleClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Edit Contact</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label>Name</Label>
            <Input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
          </div>
          <div>
            <Label>Phone Number</Label>
            <Input value={form.phoneNumber} onChange={(e) => setForm({ ...form, phoneNumber: e.target.value })} />
          </div>
          <div>
            <Label>Type</Label>
            <select
              className="w-full h-9 px-2 rounded-md border border-slate-200 text-sm"
              value={form.type}
              onChange={(e) => setForm({ ...form, type: e.target.value as "Customer" | "Supplier" | "Both" })}
            >
              <option value="Customer">Customer</option>
              <option value="Supplier">Supplier</option>
              <option value="Both">Both</option>
            </select>
          </div>
          {error && <p className="text-xs text-red-500">{error}</p>}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={handleClose} disabled={saving}>Cancel</Button>
          <Button onClick={handleSave} disabled={saving || !form.name}>{saving ? "Saving…" : "Save Changes"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function AddContactDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const qc = useQueryClient();
  const [form, setForm] = useState({ name: "", phoneNumber: "", type: "Customer" as "Customer" | "Supplier" | "Both" });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSave() {
    setSaving(true);
    setError(null);
    try {
      await api.post(`/contacts`, {
        name: form.name,
        phoneNumber: form.phoneNumber || undefined,
        type: form.type,
      });
      qc.invalidateQueries({ queryKey: ["contacts"] });
      handleClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to add contact");
    } finally {
      setSaving(false);
    }
  }

  function handleClose() {
    setForm({ name: "", phoneNumber: "", type: "Customer" });
    setError(null);
    onClose();
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && handleClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Add Contact</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label>Name</Label>
            <Input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="e.g. Ada Okafor" />
          </div>
          <div>
            <Label>Phone Number (optional)</Label>
            <Input value={form.phoneNumber} onChange={(e) => setForm({ ...form, phoneNumber: e.target.value })} placeholder="+234..." />
          </div>
          <div>
            <Label>Type</Label>
            <select
              className="w-full h-9 px-2 rounded-md border border-slate-200 text-sm"
              value={form.type}
              onChange={(e) => setForm({ ...form, type: e.target.value as "Customer" | "Supplier" | "Both" })}
            >
              <option value="Customer">Customer</option>
              <option value="Supplier">Supplier</option>
              <option value="Both">Both</option>
            </select>
          </div>
          {error && <p className="text-xs text-red-500">{error}</p>}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={handleClose} disabled={saving}>Cancel</Button>
          <Button onClick={handleSave} disabled={saving || !form.name}>{saving ? "Saving…" : "Add Contact"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

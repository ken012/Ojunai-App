"use client";

import { useState, useEffect, useMemo } from "react";
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
import { Users, Pencil, CreditCard, Banknote, Search, X } from "lucide-react";
import { hasPermission, Permission } from "@/lib/permissions";

export default function ContactsPage() {
  const [typeFilter, setTypeFilter] = useState<string>("all");
  const [balanceFilter, setBalanceFilter] = useState<string>("all");
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [adding, setAdding] = useState(false);
  const [editing, setEditing] = useState<ContactDto | null>(null);
  const [recordingDebt, setRecordingDebt] = useState(false);
  const [recordingPayment, setRecordingPayment] = useState<ContactDto | null>(null);
  const [viewingLedger, setViewingLedger] = useState<ContactDto | null>(null);

  // Debounce the search input so we don't fire a request on every keystroke. 250ms feels
  // responsive without hammering the API.
  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search.trim()), 250);
    return () => clearTimeout(t);
  }, [search]);

  const { data, isLoading } = useQuery({
    queryKey: ["contacts", typeFilter, debouncedSearch],
    queryFn: async () => {
      const typeParam = typeFilter !== "all" ? `&type=${typeFilter}` : "";
      const searchParam = debouncedSearch ? `&search=${encodeURIComponent(debouncedSearch)}` : "";
      const { data } = await api.get<{ data: PaginatedResult<ContactDto> }>(
        `/contacts?page=1&pageSize=100${typeParam}${searchParam}`
      );
      return data.data!;
    },
  });

  // Client-side balance filter — applied after the server returns contacts with their balances.
  // Keeps totals reflecting the full dataset while the table shows the filtered subset.
  const allContacts = useMemo(() => data?.items ?? [], [data?.items]);
  const filteredContacts = useMemo(() => {
    if (balanceFilter === "all") return allContacts;
    if (balanceFilter === "receivable") return allContacts.filter(c => c.outstandingReceivable > 0);
    if (balanceFilter === "payable") return allContacts.filter(c => c.outstandingPayable > 0);
    if (balanceFilter === "settled") return allContacts.filter(c => c.outstandingReceivable === 0 && c.outstandingPayable === 0);
    return allContacts;
  }, [allContacts, balanceFilter]);

  const totalReceivable = allContacts.reduce((s, c) => s + c.outstandingReceivable, 0);
  const totalPayable = allContacts.reduce((s, c) => s + c.outstandingPayable, 0);
  const contacts = filteredContacts;

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

      <div className="flex flex-col sm:flex-row gap-3 sm:items-center sm:justify-between">
        <div className="flex flex-wrap gap-2">
          <Tabs value={typeFilter} onValueChange={setTypeFilter}>
            <TabsList>
              <TabsTrigger value="all">All</TabsTrigger>
              <TabsTrigger value="Customer">Customers</TabsTrigger>
              <TabsTrigger value="Supplier">Suppliers</TabsTrigger>
            </TabsList>
          </Tabs>

          <Tabs value={balanceFilter} onValueChange={setBalanceFilter}>
            <TabsList>
              <TabsTrigger value="all">All Balances</TabsTrigger>
              <TabsTrigger value="receivable">Owes You</TabsTrigger>
              <TabsTrigger value="payable">You Owe</TabsTrigger>
              <TabsTrigger value="settled">Settled</TabsTrigger>
            </TabsList>
          </Tabs>
        </div>

        <div className="relative w-full sm:max-w-xs">
          <Search size={14} className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400 pointer-events-none" />
          <Input
            type="search"
            placeholder="Search by name..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="pl-9 pr-9"
          />
          {search && (
            <button
              onClick={() => setSearch("")}
              className="absolute right-2 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-700 p-1 rounded"
              aria-label="Clear search"
              type="button"
            >
              <X size={14} />
            </button>
          )}
        </div>
      </div>

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
                {contacts.map((contact) => (
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
                {contacts.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={6} className="text-center py-8 text-slate-400">
                      <Users size={24} className="mx-auto mb-2 opacity-30" />
                      {debouncedSearch
                        ? <>No contacts match &ldquo;{debouncedSearch}&rdquo;.</>
                        : balanceFilter === "receivable" ? "No contacts with outstanding receivables."
                        : balanceFilter === "payable" ? "No contacts with outstanding payables."
                        : balanceFilter === "settled" ? "No fully settled contacts."
                        : "No contacts yet"}
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
  const [contactSearch, setContactSearch] = useState("");

  // Client-side filter over the already-loaded contacts. We don't re-fetch because the contacts prop
  // already reflects the page's active tab filter — no need to double-round-trip for a sub-selection.
  const filteredContacts = useMemo(() => {
    const q = contactSearch.trim().toLowerCase();
    if (!q) return contacts;
    return contacts.filter((c) => c.name.toLowerCase().includes(q));
  }, [contacts, contactSearch]);

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
    setContactSearch("");
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
            <div className="relative mb-1">
              <Search size={12} className="absolute left-2.5 top-1/2 -translate-y-1/2 text-slate-400 pointer-events-none" />
              <Input
                type="search"
                placeholder="Filter contacts..."
                value={contactSearch}
                onChange={(e) => setContactSearch(e.target.value)}
                className="pl-7 h-8 text-xs"
              />
            </div>
            <select
              className="w-full h-9 px-2 rounded-md border border-slate-200 text-sm bg-white"
              value={form.contactId}
              onChange={(e) => setForm({ ...form, contactId: e.target.value })}
              size={Math.min(6, Math.max(2, filteredContacts.length + 1))}
            >
              <option value="">Select contact</option>
              {filteredContacts.map((c) => (
                <option key={c.id} value={c.id}>{c.name} ({c.type})</option>
              ))}
            </select>
            {contactSearch && filteredContacts.length === 0 && (
              <p className="text-xs text-slate-400 mt-1">No contacts match &ldquo;{contactSearch}&rdquo;.</p>
            )}
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

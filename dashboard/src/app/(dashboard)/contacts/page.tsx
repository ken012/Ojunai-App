"use client";

export const dynamic = "force-dynamic";

import { useState, useEffect, useMemo, useRef } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api, fetchAllPaged } from "@/lib/api";
import { formatNaira, formatDateTime } from "@/lib/format";
import type { ContactDto, LedgerEntryDto } from "@/lib/types";
import { Card, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useToast } from "@/components/toast";
import { Skeleton } from "@/components/ui/skeleton";
import { PageHeader } from "@/components/page-header";
import { EmptyState } from "@/components/empty-state";
import { Avatar } from "@/components/avatar";
import { Drawer, DrawerHeader, DrawerBody, DrawerFooter } from "@/components/ui/drawer";
// Tabs removed — using plain buttons to avoid base-ui context conflicts between two filter groups
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
import { Users, Pencil, CreditCard, Banknote, Search, X, Trash2, MessageCircle } from "lucide-react";
import { hasPermission, Permission } from "@/lib/permissions";
import { useBusiness } from "@/lib/data-sync";

export default function ContactsPage() {
  const [typeFilter, setTypeFilter] = useState<string>("all");
  const [balanceFilter, setBalanceFilter] = useState<string>("bal-all");
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [adding, setAdding] = useState(false);
  const [editing, setEditing] = useState<ContactDto | null>(null);
  const [recordingDebt, setRecordingDebt] = useState(false);
  const [recordingPayment, setRecordingPayment] = useState<ContactDto | null>(null);
  const [viewingLedger, setViewingLedger] = useState<ContactDto | null>(null);
  const [deleting, setDeleting] = useState<ContactDto | null>(null);

  // Debounce the search input so we don't fire a request on every keystroke. 250ms feels
  // responsive without hammering the API.
  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search.trim()), 250);
    return () => clearTimeout(t);
  }, [search]);

  // Auto-open create dialog from ?new=1 (dashboard quick action)
  useEffect(() => {
    if (new URLSearchParams(window.location.search).get("new") === "1") {
      setAdding(true);
    }
  }, []);

  // Fetch the COMPLETE contact list by paging through the API until exhausted, then
  // run search + filters fully client-side. Without this, large contact lists got
  // silently truncated and "Record Debt" / "Owes You" only saw the first batch.
  const { data, isLoading } = useQuery({
    queryKey: ["contacts"],
    queryFn: () => fetchAllPaged<ContactDto>((p, ps) => `/contacts?page=${p}&pageSize=${ps}`),
    staleTime: 30_000,
  });

  const allItems = useMemo(() => data ?? [], [data]);

  // Search runs client-side over the full list so it works with the balance/type filters
  // simultaneously and the Record Debt picker always sees every contact.
  const searchedItems = useMemo(() => {
    if (!debouncedSearch) return allItems;
    const q = debouncedSearch.toLowerCase();
    return allItems.filter((c) =>
      c.name.toLowerCase().includes(q) ||
      (c.phoneNumber ? c.phoneNumber.includes(debouncedSearch) : false)
    );
  }, [allItems, debouncedSearch]);

  // Deep-link: ?id=<contactId> auto-opens that contact's drawer once contacts are loaded.
  // Used by Today screen "Remind" CTA and the Sales list outstanding-customer link.
  // Consumed once via ref + URL is rewritten so closing the drawer doesn't re-trigger
  // (which would otherwise cause an infinite reopen loop).
  const deepLinkConsumed = useRef(false);
  useEffect(() => {
    if (deepLinkConsumed.current || !allItems.length) return;
    const id = new URLSearchParams(window.location.search).get("id");
    if (!id) return;
    const target = allItems.find((c) => c.id === id);
    if (target) {
      setViewingLedger(target);
      deepLinkConsumed.current = true;
      // Strip ?id from URL so back/forward + drawer-close behave as expected.
      window.history.replaceState(null, "", "/contacts");
    }
  }, [allItems]);

  const filteredContacts = useMemo(() => {
    let result = searchedItems;

    if (typeFilter !== "all")
      result = result.filter(c => c.type === typeFilter);

    // A negative payable behaves like a receivable (and vice versa) — surface those
    // contacts under the side that matches the user's intent.
    if (balanceFilter === "bal-receivable")
      result = result.filter(c => c.outstandingReceivable > 0 || c.outstandingPayable < 0);
    else if (balanceFilter === "bal-payable")
      result = result.filter(c => c.outstandingPayable > 0 || c.outstandingReceivable < 0);
    else if (balanceFilter === "bal-settled")
      result = result.filter(c => c.outstandingReceivable === 0 && c.outstandingPayable === 0);

    return result;
  }, [searchedItems, typeFilter, balanceFilter]);

  // Totals always reflect the full unfiltered set so the headline numbers don't jump when filtering.
  const totalReceivable = allItems.reduce((s, c) => s + c.outstandingReceivable, 0);
  const totalPayable = allItems.reduce((s, c) => s + c.outstandingPayable, 0);
  const contacts = filteredContacts;

  return (
    <div className="space-y-6">
      <PageHeader
        title="Contacts"
        subtitle="Customers, suppliers, and outstanding balances"
        actions={
          hasPermission(Permission.ManageDebts) ? (
            <>
              <Button variant="outline" onClick={() => setRecordingDebt(true)}>
                <CreditCard size={14} className="mr-1" /> Record Debt
              </Button>
              <Button onClick={() => setAdding(true)}>+ Add Contact</Button>
            </>
          ) : null
        }
      />

      <div className="grid grid-cols-2 gap-4">
        <Card>
          <CardContent className="p-5">
            <p className="text-xs text-slate-500 dark:text-slate-400 uppercase tracking-wide">Total Receivable</p>
            <p className="text-2xl font-bold text-cyan-600 mt-1">{formatNaira(totalReceivable)}</p>
            <p className="text-xs text-slate-400 dark:text-slate-500">Owed to you</p>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="p-5">
            <p className="text-xs text-slate-500 dark:text-slate-400 uppercase tracking-wide">Total Payable</p>
            <p className="text-2xl font-bold text-orange-500 mt-1">{formatNaira(totalPayable)}</p>
            <p className="text-xs text-slate-400 dark:text-slate-500">You owe</p>
          </CardContent>
        </Card>
      </div>

      {/* Active filter badges */}
      {(typeFilter !== "all" || balanceFilter !== "bal-all") && (
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-xs text-slate-400 dark:text-slate-500">Filtered by:</span>
          {typeFilter !== "all" && (
            <Badge variant="secondary" className="text-xs gap-1">
              {typeFilter === "Customer" ? "Customers" : "Suppliers"}
              <button onClick={() => setTypeFilter("all")} className="ml-1 hover:text-red-500"><X size={10} /></button>
            </Badge>
          )}
          {balanceFilter !== "bal-all" && (
            <Badge variant="secondary" className="text-xs gap-1">
              {balanceFilter === "bal-receivable" ? "Owes You" : balanceFilter === "bal-payable" ? "You Owe" : "Settled"}
              <button onClick={() => setBalanceFilter("bal-all")} className="ml-1 hover:text-red-500"><X size={10} /></button>
            </Badge>
          )}
          <button onClick={() => { setTypeFilter("all"); setBalanceFilter("bal-all"); }} className="text-xs text-slate-400 dark:text-slate-500 hover:text-slate-700 dark:hover:text-slate-300 underline">
            Clear all
          </button>
        </div>
      )}

      <div className="flex flex-col sm:flex-row gap-3 sm:items-center sm:justify-between">
        <div className="flex flex-wrap gap-1.5">
          {/* Type filter — plain buttons, no Tabs context */}
          {[
            { value: "all", label: "All" },
            { value: "Customer", label: "Customers" },
            { value: "Supplier", label: "Suppliers" },
          ].map((opt) => (
            <button
              key={opt.value}
              onClick={() => setTypeFilter(opt.value)}
              className={`px-3 py-1.5 text-xs font-medium rounded-md border transition-colors ${
                typeFilter === opt.value
                  ? "bg-cyan-500 text-white border-cyan-500 shadow-sm"
                  : "bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-400 border-slate-200 dark:border-slate-800 hover:bg-slate-50 dark:hover:bg-slate-800 hover:text-slate-900 dark:hover:text-slate-50"
              }`}
            >
              {opt.label}
            </button>
          ))}

          <span className="w-px h-6 bg-slate-200 dark:bg-slate-700 mx-1 self-center" />

          {/* Balance filter — plain buttons, no Tabs context */}
          {[
            { value: "bal-all", label: "All Balances" },
            { value: "bal-receivable", label: "Owes You" },
            { value: "bal-payable", label: "You Owe" },
            { value: "bal-settled", label: "Settled" },
          ].map((opt) => (
            <button
              key={opt.value}
              onClick={() => setBalanceFilter(opt.value)}
              className={`px-3 py-1.5 text-xs font-medium rounded-md border transition-colors ${
                balanceFilter === opt.value
                  ? "bg-cyan-500 text-white border-cyan-500 shadow-sm"
                  : "bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-400 border-slate-200 dark:border-slate-800 hover:bg-slate-50 dark:hover:bg-slate-800 hover:text-slate-900 dark:hover:text-slate-50"
              }`}
            >
              {opt.label}
            </button>
          ))}
        </div>

        <div className="relative w-full sm:max-w-xs">
          <Search size={14} className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400 dark:text-slate-500 pointer-events-none" />
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
              className="absolute right-2 top-1/2 -translate-y-1/2 text-slate-400 dark:text-slate-500 hover:text-slate-700 dark:hover:text-slate-300 p-1 rounded"
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
                    <TableCell>
                      <div className="flex items-center gap-3">
                        <Avatar name={contact.name} size="sm" />
                        <span className="font-medium text-slate-900 dark:text-slate-50">{contact.name}</span>
                      </div>
                    </TableCell>
                    <TableCell>
                      <Badge
                        className={
                          contact.type === "Customer"
                            ? "bg-cyan-50 text-cyan-700 ring-1 ring-inset ring-cyan-200"
                            : "bg-violet-50 text-violet-700 ring-1 ring-inset ring-violet-200"
                        }
                      >
                        {contact.type}
                      </Badge>
                    </TableCell>
                    <TableCell className="text-sm text-slate-500 dark:text-slate-400 tabular-nums">
                      {contact.phoneNumber ?? "—"}
                    </TableCell>
                    <TableCell className="text-right">
                      {contact.outstandingReceivable > 0 ? (
                        <button
                          onClick={() => setViewingLedger(contact)}
                          className="text-sm font-semibold text-cyan-600 hover:underline tabular-nums"
                        >
                          {formatNaira(contact.outstandingReceivable)}
                        </button>
                      ) : contact.outstandingPayable < 0 ? (
                        // Negative payable = you've paid them more than you owed → they owe you back.
                        <button
                          onClick={() => setViewingLedger(contact)}
                          className="text-xs font-medium text-cyan-500 italic hover:underline tabular-nums"
                          title="Credit balance — you paid more than you owed"
                        >
                          {formatNaira(Math.abs(contact.outstandingPayable))} credit
                        </button>
                      ) : (
                        <span className="text-slate-300 dark:text-slate-600">—</span>
                      )}
                    </TableCell>
                    <TableCell className="text-right">
                      {contact.outstandingPayable > 0 ? (
                        <button
                          onClick={() => setViewingLedger(contact)}
                          className="text-sm font-semibold text-orange-600 hover:underline tabular-nums"
                        >
                          {formatNaira(contact.outstandingPayable)}
                        </button>
                      ) : contact.outstandingReceivable < 0 ? (
                        // Negative receivable = they've paid you more than they owed → you owe them back.
                        <button
                          onClick={() => setViewingLedger(contact)}
                          className="text-xs font-medium text-orange-500 italic hover:underline tabular-nums"
                          title="Credit balance — they paid more than they owed"
                        >
                          {formatNaira(Math.abs(contact.outstandingReceivable))} credit
                        </button>
                      ) : (
                        <span className="text-slate-300 dark:text-slate-600">—</span>
                      )}
                    </TableCell>
                    <TableCell className="text-right">
                      <div className="flex items-center justify-end gap-1">
                        {hasPermission(Permission.ManageDebts) && (contact.outstandingReceivable > 0 || contact.outstandingPayable > 0) && (
                          <button
                            onClick={() => setRecordingPayment(contact)}
                            className="p-1 rounded hover:bg-emerald-50 text-slate-500 dark:text-slate-400 hover:text-emerald-600"
                            title="Record payment"
                          >
                            <Banknote size={14} />
                          </button>
                        )}
                        {hasPermission(Permission.ManageDebts) && (
                          <button
                            onClick={() => setEditing(contact)}
                            className="p-1 rounded hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-50"
                            title="Edit contact"
                          >
                            <Pencil size={14} />
                          </button>
                        )}
                        {hasPermission(Permission.ManageDebts) && (
                          <button
                            onClick={() => setDeleting(contact)}
                            className="p-1 rounded hover:bg-red-50 text-slate-400 dark:text-slate-500 hover:text-red-500"
                            title="Delete contact"
                          >
                            <Trash2 size={14} />
                          </button>
                        )}
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
                {contacts.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={6} className="p-0">
                      <EmptyState
                        icon={<Users size={22} />}
                        title={
                          debouncedSearch
                            ? `No contacts match "${debouncedSearch}"`
                            : balanceFilter === "bal-receivable" ? "No contacts with outstanding receivables"
                            : balanceFilter === "bal-payable" ? "No contacts with outstanding payables"
                            : balanceFilter === "bal-settled" ? "No fully settled contacts"
                            : "No contacts yet"
                        }
                        description={
                          !debouncedSearch && balanceFilter === "bal-all" && typeFilter === "all"
                            ? "Add your first customer or supplier to track receivables and payables."
                            : undefined
                        }
                        action={
                          !debouncedSearch && balanceFilter === "bal-all" && typeFilter === "all" && hasPermission(Permission.ManageDebts) ? (
                            <Button onClick={() => setAdding(true)}>+ Add Contact</Button>
                          ) : undefined
                        }
                      />
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
        contacts={allItems}
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
      <DeleteContactDialog
        contact={deleting}
        open={deleting !== null}
        onClose={() => setDeleting(null)}
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
              className="w-full h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 text-sm bg-white dark:bg-slate-900"
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
              <Search size={12} className="absolute left-2.5 top-1/2 -translate-y-1/2 text-slate-400 dark:text-slate-500 pointer-events-none" />
              <Input
                type="search"
                placeholder="Filter contacts..."
                value={contactSearch}
                onChange={(e) => setContactSearch(e.target.value)}
                className="pl-7 h-8 text-xs"
              />
            </div>
            <select
              className="w-full h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 text-sm bg-white dark:bg-slate-900"
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
              <p className="text-xs text-slate-400 dark:text-slate-500 mt-1">No contacts match &ldquo;{contactSearch}&rdquo;.</p>
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
                  className="w-full h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 text-sm bg-white dark:bg-slate-900"
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
              <div className="rounded-lg bg-cyan-50 border border-cyan-200 p-3">
                <p className="text-sm text-cyan-800">{contact.name} owes you <strong>{formatNaira(contact.outstandingReceivable)}</strong></p>
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
                className="text-xs text-cyan-600 hover:underline mt-1"
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
  const qc = useQueryClient();
  const business = useBusiness();
  const { toast } = useToast();
  const [editingEntry, setEditingEntry] = useState<LedgerEntryDto | null>(null);
  const [editAmount, setEditAmount] = useState("");
  const [editNotes, setEditNotes] = useState("");
  const [saving, setSaving] = useState(false);

  // WhatsApp reminder: pre-fill a polite, currency-aware nudge.
  function whatsappReminderUrl() {
    if (!contact?.phoneNumber) return null;
    const phone = contact.phoneNumber.replace(/[^\d]/g, "");
    if (!phone) return null;
    const amt = formatNaira(contact.outstandingReceivable);
    const bizName = business?.name ?? "us";
    const msg = `Hello ${contact.name}, this is a friendly reminder that you have an outstanding balance of ${amt} with ${bizName}. Could you please settle when convenient? Thank you.`;
    return `https://wa.me/${phone}?text=${encodeURIComponent(msg)}`;
  }

  const { data: entries, isLoading } = useQuery({
    queryKey: ["contact-ledger", contact?.id],
    queryFn: async () => {
      const { data } = await api.get<{ data: LedgerEntryDto[] }>(`/contacts/${contact!.id}/ledger`);
      return data.data!;
    },
    enabled: open && !!contact,
  });

  function startEdit(entry: LedgerEntryDto) {
    setEditingEntry(entry);
    // Pre-fill with the CURRENT outstanding balance (not the original entry's amount)
    // so the user sees the real current state and types the new target.
    const currentBalance = entry.entryType.includes("Receivable") || entry.entryType === "Receivable"
      ? (contact?.outstandingReceivable ?? entry.amount)
      : (contact?.outstandingPayable ?? entry.amount);
    setEditAmount(currentBalance.toString());
    setEditNotes(entry.notes ?? "");
  }

  async function saveEdit() {
    if (!editingEntry) return;
    setSaving(true);
    try {
      await api.put(`/ledger/entries/${editingEntry.id}`, {
        amount: Number(editAmount),
        notes: editNotes || null,
      });
      qc.invalidateQueries({ queryKey: ["contact-ledger", contact?.id] });
      qc.invalidateQueries({ queryKey: ["contacts"] });
      setEditingEntry(null);
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      toast.error("Couldn't save the entry", ax.response?.data?.errors?.[0] ?? "Please try again.");
    } finally { setSaving(false); }
  }

  async function deleteEntry(entryId: string) {
    if (!confirm("Delete this ledger entry? This cannot be undone.")) return;
    try {
      await api.delete(`/ledger/entries/${entryId}`);
      qc.invalidateQueries({ queryKey: ["contact-ledger", contact?.id] });
      qc.invalidateQueries({ queryKey: ["contacts"] });
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      toast.error("Couldn't delete the entry", ax.response?.data?.errors?.[0] ?? "Please try again.");
    }
  }

  const typeLabel = (t: string, source?: string) => {
    if (source === "Adjustment") return "Debt adjusted";
    switch (t) {
      case "Receivable": return "Debt owed to you";
      case "ReceivablePayment": return "Payment received";
      case "Payable": return "You owe";
      case "PayablePayment": return "You paid";
      default: return t;
    }
  };

  const typeColor = (t: string, source?: string) => {
    if (source === "Adjustment") return "text-amber-600";
    if (t === "Receivable") return "text-cyan-600";
    if (t === "Payable") return "text-orange-500";
    if (t.includes("Payment")) return "text-emerald-600";
    return "text-slate-600 dark:text-slate-400";
  };

  const canManage = hasPermission(Permission.ManageDebts);

  const handleClose = () => { setEditingEntry(null); onClose(); };
  return (
    <Drawer open={open} onClose={handleClose} width="md">
      {contact && (
        <>
          <DrawerHeader
            title={contact.name}
            subtitle={contact.type + (contact.phoneNumber ? ` · ${contact.phoneNumber}` : "")}
            onClose={handleClose}
            actions={<Avatar name={contact.name} size="md" />}
          />
          <DrawerBody>
            {(contact.outstandingReceivable > 0 || contact.outstandingPayable > 0) && (
              <div className="grid grid-cols-2 gap-3 mb-3">
                {contact.outstandingReceivable > 0 && (
                  <div className="rounded-lg bg-cyan-50 dark:bg-cyan-950/30 border border-cyan-200 dark:border-cyan-900 px-4 py-3">
                    <p className="text-[11px] font-semibold text-cyan-600 dark:text-cyan-400 uppercase tracking-wider">They owe you</p>
                    <p className="text-xl font-bold text-cyan-700 dark:text-cyan-300 mt-1 tabular-nums">{formatNaira(contact.outstandingReceivable)}</p>
                  </div>
                )}
                {contact.outstandingPayable > 0 && (
                  <div className="rounded-lg bg-orange-50 dark:bg-orange-950/30 border border-orange-200 dark:border-orange-900 px-4 py-3">
                    <p className="text-[11px] font-semibold text-orange-600 dark:text-orange-400 uppercase tracking-wider">You owe them</p>
                    <p className="text-xl font-bold text-orange-700 dark:text-orange-300 mt-1 tabular-nums">{formatNaira(contact.outstandingPayable)}</p>
                  </div>
                )}
              </div>
            )}
            {/* WhatsApp reminder — only when there's a receivable AND a phone */}
            {contact.outstandingReceivable > 0 && (() => {
              const url = whatsappReminderUrl();
              return url ? (
                <a
                  href={url}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="mb-5 flex items-center justify-center gap-2 px-4 py-2.5 rounded-lg bg-emerald-600 hover:bg-emerald-700 text-white text-sm font-semibold transition-colors"
                >
                  <MessageCircle size={16} />
                  Send WhatsApp reminder
                </a>
              ) : (
                <div className="mb-5 px-4 py-2.5 rounded-lg bg-slate-50 dark:bg-slate-800 text-xs text-slate-500 dark:text-slate-400 text-center">
                  Add a phone number to enable WhatsApp reminders
                </div>
              );
            })()}
            {contact.outstandingReceivable === 0 && contact.outstandingPayable === 0 && (
              <div className="rounded-lg bg-emerald-50 border border-emerald-200 px-4 py-3 mb-5">
                <p className="text-sm font-medium text-emerald-700">Fully settled — no outstanding balance</p>
              </div>
            )}

            <div className="space-y-2">
              <p className="text-[11px] font-semibold text-slate-500 dark:text-slate-400 uppercase tracking-wider mb-2">Activity</p>
              {isLoading ? (
                <div className="space-y-2">{Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-16" />)}</div>
              ) : entries && entries.length > 0 ? (
                entries.map((e) => (
                  <div key={e.id} className="border border-slate-200 dark:border-slate-800 rounded-lg px-3 py-2.5 bg-white dark:bg-slate-900">
                    {editingEntry?.id === e.id ? (
                      <div className="space-y-2">
                        <p className="text-xs text-slate-500 dark:text-slate-400">Set the new total outstanding balance:</p>
                        <div>
                          <Label className="text-xs">New balance amount</Label>
                          <Input type="number" value={editAmount} onChange={(ev) => setEditAmount(ev.target.value)} />
                        </div>
                        <div>
                          <Label className="text-xs">Notes</Label>
                          <Input value={editNotes} onChange={(ev) => setEditNotes(ev.target.value)} />
                        </div>
                        <div className="flex gap-2">
                          <Button size="sm" onClick={saveEdit} disabled={saving || !editAmount}>
                            {saving ? "Saving..." : "Save"}
                          </Button>
                          <Button size="sm" variant="outline" onClick={() => setEditingEntry(null)}>Cancel</Button>
                        </div>
                      </div>
                    ) : (
                      <>
                        <div className="flex items-center justify-between">
                          <span className={`text-sm font-medium ${typeColor(e.entryType, e.source)}`}>
                            {typeLabel(e.entryType, e.source)}
                          </span>
                          <div className="flex items-center gap-2">
                            <span className={`text-sm font-semibold tabular-nums ${typeColor(e.entryType, e.source)}`}>
                              {e.entryType.includes("Payment") ? "-" : "+"}{formatNaira(e.amount)}
                            </span>
                            {canManage && e.source !== "Adjustment" && (
                              <div className="flex gap-0.5">
                                <button onClick={() => startEdit(e)} className="p-1 rounded hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-400 dark:text-slate-500 hover:text-slate-700 dark:hover:text-slate-300" title="Edit">
                                  <Pencil size={12} />
                                </button>
                                <button onClick={() => deleteEntry(e.id)} className="p-1 rounded hover:bg-red-50 text-slate-400 dark:text-slate-500 hover:text-red-500" title="Delete">
                                  <Trash2 size={12} />
                                </button>
                              </div>
                            )}
                          </div>
                        </div>
                        {e.notes && (
                          <p className="text-xs text-slate-600 dark:text-slate-400 mt-1">{e.notes}</p>
                        )}
                        <div className="flex items-center gap-2 mt-1.5">
                          <span className="text-xs text-slate-400 dark:text-slate-500">{formatDateTime(e.createdAtUtc)}</span>
                          <Badge variant="secondary" className="text-[10px] px-1.5 py-0">{e.source}</Badge>
                        </div>
                      </>
                    )}
                  </div>
                ))
              ) : (
                <p className="text-sm text-slate-400 dark:text-slate-500 text-center py-6">No ledger entries for this contact.</p>
              )}
            </div>
          </DrawerBody>
          <DrawerFooter>
            <Button variant="outline" onClick={handleClose}>Close</Button>
          </DrawerFooter>
        </>
      )}
    </Drawer>
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
              className="w-full h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 text-sm"
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
              className="w-full h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 text-sm"
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

function DeleteContactDialog({ contact, open, onClose }: { contact: ContactDto | null; open: boolean; onClose: () => void }) {
  const qc = useQueryClient();
  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const hasReceivable = (contact?.outstandingReceivable ?? 0) > 0;
  const hasPayable = (contact?.outstandingPayable ?? 0) > 0;
  const hasOpenBalance = hasReceivable || hasPayable;

  async function handleDelete() {
    if (!contact) return;
    setDeleting(true);
    setError(null);
    try {
      await api.delete(`/contacts/${contact.id}`);
      qc.invalidateQueries({ queryKey: ["contacts"] });
      onClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to delete contact");
    } finally {
      setDeleting(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) { setError(null); onClose(); } }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Delete Contact — {contact?.name}</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          {hasOpenBalance && (
            <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 space-y-1">
              <p className="text-sm font-medium text-amber-800">This contact has open balances:</p>
              {hasReceivable && (
                <p className="text-sm text-amber-700">
                  • Outstanding receivable: <strong>{formatNaira(contact!.outstandingReceivable)}</strong> (they owe you)
                </p>
              )}
              {hasPayable && (
                <p className="text-sm text-amber-700">
                  • Outstanding payable: <strong>{formatNaira(contact!.outstandingPayable)}</strong> (you owe them)
                </p>
              )}
              <p className="text-xs text-amber-600 pt-1">
                Deleting will remove all ledger entries for this contact. This cannot be undone.
              </p>
            </div>
          )}
          {!hasOpenBalance && (
            <p className="text-sm text-slate-600 dark:text-slate-400">
              Are you sure you want to delete <strong>{contact?.name}</strong>? This cannot be undone.
            </p>
          )}
          {error && <p className="text-xs text-red-500">{error}</p>}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={() => { setError(null); onClose(); }} disabled={deleting}>Cancel</Button>
          <Button
            onClick={handleDelete}
            disabled={deleting}
            className="bg-red-600 hover:bg-red-700 text-white"
          >
            {deleting ? "Deleting..." : hasOpenBalance ? "Delete Anyway" : "Delete Contact"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

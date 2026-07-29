"use client";

import { useState } from "react";
import { MapPin, Plus, Loader2 } from "lucide-react";
import { useBusiness, useDataSync } from "@/lib/data-sync";
import { api } from "@/lib/api";
import { useToast } from "@/components/toast";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import type { LocationDto } from "@/lib/types";

/**
 * Locations management (multi-location Phase 2). For entitled businesses (Scale+ / Multi-location add-on):
 * list + add + rename + (de)activate — the create-location home that lets a merchant set up their first
 * extra branch (the top-bar switcher only appears once there are 2+ active locations). For non-entitled
 * businesses: an upsell teaser. Drives the existing /api/business/locations endpoints; refreshes the
 * business afterwards so the switcher + this list update.
 */
export function LocationsCard() {
  const business = useBusiness();
  const { refresh } = useDataSync();
  const { toast } = useToast();

  const [adding, setAdding] = useState(false);
  const [newName, setNewName] = useState("");
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editForm, setEditForm] = useState({ name: "", address: "", city: "", state: "", phone: "" });
  const [busyId, setBusyId] = useState<string | null>(null); // "new" while adding, else the location id

  const entitled = !!business?.isMultiLocation;
  const locations = (business?.locations ?? []).slice().sort((a, b) => Number(b.isDefault) - Number(a.isDefault));

  // ── Upsell teaser (not entitled) ────────────────────────────────────────────
  if (!entitled) {
    return (
      <Card>
        <CardContent className="pt-6">
          <div className="flex items-start gap-3">
            <div className="mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-cyan-100 dark:bg-cyan-900/40">
              <MapPin size={16} className="text-cyan-600 dark:text-cyan-400" />
            </div>
            <div>
              <p className="font-semibold text-slate-900 dark:text-slate-50">Run multiple locations</p>
              <p className="mt-1 text-sm text-slate-500 dark:text-slate-400 max-w-md">
                Track stock, sales and reports per branch or warehouse, and switch between them in one click.
                Available on the <span className="font-medium">Scale</span> plan, or add <span className="font-medium">Multi-location</span> to your current plan.
              </p>
              <a href="#plan" className="mt-3 inline-block text-sm font-medium text-cyan-600 hover:text-cyan-700 dark:text-cyan-400">
                See plans →
              </a>
            </div>
          </div>
        </CardContent>
      </Card>
    );
  }

  async function addLocation() {
    const name = newName.trim();
    if (!name) return;
    setBusyId("new");
    try {
      await api.post("/business/locations", { name, type: "branch" });
      setNewName("");
      setAdding(false);
      await refresh();
      toast.success("Location added");
    } catch (e: unknown) {
      toast.error("Couldn't add location", errText(e));
    } finally {
      setBusyId(null);
    }
  }

  async function saveLocation(loc: LocationDto) {
    const name = editForm.name.trim();
    if (!name) return;
    setBusyId(loc.id);
    try {
      await api.patch(`/business/locations/${loc.id}`, {
        name,
        address: editForm.address.trim(),
        city: editForm.city.trim(),
        state: editForm.state.trim(),
        phone: editForm.phone.trim(),
      });
      setEditingId(null);
      await refresh();
      toast.success("Location saved");
    } catch (e: unknown) {
      toast.error("Couldn't save location", errText(e));
    } finally {
      setBusyId(null);
    }
  }

  async function toggleActive(loc: LocationDto) {
    setBusyId(loc.id);
    try {
      await api.patch(`/business/locations/${loc.id}`, { isActive: !loc.isActive });
      await refresh();
      toast.success(loc.isActive ? "Location deactivated" : "Location reactivated");
    } catch (e: unknown) {
      toast.error("Couldn't update location", errText(e));
    } finally {
      setBusyId(null);
    }
  }

  return (
    <Card>
      <CardContent className="pt-6 space-y-4">
        <p className="text-sm text-slate-500 dark:text-slate-400">
          Your branches &amp; warehouses. Switch between them from the selector at the top of the page to view stock,
          sales and reports for a specific location.
        </p>

        <ul className="space-y-2">
          {locations.map((l) => (
            <li
              key={l.id}
              className="flex items-center justify-between gap-3 rounded-lg border border-slate-200 dark:border-slate-800 px-3 py-2.5"
            >
              {editingId === l.id ? (
                <div className="flex-1 space-y-2">
                  <Input value={editForm.name} onChange={(e) => setEditForm((f) => ({ ...f, name: e.target.value }))}
                    placeholder="Branch name" autoFocus className="h-8" maxLength={200} />
                  <Input value={editForm.address} onChange={(e) => setEditForm((f) => ({ ...f, address: e.target.value }))}
                    placeholder="Address (shown on receipts)" className="h-8" maxLength={300} />
                  <div className="flex gap-2">
                    <Input value={editForm.city} onChange={(e) => setEditForm((f) => ({ ...f, city: e.target.value }))}
                      placeholder="City" className="h-8" maxLength={100} />
                    <Input value={editForm.state} onChange={(e) => setEditForm((f) => ({ ...f, state: e.target.value }))}
                      placeholder="State" className="h-8" maxLength={100} />
                  </div>
                  <Input value={editForm.phone} onChange={(e) => setEditForm((f) => ({ ...f, phone: e.target.value }))}
                    placeholder="Phone (shown on receipts)" className="h-8" maxLength={40} />
                  <p className="text-[11px] text-slate-400 dark:text-slate-500">Address &amp; phone print on this branch&rsquo;s receipts. Leave blank to use your business details.</p>
                  <div className="flex gap-2">
                    <Button size="sm" disabled={busyId === l.id || !editForm.name.trim()} onClick={() => saveLocation(l)}>
                      {busyId === l.id ? <Loader2 size={14} className="animate-spin" /> : "Save"}
                    </Button>
                    <Button size="sm" variant="ghost" onClick={() => setEditingId(null)}>Cancel</Button>
                  </div>
                </div>
              ) : (
                <>
                  <div className="flex items-center gap-2 min-w-0">
                    <MapPin size={14} className="shrink-0 text-slate-400" />
                    <span className={`truncate text-sm font-medium ${l.isActive ? "text-slate-900 dark:text-slate-100" : "text-slate-400 line-through"}`}>
                      {l.name}
                    </span>
                    {l.isDefault && <Badge variant="secondary">Default</Badge>}
                    {!l.isActive && <Badge variant="outline">Inactive</Badge>}
                  </div>
                  <div className="flex items-center gap-1 shrink-0">
                    <Button size="sm" variant="ghost" onClick={() => { setEditingId(l.id); setEditForm({ name: l.name, address: l.address ?? "", city: l.city ?? "", state: l.state ?? "", phone: l.phone ?? "" }); }}>
                      Edit
                    </Button>
                    {!l.isDefault && (
                      <Button size="sm" variant="ghost" disabled={busyId === l.id} onClick={() => toggleActive(l)}>
                        {busyId === l.id ? <Loader2 size={14} className="animate-spin" /> : (l.isActive ? "Deactivate" : "Reactivate")}
                      </Button>
                    )}
                  </div>
                </>
              )}
            </li>
          ))}
        </ul>

        {adding ? (
          <div className="flex items-center gap-2">
            <Input
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              onKeyDown={(e) => { if (e.key === "Enter") addLocation(); if (e.key === "Escape") { setAdding(false); setNewName(""); } }}
              placeholder="Location name (e.g. Ikeja branch)"
              autoFocus
              className="h-9"
              maxLength={200}
            />
            <Button onClick={addLocation} disabled={busyId === "new" || !newName.trim()}>
              {busyId === "new" ? <Loader2 size={15} className="animate-spin" /> : "Add"}
            </Button>
            <Button variant="ghost" onClick={() => { setAdding(false); setNewName(""); }}>Cancel</Button>
          </div>
        ) : (
          <Button variant="outline" size="sm" onClick={() => setAdding(true)}>
            <Plus size={14} className="mr-1.5" /> Add location
          </Button>
        )}
      </CardContent>
    </Card>
  );
}

function errText(e: unknown): string | undefined {
  const err = e as { response?: { data?: { error?: string } } };
  return err?.response?.data?.error;
}

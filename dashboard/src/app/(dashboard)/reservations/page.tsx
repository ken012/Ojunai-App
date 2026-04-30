"use client";

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { formatDateTime } from "@/lib/format";
import { hasPermission, Permission } from "@/lib/permissions";
import { usePlanStatus } from "@/lib/use-plan-status";
import type { StockHoldDto } from "@/lib/types";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from "@/components/ui/table";
import { Package, ShoppingCart, Unlock, Clock, CheckCircle, RefreshCw, Phone } from "lucide-react";

// ── Types ────────────────────────────────────────────────────────────────────

type VoiceReservation = {
  id: string;
  businessId: string;
  productId: string;
  productName: string;
  productSku: string | null;
  quantity: number;
  customerPhone: string;
  customerName: string | null;
  language: string;
  status: string;
  callSessionId: string | null;
  createdAt: string;
  updatedAt: string;
  holdExpiresAt: string;
};

type UnifiedHold = {
  id: string;
  productName: string;
  contactName: string;
  quantity: number;
  unit: string;
  source: "Manual" | "WhatsApp" | "VoiceAI" | "Import" | string;
  status: string;
  notes: string | null;
  createdAt: string;
  holdExpiresAt: string | null;
  isVoice: boolean;
  callSessionId: string | null;
};

// ── Helpers ──────────────────────────────────────────────────────────────────

const SOURCE_STYLES: Record<string, { bg: string; text: string; label: string; icon?: typeof Phone }> = {
  Manual: { bg: "bg-sky-100", text: "text-sky-700", label: "Dashboard" },
  WhatsApp: { bg: "bg-green-100", text: "text-green-700", label: "WhatsApp" },
  VoiceAI: { bg: "bg-violet-100", text: "text-violet-700", label: "Voice AI", icon: Phone },
  Import: { bg: "bg-slate-100", text: "text-slate-600", label: "Import" },
};

function SourceBadge({ source }: { source: string }) {
  const s = SOURCE_STYLES[source] ?? { bg: "bg-slate-100", text: "text-slate-600", label: source };
  return (
    <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-semibold ${s.bg} ${s.text}`}>
      {s.icon && <Phone size={10} />}
      {s.label}
    </span>
  );
}

function StatusBadge({ status }: { status: string }) {
  switch (status) {
    case "Active": case "pending": return <Badge className="bg-amber-100 text-amber-700 border-0">Active</Badge>;
    case "confirmed": return <Badge className="bg-sky-100 text-sky-700 border-0">Confirmed</Badge>;
    case "Converted": case "fulfilled": return <Badge className="bg-emerald-100 text-emerald-700 border-0">Fulfilled</Badge>;
    case "Released": case "cancelled": return <Badge className="bg-slate-100 text-slate-500 border-0">{status === "Released" ? "Released" : "Cancelled"}</Badge>;
    case "expired": return <Badge className="bg-red-100 text-red-600 border-0">Expired</Badge>;
    default: return <Badge variant="outline">{status}</Badge>;
  }
}

function HoldCountdown({ expiresAt }: { expiresAt: string }) {
  const diff = new Date(expiresAt).getTime() - Date.now();
  if (diff <= 0) return <span className="text-red-500 text-[10px]">Expiring</span>;
  const hours = Math.floor(diff / 3600000);
  const mins = Math.floor((diff % 3600000) / 60000);
  return <span className="text-xs text-slate-500">{hours}h {mins}m left</span>;
}

function isActiveStatus(status: string) {
  return status === "Active" || status === "pending" || status === "confirmed";
}

// ── Page ─────────────────────────────────────────────────────────────────────

export default function ReservationsPage() {
  const qc = useQueryClient();
  const { data: planStatus } = usePlanStatus();
  const [tab, setTab] = useState<"active" | "completed">("active");
  const [actionLoading, setActionLoading] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);

  const hasVoiceAI = planStatus?.voiceAIEnabled ?? false;

  // Dashboard holds
  const { data: allHolds, isLoading: loadingHolds } = useQuery({
    queryKey: ["reservations-holds"],
    queryFn: async () => {
      const { data } = await api.get<{ data: StockHoldDto[] }>("/stock-holds/all");
      return data.data ?? [];
    },
  });

  // Voice AI reservations (only if enabled)
  const { data: voiceData, isLoading: loadingVoice } = useQuery({
    queryKey: ["reservations-voice"],
    queryFn: async () => {
      const { data } = await api.get<{ reservations: VoiceReservation[] }>("/business/voice-ai-reservations?status=all&limit=200");
      return data.reservations ?? [];
    },
    enabled: hasVoiceAI,
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  });

  // Merge into unified list
  const unified: UnifiedHold[] = [
    ...(allHolds ?? []).map((h): UnifiedHold => ({
      id: h.id,
      productName: h.productName,
      contactName: h.contactName,
      quantity: h.quantity,
      unit: h.unit,
      source: h.source ?? "Manual",
      status: h.status,
      notes: h.notes ?? null,
      createdAt: h.createdAtUtc,
      holdExpiresAt: null,
      isVoice: false,
      callSessionId: null,
    })),
    ...(voiceData ?? []).map((r): UnifiedHold => ({
      id: `voice-${r.id}`,
      productName: r.productName,
      contactName: r.customerName ?? r.customerPhone,
      quantity: r.quantity,
      unit: "unit",
      source: "VoiceAI",
      status: r.status,
      notes: r.productSku ? `SKU: ${r.productSku}` : null,
      createdAt: r.createdAt,
      holdExpiresAt: r.holdExpiresAt,
      isVoice: true,
      callSessionId: r.callSessionId,
    })),
  ].sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());

  const activeItems = unified.filter(h => isActiveStatus(h.status));
  const completedItems = unified.filter(h => !isActiveStatus(h.status));
  const displayed = tab === "active" ? activeItems : completedItems;

  const isLoading = loadingHolds || (hasVoiceAI && loadingVoice);

  async function handleAction(holdId: string, action: "release" | "convert") {
    setActionLoading(holdId);
    try {
      await api.post(`/stock-holds/${holdId}/${action}`);
      qc.invalidateQueries({ queryKey: ["reservations-holds"] });
      qc.invalidateQueries({ queryKey: ["products"] });
      qc.invalidateQueries({ queryKey: ["sales"] });
    } catch { /* silent */ } finally {
      setActionLoading(null);
    }
  }

  async function handleRefresh() {
    setRefreshing(true);
    await Promise.all([
      qc.invalidateQueries({ queryKey: ["reservations-holds"] }),
      hasVoiceAI ? qc.invalidateQueries({ queryKey: ["reservations-voice"] }) : Promise.resolve(),
    ]);
    setRefreshing(false);
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold text-slate-900">Order Reservations</h2>
          <p className="text-slate-500 text-sm mt-0.5">
            Track all stock holds from dashboard, WhatsApp{hasVoiceAI ? ", and Voice AI" : ""}
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={handleRefresh} disabled={refreshing}>
          <RefreshCw size={14} className={`mr-1 ${refreshing ? "animate-spin" : ""}`} />
          Refresh
        </Button>
      </div>

      {/* Summary cards */}
      <div className="grid grid-cols-3 gap-4">
        <Card className={`cursor-pointer transition-all ${tab === "active" ? "ring-2 ring-amber-500" : "hover:shadow-md"}`} onClick={() => setTab("active")}>
          <CardContent className="p-4 text-center">
            <Clock size={20} className="mx-auto text-amber-400 mb-1" />
            <p className="text-xl font-bold text-amber-600">{activeItems.length}</p>
            <p className="text-xs text-slate-500">Active Holds</p>
          </CardContent>
        </Card>
        <Card className={`cursor-pointer transition-all ${tab === "completed" ? "ring-2 ring-emerald-500" : "hover:shadow-md"}`} onClick={() => setTab("completed")}>
          <CardContent className="p-4 text-center">
            <CheckCircle size={20} className="mx-auto text-emerald-400 mb-1" />
            <p className="text-xl font-bold text-emerald-600">{completedItems.length}</p>
            <p className="text-xs text-slate-500">Completed</p>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="p-4 text-center">
            <Package size={20} className="mx-auto text-slate-400 mb-1" />
            <p className="text-xl font-bold text-slate-900">{unified.length}</p>
            <p className="text-xs text-slate-500">Total</p>
          </CardContent>
        </Card>
      </div>

      {/* Table */}
      <Card>
        <CardContent className="pt-4">
          {isLoading ? (
            <div className="space-y-2">
              {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-10" />)}
            </div>
          ) : displayed.length > 0 ? (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Product</TableHead>
                  <TableHead>Customer</TableHead>
                  <TableHead className="text-center">Qty</TableHead>
                  <TableHead>Source</TableHead>
                  <TableHead>Status</TableHead>
                  {tab === "active" && <TableHead>Expires</TableHead>}
                  <TableHead>Date</TableHead>
                  <TableHead>Notes</TableHead>
                  {tab === "active" && <TableHead className="text-right">Actions</TableHead>}
                </TableRow>
              </TableHeader>
              <TableBody>
                {displayed.map((h) => (
                  <TableRow key={h.id}>
                    <TableCell>
                      <p className={`text-sm font-medium ${h.productName === "(deleted)" ? "text-slate-400 italic" : "text-slate-900"}`}>
                        {h.productName}
                      </p>
                      {!h.isVoice && <p className="text-[10px] text-slate-400">{h.unit}</p>}
                    </TableCell>
                    <TableCell className="text-sm text-slate-700">{h.contactName}</TableCell>
                    <TableCell className="text-center font-semibold text-slate-900">{h.quantity}</TableCell>
                    <TableCell><SourceBadge source={h.source} /></TableCell>
                    <TableCell><StatusBadge status={h.status} /></TableCell>
                    {tab === "active" && (
                      <TableCell>
                        {h.holdExpiresAt ? <HoldCountdown expiresAt={h.holdExpiresAt} /> : <span className="text-xs text-slate-300">—</span>}
                      </TableCell>
                    )}
                    <TableCell className="text-xs text-slate-500">{formatDateTime(h.createdAt)}</TableCell>
                    <TableCell className="text-xs text-slate-500 max-w-xs truncate">{h.notes ?? "—"}</TableCell>
                    {tab === "active" && (
                      <TableCell className="text-right">
                        {!h.isVoice ? (
                          <div className="flex items-center justify-end gap-1">
                            {hasPermission(Permission.RecordSales) && (
                              <button onClick={() => handleAction(h.id, "convert")} disabled={actionLoading === h.id}
                                className="flex items-center gap-1 px-2 py-1 rounded text-xs font-medium bg-emerald-50 text-emerald-700 hover:bg-emerald-100 transition-colors">
                                <ShoppingCart size={12} /> Sell
                              </button>
                            )}
                            {hasPermission(Permission.ManageStock) && (
                              <button onClick={() => handleAction(h.id, "release")} disabled={actionLoading === h.id}
                                className="flex items-center gap-1 px-2 py-1 rounded text-xs font-medium bg-slate-100 text-slate-600 hover:bg-slate-200 transition-colors">
                                <Unlock size={12} /> Release
                              </button>
                            )}
                          </div>
                        ) : (
                          <span className="text-[10px] text-slate-400">Managed by Voice AI</span>
                        )}
                      </TableCell>
                    )}
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          ) : (
            <div className="text-center py-12 text-slate-400">
              <Package size={32} className="mx-auto mb-2 opacity-30" />
              <p>{tab === "active" ? "No active reservations." : "No completed reservations yet."}</p>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

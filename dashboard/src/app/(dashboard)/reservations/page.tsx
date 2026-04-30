"use client";

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { formatDateTime } from "@/lib/format";
import { hasPermission, Permission } from "@/lib/permissions";
import type { StockHoldDto } from "@/lib/types";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from "@/components/ui/table";
import { Package, ShoppingCart, Unlock, Clock, CheckCircle, XCircle } from "lucide-react";

const SOURCE_STYLES: Record<string, { bg: string; text: string; label: string }> = {
  Manual: { bg: "bg-sky-100", text: "text-sky-700", label: "Dashboard" },
  WhatsApp: { bg: "bg-green-100", text: "text-green-700", label: "WhatsApp" },
  VoiceAI: { bg: "bg-violet-100", text: "text-violet-700", label: "Voice AI" },
  Import: { bg: "bg-slate-100", text: "text-slate-600", label: "Import" },
};

function SourceBadge({ source }: { source?: string }) {
  const s = SOURCE_STYLES[source ?? ""] ?? { bg: "bg-slate-100", text: "text-slate-600", label: source ?? "Unknown" };
  return <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-semibold ${s.bg} ${s.text}`}>{s.label}</span>;
}

function StatusBadge({ status }: { status: string }) {
  if (status === "Active") return <Badge className="bg-amber-100 text-amber-700 border-0">Active</Badge>;
  if (status === "Converted") return <Badge className="bg-emerald-100 text-emerald-700 border-0">Sold</Badge>;
  if (status === "Released") return <Badge className="bg-slate-100 text-slate-500 border-0">Released</Badge>;
  return <Badge variant="outline">{status}</Badge>;
}

export default function ReservationsPage() {
  const qc = useQueryClient();
  const [tab, setTab] = useState<"Active" | "completed">("Active");
  const [actionLoading, setActionLoading] = useState<string | null>(null);

  const { data: activeHolds, isLoading: loadingActive } = useQuery({
    queryKey: ["reservations", "Active"],
    queryFn: async () => {
      const { data } = await api.get<{ data: StockHoldDto[] }>("/stock-holds/all?status=Active");
      return data.data!;
    },
  });

  const { data: completedHolds, isLoading: loadingCompleted } = useQuery({
    queryKey: ["reservations", "completed"],
    queryFn: async () => {
      const { data } = await api.get<{ data: StockHoldDto[] }>("/stock-holds/all");
      return (data.data ?? []).filter(h => h.status !== "Active");
    },
  });

  const holds = tab === "Active" ? activeHolds : completedHolds;
  const isLoading = tab === "Active" ? loadingActive : loadingCompleted;

  const activeCount = activeHolds?.length ?? 0;
  const completedCount = completedHolds?.length ?? 0;

  async function handleAction(holdId: string, action: "release" | "convert") {
    setActionLoading(holdId);
    try {
      await api.post(`/stock-holds/${holdId}/${action}`);
      qc.invalidateQueries({ queryKey: ["reservations"] });
      qc.invalidateQueries({ queryKey: ["products"] });
      qc.invalidateQueries({ queryKey: ["sales"] });
    } catch { /* silent */ } finally {
      setActionLoading(null);
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-2xl font-bold text-slate-900">Order Reservations</h2>
        <p className="text-slate-500 text-sm mt-0.5">Track all stock holds from dashboard, WhatsApp, and Voice AI</p>
      </div>

      {/* Summary cards */}
      <div className="grid grid-cols-3 gap-4">
        <Card className={`cursor-pointer transition-all ${tab === "Active" ? "ring-2 ring-amber-500" : "hover:shadow-md"}`} onClick={() => setTab("Active")}>
          <CardContent className="p-4 text-center">
            <Clock size={20} className="mx-auto text-amber-400 mb-1" />
            <p className="text-xl font-bold text-amber-600">{activeCount}</p>
            <p className="text-xs text-slate-500">Active Holds</p>
          </CardContent>
        </Card>
        <Card className={`cursor-pointer transition-all ${tab === "completed" ? "ring-2 ring-emerald-500" : "hover:shadow-md"}`} onClick={() => setTab("completed")}>
          <CardContent className="p-4 text-center">
            <CheckCircle size={20} className="mx-auto text-emerald-400 mb-1" />
            <p className="text-xl font-bold text-emerald-600">{completedCount}</p>
            <p className="text-xs text-slate-500">Completed</p>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="p-4 text-center">
            <Package size={20} className="mx-auto text-slate-400 mb-1" />
            <p className="text-xl font-bold text-slate-900">{activeCount + completedCount}</p>
            <p className="text-xs text-slate-500">Total Reservations</p>
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
          ) : holds && holds.length > 0 ? (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Product</TableHead>
                  <TableHead>Customer</TableHead>
                  <TableHead className="text-center">Qty</TableHead>
                  <TableHead>Source</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Date</TableHead>
                  <TableHead>Notes</TableHead>
                  {tab === "Active" && <TableHead className="text-right">Actions</TableHead>}
                </TableRow>
              </TableHeader>
              <TableBody>
                {holds.map((h) => (
                  <TableRow key={h.id}>
                    <TableCell>
                      <p className="text-sm font-medium text-slate-900">{h.productName}</p>
                      <p className="text-[10px] text-slate-400">{h.unit}</p>
                    </TableCell>
                    <TableCell className="text-sm text-slate-700">{h.contactName}</TableCell>
                    <TableCell className="text-center font-semibold text-slate-900">{h.quantity}</TableCell>
                    <TableCell><SourceBadge source={h.source} /></TableCell>
                    <TableCell><StatusBadge status={h.status} /></TableCell>
                    <TableCell className="text-xs text-slate-500">{formatDateTime(h.createdAtUtc)}</TableCell>
                    <TableCell className="text-xs text-slate-500 max-w-xs truncate">{h.notes ?? "—"}</TableCell>
                    {tab === "Active" && (
                      <TableCell className="text-right">
                        <div className="flex items-center justify-end gap-1">
                          {hasPermission(Permission.RecordSales) && (
                            <button
                              onClick={() => handleAction(h.id, "convert")}
                              disabled={actionLoading === h.id}
                              className="flex items-center gap-1 px-2 py-1 rounded text-xs font-medium bg-emerald-50 text-emerald-700 hover:bg-emerald-100 transition-colors"
                              title="Convert to sale"
                            >
                              <ShoppingCart size={12} />
                              Sell
                            </button>
                          )}
                          {hasPermission(Permission.ManageStock) && (
                            <button
                              onClick={() => handleAction(h.id, "release")}
                              disabled={actionLoading === h.id}
                              className="flex items-center gap-1 px-2 py-1 rounded text-xs font-medium bg-slate-100 text-slate-600 hover:bg-slate-200 transition-colors"
                              title="Release hold"
                            >
                              <Unlock size={12} />
                              Release
                            </button>
                          )}
                        </div>
                      </TableCell>
                    )}
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          ) : (
            <div className="text-center py-12 text-slate-400">
              <Package size={32} className="mx-auto mb-2 opacity-30" />
              <p>{tab === "Active" ? "No active reservations." : "No completed reservations yet."}</p>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

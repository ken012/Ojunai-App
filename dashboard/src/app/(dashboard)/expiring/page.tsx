"use client";

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { PageHeader } from "@/components/page-header";
import { hasPermission, Permission } from "@/lib/permissions";
import type { ProductBatchDto } from "@/lib/types";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/empty-state";
import { useToast } from "@/components/toast";
import { CalendarClock, AlertTriangle } from "lucide-react";

const WINDOWS = [7, 30, 90];

export default function ExpiringPage() {
  const qc = useQueryClient();
  const { toast } = useToast();
  const canManage = hasPermission(Permission.ManageStock);
  const [days, setDays] = useState(30);
  const [busy, setBusy] = useState<string | null>(null);

  const { data, isLoading } = useQuery({
    queryKey: ["expiring", days],
    queryFn: async () => {
      const { data } = await api.get<{ data: ProductBatchDto[] }>(`/products/expiring?days=${days}`);
      return data.data!;
    },
  });

  async function writeOff(b: ProductBatchDto) {
    if (!confirm(`Write off ${b.quantity} ${b.unit} of ${b.productName}? This records a wastage and reduces stock.`)) return;
    setBusy(b.id);
    try {
      await api.post(`/products/${b.productId}/batches/${b.id}/write-off`, {});
      qc.invalidateQueries({ queryKey: ["expiring"] });
      qc.invalidateQueries({ queryKey: ["products"] });
      toast.success("Lot written off", `${b.productName} stock reduced.`);
    } catch {
      toast.error("Couldn't write off", "Please try again.");
    } finally { setBusy(null); }
  }

  const batches = data ?? [];

  return (
    <div className="space-y-5">
      <PageHeader title="Expiring stock" subtitle="Lots approaching or past their expiry date" />

      <div className="flex gap-1.5">
        {WINDOWS.map((w) => (
          <button
            key={w}
            onClick={() => setDays(w)}
            className={`px-3 py-1.5 rounded-md text-xs font-medium transition-colors ${
              days === w
                ? "bg-cyan-100 text-cyan-700 border border-cyan-200 dark:bg-cyan-950/40 dark:text-cyan-300 dark:border-cyan-900"
                : "bg-slate-50 dark:bg-slate-950 text-slate-500 dark:text-slate-400 border border-slate-200 dark:border-slate-800"
            }`}
          >
            Next {w} days
          </button>
        ))}
      </div>

      {isLoading ? (
        <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-16 w-full rounded-lg" />)}</div>
      ) : batches.length === 0 ? (
        <EmptyState
          icon={<CalendarClock size={40} className="text-slate-300" />}
          title="Nothing expiring"
          description={`No batch-tracked lots expire within ${days} days. Enable batch tracking on a product and record expiry dates when you restock.`}
        />
      ) : (
        <div className="space-y-2">
          {batches.map((b) => (
            <Card key={b.id} className={b.isExpired ? "border-rose-300 dark:border-rose-800" : (b.daysToExpiry ?? 99) <= 7 ? "border-amber-300 dark:border-amber-800" : ""}>
              <CardContent className="p-4 flex items-center justify-between gap-3">
                <div className="min-w-0">
                  <p className="font-medium text-sm text-slate-900 dark:text-slate-50 truncate">{b.productName}</p>
                  <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
                    {b.quantity} {b.unit}{b.lotNumber ? ` · lot ${b.lotNumber}` : ""}
                    {" · "}
                    {b.isExpired ? (
                      <span className="text-rose-600 font-medium inline-flex items-center gap-1"><AlertTriangle size={11} /> expired {b.expiryDate}</span>
                    ) : (
                      <span className={(b.daysToExpiry ?? 99) <= 7 ? "text-amber-600 font-medium" : ""}>
                        expires {b.expiryDate} ({b.daysToExpiry}d)
                      </span>
                    )}
                  </p>
                </div>
                {canManage && (
                  <Button variant="outline" size="sm" onClick={() => writeOff(b)} disabled={busy === b.id} className="flex-shrink-0 text-rose-600 hover:text-rose-700">
                    {busy === b.id ? "…" : "Write off"}
                  </Button>
                )}
              </CardContent>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}

"use client";

import { useState, useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { Label } from "@/components/ui/label";
import { api } from "@/lib/api";
import { formatNaira, formatDateTime } from "@/lib/format";
import type { ActivityFeedDto } from "@/lib/types";
import { Card, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { SourceBadge } from "@/components/source-badge";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { ChevronLeft, ChevronRight } from "lucide-react";

const TYPE_CONFIG: Record<string, { label: string; variant: "default" | "secondary" | "destructive" | "outline" }> = {
  sale: { label: "Sale", variant: "default" },
  expense: { label: "Expense", variant: "destructive" },
  inventory: { label: "Inventory", variant: "secondary" },
  payment_received: { label: "Received", variant: "default" },
  payment_made: { label: "Paid", variant: "outline" },
};

const PAGE_SIZE = 25;

export default function ActivityPage() {
  const [typeFilter, setTypeFilter] = useState<string>("all");
  const [byFilter, setByFilter] = useState<string>("");
  const [page, setPage] = useState(0);

  const queryType = typeFilter === "all" ? undefined : typeFilter;

  const { data: items, isLoading } = useQuery({
    queryKey: ["activity-feed", typeFilter, page],
    queryFn: async () => {
      const params = new URLSearchParams();
      params.set("limit", String(PAGE_SIZE));
      params.set("offset", String(page * PAGE_SIZE));
      if (queryType) params.set("type", queryType);
      const { data } = await api.get<{ data: ActivityFeedDto[] }>(`/dashboard/activity?${params}`);
      return data.data!;
    },
  });

  const staffNames = useMemo(() => {
    const names = new Set<string>();
    items?.forEach((i) => { if (i.recordedBy) names.add(i.recordedBy); });
    return Array.from(names).sort();
  }, [items]);

  const filteredItems = useMemo(() => {
    if (!items) return [];
    if (!byFilter) return items;
    if (byFilter === "__unassigned__") return items.filter((i) => !i.recordedBy);
    return items.filter((i) => i.recordedBy === byFilter);
  }, [items, byFilter]);

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-2xl font-bold text-slate-900">Activity</h2>
        <p className="text-slate-500 text-sm mt-0.5">All transactions and events across your business</p>
      </div>

      <div className="flex items-center gap-4 flex-wrap">
        <Tabs value={typeFilter} onValueChange={(v) => { setTypeFilter(v); setPage(0); }}>
          <TabsList>
            <TabsTrigger value="all">All</TabsTrigger>
            <TabsTrigger value="sale">Sales</TabsTrigger>
            <TabsTrigger value="expense">Expenses</TabsTrigger>
          <TabsTrigger value="inventory">Inventory</TabsTrigger>
          <TabsTrigger value="payment">Payments</TabsTrigger>
        </TabsList>
      </Tabs>

        <div className="flex items-center gap-2">
          <Label className="text-sm text-slate-500 whitespace-nowrap">By:</Label>
          <select
            className="h-9 px-2 rounded-md border border-slate-200 text-sm bg-white"
            value={byFilter}
            onChange={(e) => { setByFilter(e.target.value); setPage(0); }}
          >
            <option value="">Everyone</option>
            <option value="__unassigned__">Unassigned</option>
            {staffNames.map((n) => (
              <option key={n} value={n}>{n}</option>
            ))}
          </select>
        </div>

        {(byFilter) && (
          <button onClick={() => setByFilter("")} className="text-xs text-sky-600 hover:underline">Clear filter</button>
        )}
      </div>

      <Card>
        <CardContent className="pt-4">
          {isLoading ? (
            <div className="space-y-2">
              {Array.from({ length: 10 }).map((_, i) => (
                <Skeleton key={i} className="h-12" />
              ))}
            </div>
          ) : (
            <>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="w-[100px]">Type</TableHead>
                    <TableHead>Description</TableHead>
                    <TableHead>Contact</TableHead>
                    <TableHead>Source</TableHead>
                    <TableHead>By</TableHead>
                    <TableHead className="text-right">Amount</TableHead>
                    <TableHead className="text-right">Date</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {filteredItems.map((item) => {
                    const config = TYPE_CONFIG[item.type] ?? { label: item.type, variant: "outline" as const };
                    const isPositive = item.type === "sale" || item.type === "payment_received";
                    return (
                      <TableRow key={`${item.type}-${item.id}`}>
                        <TableCell>
                          <Badge variant={config.variant} className="text-xs">
                            {config.label}
                          </Badge>
                        </TableCell>
                        <TableCell className="text-sm text-slate-700 max-w-xs">
                          <span className="block truncate">{item.description}</span>
                          {item.details && (
                            <span className="block text-xs text-slate-400 truncate">{item.details}</span>
                          )}
                          {item.paymentStatus && item.type === "sale" && (
                            <span className="text-xs text-slate-400"> ({item.paymentStatus}{item.paymentMethod ? `, ${item.paymentMethod}` : ""})</span>
                          )}
                        </TableCell>
                        <TableCell className="text-sm text-slate-500">
                          {item.contactName ?? <span className="text-slate-300">—</span>}
                        </TableCell>
                        <TableCell>
                          <SourceBadge source={item.source} />
                        </TableCell>
                        <TableCell className="text-xs text-slate-500">
                          {item.recordedBy ?? <span className="text-slate-300">—</span>}
                        </TableCell>
                        <TableCell className={`text-right font-medium ${isPositive ? "text-emerald-600" : "text-red-500"}`}>
                          {item.amount != null ? (
                            <>
                              {isPositive ? "+" : "-"}
                              {formatNaira(item.amount)}
                            </>
                          ) : (
                            <span className="text-slate-300">—</span>
                          )}
                        </TableCell>
                        <TableCell className="text-right text-xs text-slate-500 whitespace-nowrap">
                          {formatDateTime(item.createdAtUtc)}
                        </TableCell>
                      </TableRow>
                    );
                  })}
                  {filteredItems.length === 0 && (
                    <TableRow>
                      <TableCell colSpan={7} className="text-center py-8 text-slate-400">
                        No activity found{typeFilter !== "all" ? ` for ${typeFilter}` : ""}.
                      </TableCell>
                    </TableRow>
                  )}
                </TableBody>
              </Table>

              <div className="flex items-center justify-between mt-4 pt-4 border-t">
                <p className="text-xs text-slate-500">
                  Showing {filteredItems.length} items (page {page + 1})
                </p>
                <div className="flex gap-2">
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => setPage((p) => Math.max(0, p - 1))}
                    disabled={page === 0}
                  >
                    <ChevronLeft size={14} />
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => setPage((p) => p + 1)}
                    disabled={(items?.length ?? 0) < PAGE_SIZE}  // pagination still based on raw data count
                  >
                    <ChevronRight size={14} />
                  </Button>
                </div>
              </div>
            </>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

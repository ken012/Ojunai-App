"use client";

import { useState, useEffect } from "react";
import { useQuery } from "@tanstack/react-query";
import { Label } from "@/components/ui/label";
import { Input } from "@/components/ui/input";
import { api } from "@/lib/api";
import { formatNaira, formatDateTime } from "@/lib/format";
import type { PaginatedActivityResult } from "@/lib/types";
import { Card, CardContent } from "@/components/ui/card";
// Badge import removed — using inline styled spans for type labels instead
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
import { Search, X, ChevronLeft, ChevronRight } from "lucide-react";

const TYPE_CONFIG: Record<string, { label: string; color: string }> = {
  sale: { label: "Sale", color: "bg-emerald-100 text-emerald-700" },
  sale_voided: { label: "Sale [Voided]", color: "bg-red-100 text-red-700 line-through" },
  void_event: { label: "Void", color: "bg-red-100 text-red-700" },
  expense: { label: "Expense", color: "bg-orange-100 text-orange-700" },
  expense_voided: { label: "Expense [Voided]", color: "bg-red-100 text-red-700 line-through" },
  inventory: { label: "Inventory", color: "bg-cyan-100 text-cyan-700" },
  debt_recorded: { label: "Debt", color: "bg-violet-100 text-violet-700" },
  payment_received: { label: "Payment In", color: "bg-emerald-100 text-emerald-700" },
  payment_made: { label: "Payment Out", color: "bg-orange-100 text-orange-700" },
  adjustment: { label: "Adjustment", color: "bg-amber-100 text-amber-700" },
};

const PAGE_SIZE = 25;

export default function ActivityPage() {
  const [typeFilter, setTypeFilter] = useState<string>("all");
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [startDate, setStartDate] = useState("");
  const [endDate, setEndDate] = useState("");
  const [page, setPage] = useState(1);

  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search.trim()), 300);
    return () => clearTimeout(t);
  }, [search]);

  // Reset to page 1 when any filter changes
  useEffect(() => { setPage(1); }, [typeFilter, debouncedSearch, startDate, endDate]);

  const { data, isLoading } = useQuery({
    queryKey: ["activity-feed", typeFilter, page, debouncedSearch, startDate, endDate],
    queryFn: async () => {
      const params = new URLSearchParams();
      params.set("page", String(page));
      params.set("pageSize", String(PAGE_SIZE));
      if (typeFilter !== "all") params.set("type", typeFilter);
      if (debouncedSearch) params.set("search", debouncedSearch);
      if (startDate) params.set("startDate", startDate);
      if (endDate) params.set("endDate", endDate);
      const { data } = await api.get<{ data: PaginatedActivityResult }>(`/dashboard/activity?${params}`);
      return data.data!;
    },
  });

  const items = data?.items ?? [];
  const totalPages = data?.totalPages ?? 1;
  const totalCount = data?.totalCount ?? 0;

  const hasFilters = typeFilter !== "all" || debouncedSearch || startDate || endDate;

  function clearFilters() {
    setTypeFilter("all");
    setSearch("");
    setStartDate("");
    setEndDate("");
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-2xl font-bold text-slate-900">Audit Log</h2>
        <p className="text-slate-500 text-sm mt-0.5">Complete record of every transaction and action</p>
      </div>

      {/* Filters row */}
      <div className="flex flex-col gap-3">
        <div className="flex items-center gap-3 flex-wrap">
          <Tabs value={typeFilter} onValueChange={setTypeFilter}>
            <TabsList>
              <TabsTrigger value="all">All</TabsTrigger>
              <TabsTrigger value="sale">Sales</TabsTrigger>
              <TabsTrigger value="expense">Expenses</TabsTrigger>
              <TabsTrigger value="inventory">Inventory</TabsTrigger>
              <TabsTrigger value="payment">Payments</TabsTrigger>
              <TabsTrigger value="adjustment">Adjustments</TabsTrigger>
            </TabsList>
          </Tabs>
        </div>

        <div className="flex items-center gap-3 flex-wrap">
          {/* Search */}
          <div className="relative w-full sm:max-w-xs">
            <Search size={14} className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400 pointer-events-none" />
            <Input
              type="search"
              placeholder="Search by ref, contact, staff, description..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="pl-9 pr-9 h-9"
            />
            {search && (
              <button onClick={() => setSearch("")} className="absolute right-2 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-700 p-1 rounded" type="button">
                <X size={14} />
              </button>
            )}
          </div>

          {/* Date range */}
          <div className="flex items-center gap-2">
            <Label className="text-xs text-slate-500 whitespace-nowrap">From:</Label>
            <Input type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} className="h-9 w-[140px] text-xs" />
            <Label className="text-xs text-slate-500 whitespace-nowrap">To:</Label>
            <Input type="date" value={endDate} onChange={(e) => setEndDate(e.target.value)} className="h-9 w-[140px] text-xs" />
          </div>

          {hasFilters && (
            <button onClick={clearFilters} className="text-xs text-cyan-600 hover:underline whitespace-nowrap">
              Clear all filters
            </button>
          )}
        </div>
      </div>

      {/* Results */}
      <Card>
        <CardContent className="pt-4">
          {isLoading ? (
            <div className="space-y-2">{Array.from({ length: 10 }).map((_, i) => <Skeleton key={i} className="h-12" />)}</div>
          ) : (
            <>
              <div className="overflow-x-auto">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead className="w-[80px]">Ref</TableHead>
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
                    {items.map((item, idx) => {
                      const config = TYPE_CONFIG[item.type] ?? { label: item.type, color: "bg-slate-100 text-slate-600" };
                      const isPositive = item.type === "sale" || item.type === "payment_received" || item.type === "debt_recorded";
                      const isVoided = item.type === "sale_voided" || item.type === "expense_voided";
                      return (
                        <TableRow key={`${item.id}-${item.type}-${idx}`} className={isVoided ? "opacity-60" : ""}>
                          <TableCell className="font-mono text-[10px] text-slate-400">
                            {item.refId}
                          </TableCell>
                          <TableCell>
                            <span className={`inline-flex items-center px-2 py-0.5 rounded text-[10px] font-medium ${config.color}`}>
                              {config.label}
                            </span>
                          </TableCell>
                          <TableCell className="text-sm text-slate-700 max-w-xs">
                            <span className={`block truncate ${isVoided ? "line-through" : ""}`}>{item.description}</span>
                            {item.details && (
                              <span className="block text-xs text-slate-400 truncate">{item.details}</span>
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
                          <TableCell className={`text-right font-medium ${isVoided ? "text-slate-400 line-through" : isPositive ? "text-emerald-600" : "text-red-500"}`}>
                            {item.amount != null ? (
                              <>{isPositive ? "+" : "-"}{formatNaira(item.amount)}</>
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
                    {items.length === 0 && (
                      <TableRow>
                        <TableCell colSpan={8} className="text-center py-8 text-slate-400">
                          {hasFilters ? "No activity matches your filters." : "No activity yet."}
                        </TableCell>
                      </TableRow>
                    )}
                  </TableBody>
                </Table>
              </div>

              {/* Pagination */}
              <div className="flex items-center justify-between mt-4 pt-4 border-t">
                <p className="text-xs text-slate-500">
                  {totalCount.toLocaleString()} entries · Page {page} of {totalPages}
                </p>
                <div className="flex items-center gap-1">
                  <Button variant="outline" size="sm" onClick={() => setPage(1)} disabled={page <= 1} className="text-xs px-2">
                    First
                  </Button>
                  <Button variant="outline" size="sm" onClick={() => setPage(p => p - 1)} disabled={page <= 1}>
                    <ChevronLeft size={14} />
                  </Button>

                  {/* Page numbers */}
                  {Array.from({ length: Math.min(5, totalPages) }, (_, i) => {
                    let pageNum: number;
                    if (totalPages <= 5) {
                      pageNum = i + 1;
                    } else if (page <= 3) {
                      pageNum = i + 1;
                    } else if (page >= totalPages - 2) {
                      pageNum = totalPages - 4 + i;
                    } else {
                      pageNum = page - 2 + i;
                    }
                    return (
                      <Button
                        key={pageNum}
                        variant={pageNum === page ? "default" : "outline"}
                        size="sm"
                        onClick={() => setPage(pageNum)}
                        className={`text-xs w-8 ${pageNum === page ? "bg-cyan-500 text-white" : ""}`}
                      >
                        {pageNum}
                      </Button>
                    );
                  })}

                  <Button variant="outline" size="sm" onClick={() => setPage(p => p + 1)} disabled={page >= totalPages}>
                    <ChevronRight size={14} />
                  </Button>
                  <Button variant="outline" size="sm" onClick={() => setPage(totalPages)} disabled={page >= totalPages} className="text-xs px-2">
                    Last
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

"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { formatNaira, formatDateTime } from "@/lib/format";
import { useBusiness } from "@/lib/data-sync";
import type { DashboardOverviewDto, RecentActivityDto, DashboardInsightsDto, OutstandingBalanceDto } from "@/lib/types";
import {
  AreaChart,
  Area,
  BarChart,
  Bar,
  PieChart,
  Pie,
  Cell,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  Legend,
} from "recharts";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { usePlanStatus } from "@/lib/use-plan-status";
import { UpgradePrompt } from "@/components/upgrade-prompt";
import {
  TrendingUp,
  TrendingDown,
  AlertTriangle,
  DollarSign,
  Plus,
  ShoppingCart,
  Receipt,
  Package,
  Users,
} from "lucide-react";

function KpiCard({
  title,
  value,
  sub,
  icon,
  accent,
  onClick,
  active,
  sparklineData,
  sparklineColor = "#06b6d4",
  featured = false,
}: {
  title: string;
  value: string;
  sub?: string;
  icon: React.ReactNode;
  accent?: string;
  onClick?: () => void;
  active?: boolean;
  sparklineData?: { amount: number }[];
  sparklineColor?: string;
  featured?: boolean;
}) {
  return (
    <Card
      className={`relative overflow-hidden ${
        onClick ? "cursor-pointer hover:shadow-md transition-shadow duration-150" : ""
      } ${active ? "ring-2 ring-cyan-500" : ""} ${
        featured
          ? "bg-gradient-to-br from-slate-900 to-slate-800 text-white !ring-0 shadow-md"
          : ""
      }`}
      onClick={onClick}
    >
      <CardContent className={`${featured ? "p-6" : "p-5"}`}>
        <div className="flex items-start justify-between">
          <div className="flex-1 min-w-0">
            <p className={`text-[11px] font-semibold uppercase tracking-wider ${featured ? "text-slate-400" : "text-slate-500"}`}>
              {title}
            </p>
            <p
              className={`${featured ? "text-4xl" : "text-2xl"} font-bold mt-2 tabular-nums tracking-tight ${
                featured ? "text-white" : accent ?? "text-slate-900"
              }`}
            >
              {value}
            </p>
            {sub && (
              <p className={`text-xs mt-1 ${featured ? "text-slate-400" : "text-slate-500"}`}>{sub}</p>
            )}
          </div>
          <div
            className={`p-2 rounded-lg flex-shrink-0 ml-3 ${
              featured ? "bg-white/10 text-cyan-300" : "bg-slate-100 text-slate-600"
            }`}
          >
            {icon}
          </div>
        </div>
        {sparklineData && sparklineData.length > 0 && (
          <div className={`${featured ? "mt-4 -mx-6 -mb-6 h-14" : "mt-3 -mx-5 -mb-5 h-10"}`}>
            <ResponsiveContainer width="100%" height="100%">
              <AreaChart data={sparklineData} margin={{ top: 0, right: 0, left: 0, bottom: 0 }}>
                <Area
                  type="monotone"
                  dataKey="amount"
                  stroke={sparklineColor}
                  strokeWidth={2}
                  fill={sparklineColor}
                  fillOpacity={featured ? 0.25 : 0.12}
                  isAnimationActive={false}
                />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

type Period = "today" | "week" | "month";

export default function DashboardPage() {
  const router = useRouter();
  const [expanded, setExpanded] = useState<string | null>(null);
  const [period, setPeriod] = useState<Period>("today");
  const { data: planStatus } = usePlanStatus();
  const hasCharts = planStatus?.hasMonthlyCharts ?? true;
  const biz = useBusiness();

  const currencyMeta: Record<string, string> = { NGN: "\u20A6", GHS: "GH\u20B5", USD: "$", GBP: "\u00A3", KES: "KSh", ZAR: "R", TZS: "TSh", UGX: "USh", RWF: "RF", XAF: "FCFA", XOF: "CFA", EGP: "E\u00A3", ETB: "Br" };
  const currencySymbol = currencyMeta[biz?.currency?.toUpperCase() ?? "NGN"] ?? biz?.currency ?? "\u20A6";

  const toggleExpand = (key: string) => setExpanded(expanded === key ? null : key);

  const { data: overview, isLoading: loadingOverview } = useQuery({
    queryKey: ["dashboard-overview"],
    queryFn: async () => {
      const { data } = await api.get<{ data: DashboardOverviewDto }>("/dashboard/overview");
      return data.data!;
    },
  });

  const { data: activity, isLoading: loadingActivity } = useQuery({
    queryKey: ["recent-activity"],
    queryFn: async () => {
      const { data } = await api.get<{ data: RecentActivityDto[] }>("/dashboard/recent-activity?limit=10");
      return data.data!;
    },
  });

  const { data: receivables } = useQuery({
    queryKey: ["outstanding-receivables"],
    queryFn: async () => {
      const { data } = await api.get<{ data: OutstandingBalanceDto[] }>("/ledger/balances?type=receivable");
      return data.data!;
    },
  });

  const { data: payables } = useQuery({
    queryKey: ["outstanding-payables"],
    queryFn: async () => {
      const { data } = await api.get<{ data: OutstandingBalanceDto[] }>("/ledger/balances?type=payable");
      return data.data!;
    },
  });

  const { data: insights } = useQuery({
    queryKey: ["dashboard-insights"],
    queryFn: async () => {
      const { data } = await api.get<{ data: DashboardInsightsDto }>("/dashboard/insights");
      return data.data!;
    },
    enabled: hasCharts,
  });

  if (loadingOverview) {
    return (
      <div className="space-y-6">
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-24 rounded-xl" />
          ))}
        </div>
        <Skeleton className="h-64 rounded-xl" />
      </div>
    );
  }

  const ov = overview!;

  // Period-aware values
  const weekSales = (ov.salesTrend ?? []).reduce((s, d) => s + d.amount, 0);
  const weekExpenses = (ov.expenseTrend ?? []).reduce((s, d) => s + d.amount, 0);
  const periodSales = period === "today" ? ov.todaySales : period === "week" ? weekSales : ov.monthlySales;
  const periodExpenses = period === "today" ? ov.todayExpenses : period === "week" ? weekExpenses : ov.monthlyExpenses;
  const periodNet = periodSales - periodExpenses;
  const periodSaleCount = period === "today" ? ov.todaySaleCount : null;
  const netLabel = period === "today" ? "Net Today" : period === "week" ? "Net This Week" : "Net This Month";
  const salesLabel = period === "today" ? "Today's Sales" : period === "week" ? "Sales (7 days)" : "Monthly Sales";
  const expensesLabel = period === "today" ? "Today's Expenses" : period === "week" ? "Expenses (7 days)" : "Monthly Expenses";

  // Greeting (time-aware)
  const hour = new Date().getHours();
  const greet = hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
  const businessName = biz?.name ?? "your business";

  return (
    <div className="space-y-8">
      {/* Hero greeting */}
      <div className="flex flex-col sm:flex-row sm:items-end sm:justify-between gap-4">
        <div>
          <h1 className="text-2xl sm:text-3xl font-bold text-slate-900 tracking-tight">
            {greet}.
          </h1>
          <p className="text-slate-500 text-sm mt-1">
            Here&apos;s what&apos;s happening at <span className="font-medium text-slate-700">{businessName}</span>
            {" "}— {period === "today" ? "today" : period === "week" ? "this week" : "this month"}.
          </p>
        </div>

        {/* Period selector + Quick actions */}
        <div className="flex flex-wrap items-center gap-2">
          <div className="inline-flex items-center bg-white border border-slate-200 rounded-lg p-0.5">
            {(["today", "week", "month"] as Period[]).map((p) => (
              <button
                key={p}
                onClick={() => setPeriod(p)}
                className={`px-3 py-1.5 text-xs font-medium rounded-md transition-colors capitalize ${
                  period === p ? "bg-slate-900 text-white" : "text-slate-500 hover:text-slate-900"
                }`}
              >
                {p}
              </button>
            ))}
          </div>
          <div className="hidden sm:block w-px h-5 bg-slate-200 mx-1" />
          <button
            onClick={() => router.push("/sales?new=1")}
            className="inline-flex items-center gap-1.5 px-3.5 py-2 rounded-lg bg-violet-600 hover:bg-violet-700 text-white text-sm font-medium transition-colors"
          >
            <Plus size={14} /> <ShoppingCart size={14} /> Sale
          </button>
          <button
            onClick={() => router.push("/expenses?new=1")}
            className="inline-flex items-center gap-1.5 px-3.5 py-2 rounded-lg border border-slate-200 bg-white hover:bg-slate-50 text-slate-700 text-sm font-medium transition-colors"
          >
            <Plus size={14} /> <Receipt size={14} /> Expense
          </button>
          <button
            onClick={() => router.push("/inventory?new=1")}
            className="inline-flex items-center gap-1.5 px-3.5 py-2 rounded-lg border border-slate-200 bg-white hover:bg-slate-50 text-slate-700 text-sm font-medium transition-colors"
          >
            <Plus size={14} /> <Package size={14} /> Product
          </button>
        </div>
      </div>

      {/* Payment failed banner */}
      {planStatus?.subscriptionStatus?.toLowerCase() === "past_due" && (
        <div className="flex items-center justify-between rounded-lg border border-red-200 bg-red-50 px-4 py-3">
          <p className="text-sm text-red-800">
            Your last payment failed. Resubscribe to keep your {planStatus.plan.charAt(0).toUpperCase() + planStatus.plan.slice(1)} plan features.
          </p>
          <a href="/settings" className="text-sm font-semibold text-red-700 hover:text-red-900 whitespace-nowrap ml-4">
            Fix payment &rarr;
          </a>
        </div>
      )}

      {/* Grace period banner */}
      {planStatus?.subscriptionStatus?.toLowerCase() === "grace" && (
        <div className="flex items-center justify-between rounded-lg border border-red-200 bg-red-50 px-4 py-3">
          <p className="text-sm text-red-800">
            Your {planStatus.plan.charAt(0).toUpperCase() + planStatus.plan.slice(1)} subscription has expired. Resubscribe within 3 days to keep access.
          </p>
          <a href="/settings" className="text-sm font-semibold text-red-700 hover:text-red-900 whitespace-nowrap ml-4">
            Renew now &rarr;
          </a>
        </div>
      )}

      {/* Subscription expiry banner */}
      {planStatus && !planStatus.isAutoRenew && planStatus.subscriptionEndsAt && (() => {
        const days = Math.max(0, Math.ceil((new Date(planStatus.subscriptionEndsAt!).getTime() - Date.now()) / 86400000));
        if (days > 7 || !planStatus.hasActiveSubscription) return null;
        const label = planStatus.plan.charAt(0).toUpperCase() + planStatus.plan.slice(1);
        const urgent = days <= 3;
        return (
          <div className={`flex items-center justify-between rounded-lg border px-4 py-3 ${
            urgent ? "border-red-200 bg-red-50" : "border-amber-200 bg-amber-50"
          }`}>
            <p className={`text-sm ${urgent ? "text-red-800" : "text-amber-800"}`}>
              {urgent ? "⚠️ " : ""}Your <span className="font-semibold">{label}</span> plan {days === 0 ? "expires today" : `expires in ${days} day${days !== 1 ? "s" : ""}`}.
              {urgent ? " Renew now to avoid losing access." : ""}
            </p>
            <a href="/settings" className={`text-sm font-semibold whitespace-nowrap ml-4 ${
              urgent ? "text-red-700 hover:text-red-900" : "text-amber-700 hover:text-amber-900"
            }`}>
              Renew now &rarr;
            </a>
          </div>
        );
      })()}

      {/* KPI Cards — Net Today is featured (col-span-2 on lg) */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <div className="lg:col-span-2 sm:col-span-2">
          <KpiCard
            title={netLabel}
            value={formatNaira(periodNet)}
            sub={periodNet >= 0 ? "Profit so far" : "Loss so far"}
            icon={<DollarSign size={20} />}
            featured
            sparklineColor="#06b6d4"
            sparklineData={(ov.salesTrend ?? []).map((s, i) => ({
              amount: s.amount - (ov.expenseTrend?.[i]?.amount ?? 0),
            }))}
            onClick={() => toggleExpand("net")}
            active={expanded === "net"}
          />
        </div>
        <KpiCard
          title={salesLabel}
          value={formatNaira(periodSales)}
          sub={periodSaleCount !== null ? `${periodSaleCount} transactions` : undefined}
          icon={<TrendingUp size={18} />}
          accent="text-emerald-600"
          sparklineColor="#10b981"
          sparklineData={ov.salesTrend ?? []}
          onClick={() => router.push("/sales")}
        />
        <KpiCard
          title={expensesLabel}
          value={formatNaira(periodExpenses)}
          icon={<TrendingDown size={18} />}
          accent="text-red-500"
          sparklineColor="#ef4444"
          sparklineData={ov.expenseTrend ?? []}
          onClick={() => router.push("/expenses")}
        />
        <KpiCard
          title="Receivables"
          value={formatNaira(ov.outstandingReceivables)}
          sub="Owed to you"
          icon={<TrendingUp size={18} />}
          accent="text-cyan-600"
          onClick={() => toggleExpand("receivables")}
          active={expanded === "receivables"}
        />
        <KpiCard
          title="Payables"
          value={formatNaira(ov.outstandingPayables)}
          sub="You owe"
          icon={<TrendingDown size={18} />}
          accent="text-orange-500"
          onClick={() => toggleExpand("payables")}
          active={expanded === "payables"}
        />
        <KpiCard
          title="Low Stock"
          value={`${ov.lowStockCount} items`}
          sub={ov.lowStockCount > 0 ? "Need restocking" : "All good"}
          icon={<AlertTriangle size={18} />}
          accent={ov.lowStockCount > 0 ? "text-amber-600" : "text-emerald-600"}
          onClick={() => router.push("/inventory")}
        />
      </div>

      {/* Monthly P&L Summary */}
      <Card>
        <CardContent className="p-5">
          <p className="text-xs font-medium text-slate-500 uppercase tracking-wide mb-3">
            Monthly P&L &mdash; {new Date().toLocaleDateString("en", { month: "long", year: "numeric" })}
          </p>
          <div className="space-y-2 text-sm">
            <div className="flex justify-between">
              <span className="text-slate-600">Sales</span>
              <span className="text-emerald-600 font-medium">{formatNaira(ov.monthlySales)}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-slate-600">Expenses</span>
              <span className="text-red-500 font-medium">-{formatNaira(ov.monthlyExpenses)}</span>
            </div>
            <div className="border-t pt-2 flex justify-between font-semibold">
              <span>Net Profit</span>
              <span className={ov.monthlyProfit >= 0 ? "text-emerald-600" : "text-red-500"}>
                {ov.monthlyProfit < 0 ? "-" : ""}{formatNaira(Math.abs(ov.monthlyProfit))}
              </span>
            </div>
          </div>
        </CardContent>
      </Card>

      {/* Expandable detail panels */}
      {expanded === "net" && (
        <Card className="border-cyan-200 bg-cyan-50/30">
          <CardContent className="p-4">
            <div className="space-y-2 text-sm">
              <div className="flex justify-between"><span className="text-slate-600">Sales</span><span className="text-emerald-600 font-medium">{formatNaira(ov.todaySales)}</span></div>
              <div className="flex justify-between"><span className="text-slate-600">Expenses</span><span className="text-red-500 font-medium">-{formatNaira(ov.todayExpenses)}</span></div>
              <div className="border-t pt-2 flex justify-between font-semibold"><span>Net</span><span className={ov.todaySales - ov.todayExpenses >= 0 ? "text-emerald-600" : "text-red-500"}>{formatNaira(ov.todaySales - ov.todayExpenses)}</span></div>
              <div className="flex justify-between text-xs text-slate-400"><span>Outstanding receivables</span><span>{formatNaira(ov.outstandingReceivables)}</span></div>
              <div className="flex justify-between text-xs text-slate-400"><span>Outstanding payables</span><span>-{formatNaira(ov.outstandingPayables)}</span></div>
            </div>
          </CardContent>
        </Card>
      )}

      {expanded === "receivables" && (
        <Card className="border-cyan-200 bg-cyan-50/30">
          <CardContent className="p-4">
            <p className="text-xs font-semibold text-slate-500 uppercase mb-2">Who owes you</p>
            {receivables && receivables.length > 0 ? (
              <div className="space-y-2">
                {receivables.slice(0, 10).map((r) => (
                  <div key={r.contactId} className="border-b border-cyan-100 pb-2 last:border-0">
                    <div className="flex justify-between text-sm">
                      <span className="text-slate-700 font-medium">{r.contactName}</span>
                      <span className="text-cyan-600 font-semibold">{formatNaira(r.totalReceivable)}</span>
                    </div>
                    {r.recentNotes && r.recentNotes.length > 0 && (
                      <div className="mt-0.5">
                        {r.recentNotes.map((note, i) => (
                          <p key={i} className="text-xs text-slate-500">{note}</p>
                        ))}
                      </div>
                    )}
                  </div>
                ))}
                {receivables.length > 10 && <p className="text-xs text-slate-400">+ {receivables.length - 10} more</p>}
              </div>
            ) : (
              <p className="text-sm text-slate-400">No outstanding receivables</p>
            )}
            <button onClick={() => router.push("/contacts")} className="text-xs text-cyan-600 hover:underline mt-3 block">View all contacts →</button>
          </CardContent>
        </Card>
      )}

      {expanded === "payables" && (
        <Card className="border-orange-200 bg-orange-50/30">
          <CardContent className="p-4">
            <p className="text-xs font-semibold text-slate-500 uppercase mb-2">Who you owe</p>
            {payables && payables.length > 0 ? (
              <div className="space-y-2">
                {payables.slice(0, 10).map((p) => (
                  <div key={p.contactId} className="border-b border-orange-100 pb-2 last:border-0">
                    <div className="flex justify-between text-sm">
                      <span className="text-slate-700 font-medium">{p.contactName}</span>
                      <span className="text-orange-600 font-semibold">{formatNaira(p.totalPayable)}</span>
                    </div>
                    {p.recentNotes && p.recentNotes.length > 0 && (
                      <div className="mt-0.5">
                        {p.recentNotes.map((note, i) => (
                          <p key={i} className="text-xs text-slate-500">{note}</p>
                        ))}
                      </div>
                    )}
                  </div>
                ))}
                {payables.length > 10 && <p className="text-xs text-slate-400">+ {payables.length - 10} more</p>}
              </div>
            ) : (
              <p className="text-sm text-slate-400">No outstanding payables</p>
            )}
            <button onClick={() => router.push("/contacts")} className="text-xs text-orange-600 hover:underline mt-3 block">View all contacts →</button>
          </CardContent>
        </Card>
      )}

      {/* Charts */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-semibold text-slate-700">
              7-Day Sales Trend
            </CardTitle>
          </CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={200}>
              <AreaChart data={ov.salesTrend}>
                <defs>
                  <linearGradient id="salesGrad" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor="#06b6d4" stopOpacity={0.3} />
                    <stop offset="95%" stopColor="#06b6d4" stopOpacity={0} />
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" stroke="#f1f5f9" />
                <XAxis
                  dataKey="date"
                  tick={{ fontSize: 10, fill: "#94a3b8" }}
                  tickFormatter={(v) =>
                    new Date(v).toLocaleDateString("en", { day: "numeric", month: "short" })
                  }
                />
                <YAxis
                  tick={{ fontSize: 10, fill: "#94a3b8" }}
                  tickFormatter={(v) => `${currencySymbol}${(v / 1000).toFixed(0)}k`}
                />
                <Tooltip
                  formatter={(v) => [formatNaira(Number(v)), "Sales"]}
                  labelFormatter={(l) =>
                    new Date(l).toLocaleDateString("en", { weekday: "short", day: "numeric", month: "short" })
                  }
                />
                <Area
                  type="monotone"
                  dataKey="amount"
                  stroke="#06b6d4"
                  strokeWidth={2}
                  fill="url(#salesGrad)"
                />
              </AreaChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-semibold text-slate-700">
              7-Day Expense Trend
            </CardTitle>
          </CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={200}>
              <AreaChart data={ov.expenseTrend}>
                <defs>
                  <linearGradient id="expGrad" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor="#ef4444" stopOpacity={0.3} />
                    <stop offset="95%" stopColor="#ef4444" stopOpacity={0} />
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" stroke="#f1f5f9" />
                <XAxis
                  dataKey="date"
                  tick={{ fontSize: 10, fill: "#94a3b8" }}
                  tickFormatter={(v) =>
                    new Date(v).toLocaleDateString("en", { day: "numeric", month: "short" })
                  }
                />
                <YAxis
                  tick={{ fontSize: 10, fill: "#94a3b8" }}
                  tickFormatter={(v) => `${currencySymbol}${(v / 1000).toFixed(0)}k`}
                />
                <Tooltip
                  formatter={(v) => [formatNaira(Number(v)), "Expenses"]}
                />
                <Area
                  type="monotone"
                  dataKey="amount"
                  stroke="#ef4444"
                  strokeWidth={2}
                  fill="url(#expGrad)"
                />
              </AreaChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>
      </div>

      {/* Insights */}
      {!hasCharts && (
        <UpgradePrompt feature="Insights & Charts" plan="Pro">
          <p className="text-xs text-slate-400 mt-2">Get visual breakdowns of your sales, expenses, top products, and cash flow trends.</p>
        </UpgradePrompt>
      )}
      {hasCharts && insights && (
        <>
          {/* Daily Net Cash Flow (full width) */}
          <Card>
            <CardHeader className="pb-2">
              <CardTitle className="text-sm font-semibold text-slate-700">
                Daily Net Cash Flow (Last 14 Days)
              </CardTitle>
            </CardHeader>
            <CardContent>
              <ResponsiveContainer width="100%" height={220}>
                <BarChart data={insights.dailyNet}>
                  <CartesianGrid strokeDasharray="3 3" stroke="#f1f5f9" />
                  <XAxis
                    dataKey="date"
                    tick={{ fontSize: 10, fill: "#94a3b8" }}
                    tickFormatter={(v) =>
                      new Date(v).toLocaleDateString("en", { day: "numeric", month: "short" })
                    }
                  />
                  <YAxis
                    tick={{ fontSize: 10, fill: "#94a3b8" }}
                    tickFormatter={(v) => `${currencySymbol}${(v / 1000).toFixed(0)}k`}
                  />
                  <Tooltip
                    formatter={(v) => formatNaira(Number(v))}
                    labelFormatter={(l) =>
                      new Date(l).toLocaleDateString("en", { weekday: "short", day: "numeric", month: "short" })
                    }
                  />
                  <Legend wrapperStyle={{ fontSize: 11 }} />
                  <Bar dataKey="sales" name="Sales" fill="#10b981" radius={[4, 4, 0, 0]} />
                  <Bar dataKey="expenses" name="Expenses" fill="#ef4444" radius={[4, 4, 0, 0]} />
                </BarChart>
              </ResponsiveContainer>
            </CardContent>
          </Card>

          {/* Top Products + Top Customers */}
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
            <Card>
              <CardHeader className="pb-2">
                <CardTitle className="text-sm font-semibold text-slate-700">
                  Top Products (Last 30 Days)
                </CardTitle>
              </CardHeader>
              <CardContent>
                {insights.topProducts.length === 0 ? (
                  <p className="text-sm text-slate-400 text-center py-8">No sales yet</p>
                ) : (
                  <ResponsiveContainer width="100%" height={220}>
                    <BarChart data={insights.topProducts} layout="vertical">
                      <CartesianGrid strokeDasharray="3 3" stroke="#f1f5f9" />
                      <XAxis
                        type="number"
                        tick={{ fontSize: 10, fill: "#94a3b8" }}
                        tickFormatter={(v) => `${currencySymbol}${(v / 1000).toFixed(0)}k`}
                      />
                      <YAxis
                        type="category"
                        dataKey="productName"
                        width={80}
                        tick={{ fontSize: 11, fill: "#475569" }}
                      />
                      <Tooltip formatter={(v) => formatNaira(Number(v))} />
                      <Bar dataKey="revenue" fill="#06b6d4" radius={[0, 4, 4, 0]} />
                    </BarChart>
                  </ResponsiveContainer>
                )}
              </CardContent>
            </Card>

            <Card>
              <CardHeader className="pb-2">
                <CardTitle className="text-sm font-semibold text-slate-700">
                  Top Customers (Last 30 Days)
                </CardTitle>
              </CardHeader>
              <CardContent>
                {insights.topCustomers.length === 0 ? (
                  <p className="text-sm text-slate-400 text-center py-8">No customers yet</p>
                ) : (
                  <ResponsiveContainer width="100%" height={220}>
                    <BarChart data={insights.topCustomers} layout="vertical">
                      <CartesianGrid strokeDasharray="3 3" stroke="#f1f5f9" />
                      <XAxis
                        type="number"
                        tick={{ fontSize: 10, fill: "#94a3b8" }}
                        tickFormatter={(v) => `${currencySymbol}${(v / 1000).toFixed(0)}k`}
                      />
                      <YAxis
                        type="category"
                        dataKey="contactName"
                        width={80}
                        tick={{ fontSize: 11, fill: "#475569" }}
                      />
                      <Tooltip formatter={(v) => formatNaira(Number(v))} />
                      <Bar dataKey="revenue" fill="#8b5cf6" radius={[0, 4, 4, 0]} />
                    </BarChart>
                  </ResponsiveContainer>
                )}
              </CardContent>
            </Card>
          </div>

          {/* Expense Categories + Payment Status */}
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
            <Card>
              <CardHeader className="pb-2">
                <CardTitle className="text-sm font-semibold text-slate-700">
                  Expense Breakdown (Last 30 Days)
                </CardTitle>
              </CardHeader>
              <CardContent>
                {insights.expenseCategories.length === 0 ? (
                  <p className="text-sm text-slate-400 text-center py-8">No expenses yet</p>
                ) : (() => {
                  const COLORS = ["#06b6d4", "#10b981", "#8b5cf6", "#f59e0b", "#ef4444", "#ec4899", "#6366f1", "#14b8a6", "#f97316", "#06b6d4", "#a855f7", "#84cc16"];
                  const sorted = [...insights.expenseCategories].sort((a, b) => b.amount - a.amount);
                  const total = sorted.reduce((s, c) => s + c.amount, 0);
                  return (
                    <div className="space-y-1.5 max-h-[260px] overflow-y-auto">
                      {sorted.map((c, i) => {
                        const pct = total > 0 ? (c.amount / total * 100) : 0;
                        return (
                          <div key={c.category}>
                            <div className="flex justify-between text-xs mb-0.5">
                              <div className="flex items-center gap-1.5">
                                <span className="inline-block w-2 h-2 rounded-sm flex-shrink-0" style={{ backgroundColor: COLORS[i % COLORS.length] }} />
                                <span className="text-slate-700 truncate">{c.category}</span>
                              </div>
                              <span className="text-slate-900 font-medium ml-2 flex-shrink-0">
                                {formatNaira(c.amount)} <span className="text-slate-400">({pct.toFixed(1)}%)</span>
                              </span>
                            </div>
                            <div className="h-1.5 bg-slate-100 rounded-full overflow-hidden">
                              <div className="h-full rounded-full" style={{ width: `${Math.max(1, pct)}%`, backgroundColor: COLORS[i % COLORS.length] }} />
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  );
                })()}
              </CardContent>
            </Card>

            <Card>
              <CardHeader className="pb-2">
                <CardTitle className="text-sm font-semibold text-slate-700">
                  Sales by Payment Status (Last 30 Days)
                </CardTitle>
              </CardHeader>
              <CardContent>
                {insights.paymentStatus.length === 0 ? (
                  <p className="text-sm text-slate-400 text-center py-8">No sales yet</p>
                ) : (
                  <ResponsiveContainer width="100%" height={220}>
                    <PieChart>
                      <Pie
                        data={insights.paymentStatus}
                        dataKey="amount"
                        nameKey="status"
                        cx="50%"
                        cy="50%"
                        outerRadius={75}
                        innerRadius={40}
                        paddingAngle={2}
                      >
                        {insights.paymentStatus.map((entry, i) => {
                          const color =
                            entry.status === "Paid"
                              ? "#10b981"
                              : entry.status === "Unpaid"
                              ? "#ef4444"
                              : "#f59e0b";
                          return <Cell key={i} fill={color} />;
                        })}
                      </Pie>
                      <Tooltip formatter={(v) => formatNaira(Number(v))} />
                      <Legend wrapperStyle={{ fontSize: 11 }} />
                    </PieChart>
                  </ResponsiveContainer>
                )}
              </CardContent>
            </Card>
          </div>

          {/* Receivables Aging (full width) */}
          <Card>
            <CardHeader className="pb-2">
              <CardTitle className="text-sm font-semibold text-slate-700">
                Receivables Aging — Who To Chase
              </CardTitle>
            </CardHeader>
            <CardContent>
              {insights.receivablesAging.every((b) => b.amount === 0) ? (
                <p className="text-sm text-slate-400 text-center py-8">No outstanding receivables</p>
              ) : (
                <>
                  <ResponsiveContainer width="100%" height={180}>
                    <BarChart data={insights.receivablesAging}>
                      <CartesianGrid strokeDasharray="3 3" stroke="#f1f5f9" />
                      <XAxis dataKey="bucket" tick={{ fontSize: 11, fill: "#475569" }} />
                      <YAxis
                        tick={{ fontSize: 10, fill: "#94a3b8" }}
                        tickFormatter={(v) => `${currencySymbol}${(v / 1000).toFixed(0)}k`}
                      />
                      <Tooltip formatter={(v) => formatNaira(Number(v))} />
                      <Bar dataKey="amount" radius={[4, 4, 0, 0]}>
                        {insights.receivablesAging.map((_, i) => (
                          <Cell
                            key={i}
                            fill={["#10b981", "#f59e0b", "#f97316", "#ef4444"][i]}
                          />
                        ))}
                      </Bar>
                    </BarChart>
                  </ResponsiveContainer>
                  {/* Contact names per aging bucket */}
                  <div className="mt-4 grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
                    {insights.receivablesAging.map((bucket, i) => {
                      const colors = ["border-emerald-200 bg-emerald-50/50", "border-amber-200 bg-amber-50/50", "border-orange-200 bg-orange-50/50", "border-red-200 bg-red-50/50"];
                      const textColors = ["text-emerald-700", "text-amber-700", "text-orange-700", "text-red-700"];
                      if (!bucket.contacts || bucket.contacts.length === 0) return null;
                      return (
                        <div key={bucket.bucket} className={`border rounded-lg p-3 ${colors[i]}`}>
                          <p className={`text-xs font-semibold uppercase tracking-wide mb-2 ${textColors[i]}`}>{bucket.bucket}</p>
                          <div className="space-y-1.5">
                            {bucket.contacts.map((contact, ci) => (
                              <div key={ci} className="flex justify-between text-xs">
                                <span className="text-slate-700 truncate mr-2">{contact.contactName}</span>
                                <span className={`font-medium whitespace-nowrap ${textColors[i]}`}>{formatNaira(contact.amount)}</span>
                              </div>
                            ))}
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </>
              )}
            </CardContent>
          </Card>
        </>
      )}

      {/* Recent Activity */}
      <Card>
        <CardHeader className="pb-2 flex flex-row items-center justify-between">
          <CardTitle className="text-sm font-semibold text-slate-700">
            Recent Activity
          </CardTitle>
          <button
            onClick={() => router.push("/activity")}
            className="text-xs text-cyan-600 hover:underline"
          >
            View all →
          </button>
        </CardHeader>
        <CardContent>
          {loadingActivity ? (
            <div className="space-y-3">
              {Array.from({ length: 5 }).map((_, i) => (
                <Skeleton key={i} className="h-10" />
              ))}
            </div>
          ) : (
            <div className="space-y-1">
              {(activity ?? []).map((item, i) => (
                <div
                  key={i}
                  onClick={() => router.push(item.type === "sale" ? "/sales" : "/expenses")}
                  className="flex items-center justify-between py-2.5 px-2 -mx-2 border-b border-slate-100 last:border-0 rounded-lg cursor-pointer hover:bg-slate-50 transition-colors"
                >
                  <div className="flex items-center gap-3">
                    <Badge
                      variant={item.type === "sale" ? "default" : "secondary"}
                      className="text-xs capitalize"
                    >
                      {item.type}
                    </Badge>
                    <span className="text-sm text-slate-700">{item.description}</span>
                  </div>
                  <div className="text-right">
                    {item.amount != null && (
                      <span
                        className={`text-sm font-medium ${
                          item.type === "sale" ? "text-emerald-600" : "text-red-500"
                        }`}
                      >
                        {item.type === "sale" ? "+" : "-"}
                        {formatNaira(item.amount)}
                      </span>
                    )}
                    <p className="text-xs text-slate-400">{formatDateTime(item.createdAtUtc)}</p>
                  </div>
                </div>
              ))}
              {(activity ?? []).length === 0 && (
                <p className="text-sm text-slate-400 text-center py-4">
                  No activity yet. Start by recording a sale on WhatsApp!
                </p>
              )}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

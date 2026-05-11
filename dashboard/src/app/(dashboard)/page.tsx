"use client";

import { useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { formatNaira } from "@/lib/format";
import { useBusiness } from "@/lib/data-sync";
import type {
  DashboardOverviewDto,
  DashboardInsightsDto,
  OutstandingBalanceDto,
  ProductDto,
} from "@/lib/types";
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
import { Skeleton } from "@/components/ui/skeleton";
import { usePlanStatus } from "@/lib/use-plan-status";
import { UpgradePrompt } from "@/components/upgrade-prompt";
import { useChartTheme, useTooltipStyle } from "@/lib/chart-theme";
import {
  TrendingUp,
  AlertTriangle,
  DollarSign,
  Plus,
  ShoppingCart,
  Package,
  ChevronDown,
  Wallet,
  Users,
  ArrowRight,
} from "lucide-react";

// ─────────────────────────────────────────────────────────────────────────────
// Pulse cards — small, dense, signal-only. No legends, no axes.
// `featured` renders the dark slate hero variant for the lead metric.
// ─────────────────────────────────────────────────────────────────────────────
function PulseCard({
  label,
  value,
  delta,
  tone = "neutral",
  icon,
  onClick,
  sparklineData,
  sparklineColor,
  featured = false,
}: {
  label: string;
  value: string;
  delta?: { text: string; up: boolean | null };
  tone?: "neutral" | "good" | "warn" | "bad";
  icon: React.ReactNode;
  onClick?: () => void;
  sparklineData?: { v: number }[];
  sparklineColor?: string;
  featured?: boolean;
}) {
  const lightToneClass = {
    neutral: "text-slate-900 dark:text-slate-50",
    good: "text-emerald-600",
    warn: "text-amber-600",
    bad: "text-rose-600",
  }[tone];
  // On the dark featured card, "good" and "neutral" should both read as bright white.
  const featuredToneClass = {
    neutral: "text-white",
    good: "text-white",
    warn: "text-amber-300",
    bad: "text-rose-300",
  }[tone];

  // Auto-shrink the value text so big amounts (e.g. NGN 677,176,179) fit inside
  // the card on both desktop and mobile. The card is otherwise fixed-width
  // (4-up grid on desktop, 2-up on mobile), and a 17-character value at text-2xl
  // overflows the card boundary. We pick a smaller class for longer strings.
  const valueLen = value.length;
  const valueSize = featured
    ? valueLen > 16 ? "text-2xl" : valueLen > 13 ? "text-3xl" : "text-4xl"
    : valueLen > 16 ? "text-base" : valueLen > 13 ? "text-lg" : valueLen > 10 ? "text-xl" : "text-2xl";

  if (featured) {
    return (
      <button
        type="button"
        onClick={onClick}
        className="group relative overflow-hidden text-left bg-gradient-to-br from-slate-900 to-slate-800 rounded-xl p-6 shadow-md hover:shadow-lg transition-shadow w-full h-full"
      >
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <p className="text-[11px] font-semibold uppercase tracking-wider text-slate-400 dark:text-slate-500">
              {label}
            </p>
            <p className={`${valueSize} font-bold mt-2 tabular-nums tracking-tight whitespace-nowrap ${featuredToneClass}`}>
              {value}
            </p>
            {delta && (
              <p className="text-xs mt-1.5 text-slate-400 dark:text-slate-500">
                {delta.up === true && <span className="text-emerald-400">▲ </span>}
                {delta.up === false && <span className="text-rose-400">▼ </span>}
                {delta.text}
              </p>
            )}
          </div>
          <div className="p-2 rounded-md bg-white/10 text-cyan-300 flex-shrink-0">
            {icon}
          </div>
        </div>
        {sparklineData && sparklineData.length > 1 && (
          <div className="mt-4 -mx-6 -mb-6 h-14">
            <ResponsiveContainer width="100%" height="100%">
              <AreaChart data={sparklineData} margin={{ top: 0, right: 0, left: 0, bottom: 0 }}>
                <Area
                  type="monotone"
                  dataKey="v"
                  stroke={sparklineColor ?? "#06b6d4"}
                  strokeWidth={2}
                  fill={sparklineColor ?? "#06b6d4"}
                  fillOpacity={0.25}
                  isAnimationActive={false}
                />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        )}
      </button>
    );
  }

  return (
    <button
      type="button"
      onClick={onClick}
      className="group relative overflow-hidden text-left bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 hover:border-slate-300 dark:hover:border-slate-700 hover:shadow-sm transition-all w-full h-full"
    >
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="text-[11px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">
            {label}
          </p>
          <p className={`${valueSize} font-bold mt-1.5 tabular-nums tracking-tight whitespace-nowrap ${lightToneClass}`}>
            {value}
          </p>
          {delta && (
            <p className="text-[11px] mt-1 text-slate-500 dark:text-slate-400">
              {delta.up === true && <span className="text-emerald-600">▲ </span>}
              {delta.up === false && <span className="text-rose-600">▼ </span>}
              {delta.text}
            </p>
          )}
        </div>
        <div className="p-1.5 rounded-md bg-slate-50 dark:bg-slate-950 text-slate-500 dark:text-slate-400 group-hover:bg-slate-100 dark:group-hover:bg-slate-800 transition-colors flex-shrink-0">
          {icon}
        </div>
      </div>
      {sparklineData && sparklineData.length > 1 && (
        <div className="mt-2 -mx-4 -mb-4 h-10">
          <ResponsiveContainer width="100%" height="100%">
            <AreaChart data={sparklineData} margin={{ top: 0, right: 0, left: 0, bottom: 0 }}>
              <Area
                type="monotone"
                dataKey="v"
                stroke={sparklineColor ?? "#64748b"}
                strokeWidth={1.5}
                fill={sparklineColor ?? "#64748b"}
                fillOpacity={0.1}
                isAnimationActive={false}
              />
            </AreaChart>
          </ResponsiveContainer>
        </div>
      )}
    </button>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// "What needs you" priority feed item.
// ─────────────────────────────────────────────────────────────────────────────
type PriorityItem = {
  id: string;
  severity: "high" | "med" | "low";
  icon: React.ReactNode;
  title: string;
  meta?: string;
  amount?: string;
  ctaLabel: string;
  onCta: () => void;
};

function PriorityRow({ item }: { item: PriorityItem }) {
  const severityRing = {
    high: "bg-rose-50 dark:bg-rose-950/40 text-rose-600 dark:text-rose-400 ring-rose-100 dark:ring-rose-900",
    med: "bg-amber-50 dark:bg-amber-950/40 text-amber-600 dark:text-amber-400 ring-amber-100 dark:ring-amber-900",
    low: "bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400 ring-slate-200 dark:ring-slate-700",
  }[item.severity];

  return (
    <div className="flex items-center gap-3 py-3 px-3 -mx-1 rounded-lg hover:bg-slate-50 dark:hover:bg-slate-800 transition-colors">
      <div className={`p-2 rounded-md ring-1 ${severityRing} flex-shrink-0`}>{item.icon}</div>
      <div className="flex-1 min-w-0">
        <p className="text-sm font-medium text-slate-900 dark:text-slate-50 truncate">{item.title}</p>
        {item.meta && <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5 truncate">{item.meta}</p>}
      </div>
      {item.amount && (
        <span className="text-sm font-semibold text-slate-900 dark:text-slate-50 tabular-nums whitespace-nowrap">
          {item.amount}
        </span>
      )}
      <button
        onClick={item.onCta}
        className="text-xs font-semibold text-cyan-600 hover:text-cyan-700 inline-flex items-center gap-1 whitespace-nowrap"
      >
        {item.ctaLabel}
        <ArrowRight size={12} />
      </button>
    </div>
  );
}

export default function TodayPage() {
  const router = useRouter();
  const chart = useChartTheme();
  const tooltipStyle = useTooltipStyle();
  const [showDetails, setShowDetails] = useState(false);
  const { data: planStatus } = usePlanStatus();
  const hasCharts = planStatus?.hasMonthlyCharts ?? true;
  const biz = useBusiness();

  const currencyMeta: Record<string, string> = { NGN: "₦", GHS: "GH₵", USD: "$", GBP: "£", KES: "KSh", ZAR: "R", TZS: "TSh", UGX: "USh", RWF: "RF", XAF: "FCFA", XOF: "CFA", EGP: "E£", ETB: "Br" };
  const currencySymbol = currencyMeta[biz?.currency?.toUpperCase() ?? "NGN"] ?? biz?.currency ?? "₦";

  const { data: overview, isLoading: loadingOverview } = useQuery({
    queryKey: ["dashboard-overview"],
    queryFn: async () => {
      const { data } = await api.get<{ data: DashboardOverviewDto }>("/dashboard/overview");
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

  const { data: lowStockItems } = useQuery({
    queryKey: ["low-stock-items"],
    queryFn: async () => {
      const { data } = await api.get<{ data: ProductDto[] }>("/products/low-stock");
      return data.data!;
    },
  });

  const { data: insights } = useQuery({
    queryKey: ["dashboard-insights"],
    queryFn: async () => {
      const { data } = await api.get<{ data: DashboardInsightsDto }>("/dashboard/insights");
      return data.data!;
    },
    enabled: hasCharts && showDetails, // only fetch when user expands details
  });

  // ── Build the "What needs you" feed ────────────────────────────────────────
  const priorityItems: PriorityItem[] = useMemo(() => {
    const items: PriorityItem[] = [];

    // 1. Overdue receivables — pull from receivables list, sort by amount desc, top 3
    const overdueReceivables = (receivables ?? [])
      .filter((r) => r.totalReceivable > 0)
      .sort((a, b) => b.totalReceivable - a.totalReceivable)
      .slice(0, 3);

    overdueReceivables.forEach((r) => {
      items.push({
        id: `r-${r.contactId}`,
        severity: "high",
        icon: <Users size={14} />,
        title: `${r.contactName} owes you`,
        meta: r.recentNotes?.[0],
        amount: formatNaira(r.totalReceivable),
        ctaLabel: "Remind",
        onCta: () => router.push(`/contacts?id=${r.contactId}`),
      });
    });

    // 2. Low stock items (top 3)
    const lows = (lowStockItems ?? []).slice(0, 3);
    lows.forEach((p) => {
      items.push({
        id: `s-${p.id}`,
        severity: p.currentStock === 0 ? "high" : "med",
        icon: <Package size={14} />,
        title: p.name,
        meta: p.currentStock === 0 ? "Out of stock" : `${p.currentStock} ${p.unit} left · threshold ${p.lowStockThreshold}`,
        ctaLabel: "Restock",
        onCta: () => router.push(`/inventory?focus=${p.id}`),
      });
    });

    // 3. Top payable (you owe)
    const topPayable = (payables ?? [])
      .filter((p) => p.totalPayable > 0)
      .sort((a, b) => b.totalPayable - a.totalPayable)[0];
    if (topPayable) {
      items.push({
        id: `p-${topPayable.contactId}`,
        severity: "med",
        icon: <Wallet size={14} />,
        title: `You owe ${topPayable.contactName}`,
        amount: formatNaira(topPayable.totalPayable),
        ctaLabel: "Pay",
        onCta: () => router.push(`/contacts?id=${topPayable.contactId}`),
      });
    }

    // Cap at 5 most urgent
    return items.slice(0, 5);
  }, [receivables, lowStockItems, payables, router]);

  if (loadingOverview) {
    return (
      <div className="space-y-6">
        <Skeleton className="h-12 rounded-lg" />
        <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-28 rounded-xl" />
          ))}
        </div>
        <Skeleton className="h-72 rounded-xl" />
      </div>
    );
  }

  const ov = overview!;

  // Pulse calculations
  const todayNet = ov.todaySales - ov.todayExpenses;
  const monthlyNet = ov.monthlySales - ov.monthlyExpenses;
  // Cash on hand proxy: monthly net + receivables - payables (a rough running balance)
  const cashOnHand = monthlyNet;

  // 7-day trends for sparklines (daily net)
  const netSpark = (ov.salesTrend ?? []).map((s, i) => ({
    v: s.amount - (ov.expenseTrend?.[i]?.amount ?? 0),
  }));
  const cashSpark = (ov.salesTrend ?? []).map((s, i) => {
    // running cumulative net over the 7-day window
    let running = 0;
    for (let j = 0; j <= i; j++) {
      running += (ov.salesTrend?.[j]?.amount ?? 0) - (ov.expenseTrend?.[j]?.amount ?? 0);
    }
    return { v: running };
  });

  // Greeting
  const hour = new Date().getHours();
  const greet = hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";

  return (
    <div className="space-y-6">
      {/* ─── Sticky action bar ───────────────────────────────────────────── */}
      <div className="sticky top-0 z-10 -mx-4 sm:-mx-6 lg:-mx-8 px-4 sm:px-6 lg:px-8 py-3 bg-slate-50/80 dark:bg-slate-950/80 backdrop-blur-sm border-b border-slate-200/80 dark:border-slate-800/80">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div className="min-w-0">
            <h1 className="text-lg sm:text-xl font-bold text-slate-900 dark:text-slate-50 tracking-tight truncate">
              {greet}{biz?.name ? `, ${biz.name}` : ""}
            </h1>
            <p className="text-xs text-slate-500 dark:text-slate-400 hidden sm:block">
              {ov.todaySaleCount > 0
                ? `${ov.todaySaleCount} sale${ov.todaySaleCount === 1 ? "" : "s"} today`
                : "No sales yet today"}
            </p>
          </div>
          <div className="flex items-center gap-2 flex-shrink-0">
            <button
              onClick={() => router.push("/sales?new=1")}
              className="inline-flex items-center gap-1.5 px-3 sm:px-4 py-2 rounded-lg bg-slate-900 dark:bg-slate-100 hover:bg-slate-800 dark:hover:bg-white text-white dark:text-slate-900 text-sm font-semibold transition-colors"
            >
              <Plus size={14} /> <span>Sale</span>
            </button>
            <button
              onClick={() => router.push("/expenses?new=1")}
              className="inline-flex items-center gap-1.5 px-3 sm:px-4 py-2 rounded-lg border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 text-slate-700 dark:text-slate-300 text-sm font-semibold transition-colors"
            >
              <Plus size={14} /> <span>Expense</span>
            </button>
            <button
              onClick={() => router.push("/inventory?new=1")}
              className="inline-flex items-center gap-1.5 px-3 sm:px-4 py-2 rounded-lg border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 text-slate-700 dark:text-slate-300 text-sm font-semibold transition-colors"
            >
              <Plus size={14} /> <span>Stock-in</span>
            </button>
          </div>
        </div>
      </div>

      {/* Subscription banners (kept from previous design) */}
      {planStatus?.subscriptionStatus?.toLowerCase() === "past_due" && (
        <div className="flex items-center justify-between rounded-lg border border-rose-200 bg-rose-50 px-4 py-3">
          <p className="text-sm text-rose-800">
            Your last payment failed. Resubscribe to keep your {planStatus.plan.charAt(0).toUpperCase() + planStatus.plan.slice(1)} plan features.
          </p>
          <a href="/settings" className="text-sm font-semibold text-rose-700 hover:text-rose-900 whitespace-nowrap ml-4">
            Fix payment &rarr;
          </a>
        </div>
      )}
      {planStatus?.subscriptionStatus?.toLowerCase() === "grace" && (
        <div className="flex items-center justify-between rounded-lg border border-rose-200 bg-rose-50 px-4 py-3">
          <p className="text-sm text-rose-800">
            Your {planStatus.plan.charAt(0).toUpperCase() + planStatus.plan.slice(1)} subscription has expired. Resubscribe within 3 days to keep access.
          </p>
          <a href="/settings" className="text-sm font-semibold text-rose-700 hover:text-rose-900 whitespace-nowrap ml-4">
            Renew now &rarr;
          </a>
        </div>
      )}
      {planStatus && !planStatus.isAutoRenew && planStatus.subscriptionEndsAt && (() => {
        const days = Math.max(0, Math.ceil((new Date(planStatus.subscriptionEndsAt!).getTime() - Date.now()) / 86400000));
        if (days > 7 || !planStatus.hasActiveSubscription) return null;
        const label = planStatus.plan.charAt(0).toUpperCase() + planStatus.plan.slice(1);
        const urgent = days <= 3;
        return (
          <div className={`flex items-center justify-between rounded-lg border px-4 py-3 ${
            urgent ? "border-rose-200 bg-rose-50" : "border-amber-200 bg-amber-50"
          }`}>
            <p className={`text-sm ${urgent ? "text-rose-800" : "text-amber-800"}`}>
              Your <span className="font-semibold">{label}</span> plan {days === 0 ? "expires today" : `expires in ${days} day${days !== 1 ? "s" : ""}`}.
              {urgent ? " Renew now to avoid losing access." : ""}
            </p>
            <a href="/settings" className={`text-sm font-semibold whitespace-nowrap ml-4 ${
              urgent ? "text-rose-700 hover:text-rose-900" : "text-amber-700 hover:text-amber-900"
            }`}>
              Renew now &rarr;
            </a>
          </div>
        );
      })()}

      {/* ─── Pulse strip — featured Net + 3 supporting cards ─────────────
          Mobile: featured spans full width, others stack 2x2.
          Desktop: featured 2/5, others 1/5 each.                      */}
      <div className="grid grid-cols-2 lg:grid-cols-5 gap-3">
        <div className="col-span-2">
          <PulseCard
            label="Today's Net"
            value={formatNaira(todayNet)}
            tone={todayNet >= 0 ? "good" : "bad"}
            delta={{ text: `${formatNaira(ov.todaySales)} in · ${formatNaira(ov.todayExpenses)} out`, up: null }}
            icon={<DollarSign size={20} />}
            onClick={() => router.push("/sales")}
            sparklineData={netSpark}
            sparklineColor={todayNet >= 0 ? "#10b981" : "#f43f5e"}
            featured
          />
        </div>
        <PulseCard
          label="Cash on Hand"
          value={formatNaira(cashOnHand)}
          tone={cashOnHand >= 0 ? "neutral" : "bad"}
          delta={{ text: "Month to date", up: null }}
          icon={<Wallet size={14} />}
          sparklineData={cashSpark}
          sparklineColor="#06b6d4"
        />
        <PulseCard
          label="Receivables"
          value={formatNaira(ov.outstandingReceivables)}
          tone={ov.outstandingReceivables > 0 ? "warn" : "neutral"}
          delta={{
            text: ov.outstandingReceivables > 0
              ? `${(receivables ?? []).length} contact${(receivables ?? []).length === 1 ? "" : "s"}`
              : "All settled",
            up: null,
          }}
          icon={<TrendingUp size={14} />}
          onClick={() => router.push("/contacts")}
        />
        <PulseCard
          label="Low Stock"
          value={`${ov.lowStockCount}`}
          tone={ov.lowStockCount > 0 ? "warn" : "good"}
          delta={{
            text: ov.lowStockCount > 0 ? "Need restocking" : "All good",
            up: null,
          }}
          icon={<AlertTriangle size={14} />}
          onClick={() => router.push("/inventory")}
        />
      </div>

      {/* ─── What needs you ─────────────────────────────────────────────── */}
      <Card>
        <CardHeader className="pb-2 flex flex-row items-center justify-between">
          <CardTitle className="text-sm font-semibold text-slate-900 dark:text-slate-50">
            What needs you
          </CardTitle>
          {priorityItems.length > 0 && (
            <span className="text-[11px] font-semibold uppercase tracking-wider text-slate-400 dark:text-slate-500">
              {priorityItems.length} item{priorityItems.length === 1 ? "" : "s"}
            </span>
          )}
        </CardHeader>
        <CardContent className="pt-1">
          {priorityItems.length === 0 ? (
            <div className="text-center py-10">
              <div className="inline-flex p-3 rounded-full bg-emerald-50 text-emerald-600 mb-3">
                <TrendingUp size={20} />
              </div>
              <p className="text-sm font-medium text-slate-900 dark:text-slate-50">You&rsquo;re caught up</p>
              <p className="text-xs text-slate-500 dark:text-slate-400 mt-1">No overdue debts, no low stock, no outstanding actions.</p>
            </div>
          ) : (
            <div className="divide-y divide-slate-100 dark:divide-slate-800">
              {priorityItems.map((item) => (
                <PriorityRow key={item.id} item={item} />
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      {/* ─── Detailed metrics (collapsed by default) ────────────────────── */}
      <div>
        <button
          type="button"
          onClick={() => setShowDetails((v) => !v)}
          className="w-full flex items-center justify-between px-4 py-3 rounded-lg border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 hover:bg-slate-50 dark:hover:bg-slate-800 transition-colors text-sm font-semibold text-slate-700 dark:text-slate-300"
        >
          <span className="flex items-center gap-2">
            <ShoppingCart size={14} className="text-slate-500 dark:text-slate-400" />
            View detailed metrics
          </span>
          <ChevronDown
            size={16}
            className={`text-slate-400 dark:text-slate-500 transition-transform ${showDetails ? "rotate-180" : ""}`}
          />
        </button>

        {showDetails && (
          <div className="mt-4 space-y-6">
            {/* Monthly P&L */}
            <Card>
              <CardContent className="p-5">
                <p className="text-xs font-semibold text-slate-500 dark:text-slate-400 uppercase tracking-wide mb-3">
                  Monthly P&amp;L &mdash; {new Date().toLocaleDateString("en", { month: "long", year: "numeric" })}
                </p>
                <div className="space-y-2 text-sm">
                  <div className="flex justify-between">
                    <span className="text-slate-600 dark:text-slate-400">Sales</span>
                    <span className="text-emerald-600 font-medium">{formatNaira(ov.monthlySales)}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-slate-600 dark:text-slate-400">Expenses</span>
                    <span className="text-rose-500 font-medium">-{formatNaira(ov.monthlyExpenses)}</span>
                  </div>
                  <div className="border-t pt-2 flex justify-between font-semibold">
                    <span>Net Profit</span>
                    <span className={ov.monthlyProfit >= 0 ? "text-emerald-600" : "text-rose-500"}>
                      {ov.monthlyProfit < 0 ? "-" : ""}{formatNaira(Math.abs(ov.monthlyProfit))}
                    </span>
                  </div>
                </div>
              </CardContent>
            </Card>

            {/* 7-day trends */}
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
              <Card>
                <CardHeader className="pb-2">
                  <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300">7-Day Sales Trend</CardTitle>
                </CardHeader>
                <CardContent>
                  <ResponsiveContainer width="100%" height={200}>
                    <AreaChart data={ov.salesTrend}>
                      <defs>
                        <linearGradient id="salesGrad" x1="0" y1="0" x2="0" y2="1">
                          <stop offset="5%" stopColor="#10b981" stopOpacity={0.3} />
                          <stop offset="95%" stopColor="#10b981" stopOpacity={0} />
                        </linearGradient>
                      </defs>
                      <CartesianGrid strokeDasharray="3 3" stroke={chart.grid} />
                      <XAxis dataKey="date" tick={{ fontSize: 10, fill: chart.tickMuted }}
                        tickFormatter={(v) => new Date(v).toLocaleDateString("en", { day: "numeric", month: "short" })} />
                      <YAxis tick={{ fontSize: 10, fill: chart.tickMuted }}
                        tickFormatter={(v) => `${currencySymbol}${(v / 1000).toFixed(0)}k`} />
                      <Tooltip contentStyle={tooltipStyle} formatter={(v) => [formatNaira(Number(v)), "Sales"]}
                        labelFormatter={(l) => new Date(l).toLocaleDateString("en", { weekday: "short", day: "numeric", month: "short" })} />
                      <Area type="monotone" dataKey="amount" stroke="#10b981" strokeWidth={2} fill="url(#salesGrad)" />
                    </AreaChart>
                  </ResponsiveContainer>
                </CardContent>
              </Card>

              <Card>
                <CardHeader className="pb-2">
                  <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300">7-Day Expense Trend</CardTitle>
                </CardHeader>
                <CardContent>
                  <ResponsiveContainer width="100%" height={200}>
                    <AreaChart data={ov.expenseTrend}>
                      <defs>
                        <linearGradient id="expGrad" x1="0" y1="0" x2="0" y2="1">
                          <stop offset="5%" stopColor="#f43f5e" stopOpacity={0.3} />
                          <stop offset="95%" stopColor="#f43f5e" stopOpacity={0} />
                        </linearGradient>
                      </defs>
                      <CartesianGrid strokeDasharray="3 3" stroke={chart.grid} />
                      <XAxis dataKey="date" tick={{ fontSize: 10, fill: chart.tickMuted }}
                        tickFormatter={(v) => new Date(v).toLocaleDateString("en", { day: "numeric", month: "short" })} />
                      <YAxis tick={{ fontSize: 10, fill: chart.tickMuted }}
                        tickFormatter={(v) => `${currencySymbol}${(v / 1000).toFixed(0)}k`} />
                      <Tooltip contentStyle={tooltipStyle} formatter={(v) => [formatNaira(Number(v)), "Expenses"]} />
                      <Area type="monotone" dataKey="amount" stroke="#f43f5e" strokeWidth={2} fill="url(#expGrad)" />
                    </AreaChart>
                  </ResponsiveContainer>
                </CardContent>
              </Card>
            </div>

            {/* Insights (Pro) */}
            {!hasCharts && (
              <UpgradePrompt feature="Insights & Charts" plan="Pro">
                <p className="text-xs text-slate-400 dark:text-slate-500 mt-2">Get visual breakdowns of your sales, expenses, top products, and cash flow trends.</p>
              </UpgradePrompt>
            )}
            {hasCharts && insights && (
              <>
                <Card>
                  <CardHeader className="pb-2">
                    <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300">
                      Daily Net Cash Flow (Last 14 Days)
                    </CardTitle>
                  </CardHeader>
                  <CardContent>
                    <ResponsiveContainer width="100%" height={220}>
                      <BarChart data={insights.dailyNet}>
                        <CartesianGrid strokeDasharray="3 3" stroke={chart.grid} />
                        <XAxis dataKey="date" tick={{ fontSize: 10, fill: chart.tickMuted }}
                          tickFormatter={(v) => new Date(v).toLocaleDateString("en", { day: "numeric", month: "short" })} />
                        <YAxis tick={{ fontSize: 10, fill: chart.tickMuted }}
                          tickFormatter={(v) => `${currencySymbol}${(v / 1000).toFixed(0)}k`} />
                        <Tooltip contentStyle={tooltipStyle} formatter={(v) => formatNaira(Number(v))}
                          labelFormatter={(l) => new Date(l).toLocaleDateString("en", { weekday: "short", day: "numeric", month: "short" })} />
                        <Legend wrapperStyle={{ fontSize: 11 }} />
                        <Bar dataKey="sales" name="Sales" fill="#10b981" radius={[4, 4, 0, 0]} />
                        <Bar dataKey="expenses" name="Expenses" fill="#f43f5e" radius={[4, 4, 0, 0]} />
                      </BarChart>
                    </ResponsiveContainer>
                  </CardContent>
                </Card>

                <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
                  <Card>
                    <CardHeader className="pb-2">
                      <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300">Top Products (30 Days)</CardTitle>
                    </CardHeader>
                    <CardContent>
                      {insights.topProducts.length === 0 ? (
                        <p className="text-sm text-slate-400 dark:text-slate-500 text-center py-8">No sales yet</p>
                      ) : (
                        <ResponsiveContainer width="100%" height={220}>
                          <BarChart data={insights.topProducts} layout="vertical">
                            <CartesianGrid strokeDasharray="3 3" stroke={chart.grid} />
                            <XAxis type="number" tick={{ fontSize: 10, fill: chart.tickMuted }}
                              tickFormatter={(v) => `${currencySymbol}${(v / 1000).toFixed(0)}k`} />
                            <YAxis type="category" dataKey="productName" width={80} tick={{ fontSize: 11, fill: chart.tickStrong }} />
                            <Tooltip contentStyle={tooltipStyle} formatter={(v) => formatNaira(Number(v))} />
                            <Bar dataKey="revenue" fill="#06b6d4" radius={[0, 4, 4, 0]} />
                          </BarChart>
                        </ResponsiveContainer>
                      )}
                    </CardContent>
                  </Card>

                  <Card>
                    <CardHeader className="pb-2">
                      <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300">Top Customers (30 Days)</CardTitle>
                    </CardHeader>
                    <CardContent>
                      {insights.topCustomers.length === 0 ? (
                        <p className="text-sm text-slate-400 dark:text-slate-500 text-center py-8">No customers yet</p>
                      ) : (
                        <ResponsiveContainer width="100%" height={220}>
                          <BarChart data={insights.topCustomers} layout="vertical">
                            <CartesianGrid strokeDasharray="3 3" stroke={chart.grid} />
                            <XAxis type="number" tick={{ fontSize: 10, fill: chart.tickMuted }}
                              tickFormatter={(v) => `${currencySymbol}${(v / 1000).toFixed(0)}k`} />
                            <YAxis type="category" dataKey="contactName" width={80} tick={{ fontSize: 11, fill: chart.tickStrong }} />
                            <Tooltip contentStyle={tooltipStyle} formatter={(v) => formatNaira(Number(v))} />
                            <Bar dataKey="revenue" fill="#8b5cf6" radius={[0, 4, 4, 0]} />
                          </BarChart>
                        </ResponsiveContainer>
                      )}
                    </CardContent>
                  </Card>
                </div>

                <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
                  <Card>
                    <CardHeader className="pb-2">
                      <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300">Expense Breakdown (30 Days)</CardTitle>
                    </CardHeader>
                    <CardContent>
                      {insights.expenseCategories.length === 0 ? (
                        <p className="text-sm text-slate-400 dark:text-slate-500 text-center py-8">No expenses yet</p>
                      ) : (() => {
                        const COLORS = ["#06b6d4", "#10b981", "#8b5cf6", "#f59e0b", "#f43f5e", "#ec4899", "#6366f1", "#14b8a6", "#f97316", "#06b6d4", "#a855f7", "#84cc16"];
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
                                      <span className="text-slate-700 dark:text-slate-300 truncate">{c.category}</span>
                                    </div>
                                    <span className="text-slate-900 dark:text-slate-50 font-medium ml-2 flex-shrink-0">
                                      {formatNaira(c.amount)} <span className="text-slate-400 dark:text-slate-500">({pct.toFixed(1)}%)</span>
                                    </span>
                                  </div>
                                  <div className="h-1.5 bg-slate-100 dark:bg-slate-800 rounded-full overflow-hidden">
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
                      <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300">Sales by Payment (30 Days)</CardTitle>
                    </CardHeader>
                    <CardContent>
                      {insights.paymentStatus.length === 0 ? (
                        <p className="text-sm text-slate-400 dark:text-slate-500 text-center py-8">No sales yet</p>
                      ) : (
                        <ResponsiveContainer width="100%" height={220}>
                          <PieChart>
                            <Pie data={insights.paymentStatus} dataKey="amount" nameKey="status" cx="50%" cy="50%" outerRadius={75} innerRadius={40} paddingAngle={2}>
                              {insights.paymentStatus.map((entry, i) => {
                                const color = entry.status === "Paid" ? "#10b981" : entry.status === "Unpaid" ? "#f43f5e" : "#f59e0b";
                                return <Cell key={i} fill={color} />;
                              })}
                            </Pie>
                            <Tooltip contentStyle={tooltipStyle} formatter={(v) => formatNaira(Number(v))} />
                            <Legend wrapperStyle={{ fontSize: 11 }} />
                          </PieChart>
                        </ResponsiveContainer>
                      )}
                    </CardContent>
                  </Card>
                </div>

                <Card>
                  <CardHeader className="pb-2">
                    <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300">Receivables Aging — Who To Chase</CardTitle>
                  </CardHeader>
                  <CardContent>
                    {insights.receivablesAging.every((b) => b.amount === 0) ? (
                      <p className="text-sm text-slate-400 dark:text-slate-500 text-center py-8">No outstanding receivables</p>
                    ) : (
                      <>
                        <ResponsiveContainer width="100%" height={180}>
                          <BarChart data={insights.receivablesAging}>
                            <CartesianGrid strokeDasharray="3 3" stroke={chart.grid} />
                            <XAxis dataKey="bucket" tick={{ fontSize: 11, fill: chart.tickStrong }} />
                            <YAxis tick={{ fontSize: 10, fill: chart.tickMuted }}
                              tickFormatter={(v) => `${currencySymbol}${(v / 1000).toFixed(0)}k`} />
                            <Tooltip contentStyle={tooltipStyle} formatter={(v) => formatNaira(Number(v))} />
                            <Bar dataKey="amount" radius={[4, 4, 0, 0]}>
                              {insights.receivablesAging.map((_, i) => (
                                <Cell key={i} fill={["#10b981", "#f59e0b", "#f97316", "#f43f5e"][i]} />
                              ))}
                            </Bar>
                          </BarChart>
                        </ResponsiveContainer>
                        <div className="mt-4 grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
                          {insights.receivablesAging.map((bucket, i) => {
                            const colors = ["border-emerald-200 bg-emerald-50/50", "border-amber-200 bg-amber-50/50", "border-orange-200 bg-orange-50/50", "border-rose-200 bg-rose-50/50"];
                            const textColors = ["text-emerald-700", "text-amber-700", "text-orange-700", "text-rose-700"];
                            if (!bucket.contacts || bucket.contacts.length === 0) return null;
                            return (
                              <div key={bucket.bucket} className={`border rounded-lg p-3 ${colors[i]}`}>
                                <p className={`text-xs font-semibold uppercase tracking-wide mb-2 ${textColors[i]}`}>{bucket.bucket}</p>
                                <div className="space-y-1.5">
                                  {bucket.contacts.map((contact, ci) => (
                                    <div key={ci} className="flex justify-between text-xs">
                                      <span className="text-slate-700 dark:text-slate-300 truncate mr-2">{contact.contactName}</span>
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
          </div>
        )}
      </div>
    </div>
  );
}

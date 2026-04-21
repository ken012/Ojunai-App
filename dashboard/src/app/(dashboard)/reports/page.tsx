"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { formatNaira } from "@/lib/format";
import { usePlanStatus } from "@/lib/use-plan-status";
import { useBusiness } from "@/lib/data-sync";
import { UpgradePrompt } from "@/components/upgrade-prompt";
import type {
  DailySummaryDto, WeeklySummaryDto, CashPositionDto, DeadStockItemDto,
  StockoutPredictionDto, ProductProfitDto, StaffSalesDto,
  AgingReportDto, MonthlyPnlDto, ExpenseBreakdownDto, InventoryTurnoverDto,
  TopCustomersReportDto, SalesHeatmapDto, MonthlyTrendDto, PaymentMethodSplitDto,
  CustomerReliabilityDto, WastageReportDto, AvgTransactionValueDto,
  CustomerRetentionDto, ReorderSuggestionDto, ProductAffinityDto,
  WeeklySalesTrendDto
} from "@/lib/types";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import {
  AlertTriangle, TrendingUp, TrendingDown, Clock, Target, DollarSign, Users,
  CalendarClock, PieChart, Activity, CreditCard, UserCheck, PackageX,
  Repeat, PackageCheck, Link2, FileBarChart
} from "lucide-react";
import {
  ResponsiveContainer, LineChart, Line, XAxis, YAxis, Tooltip, CartesianGrid,
  BarChart, Bar, Legend, Area, ComposedChart, ReferenceLine
} from "recharts";

// ───────── Shared bits ─────────

function StatRow({ label, value, accent }: { label: string; value: string; accent?: string }) {
  return (
    <div className="flex justify-between py-2 border-b border-slate-100 last:border-0">
      <span className="text-sm text-slate-500">{label}</span>
      <span className={`text-sm font-semibold ${accent ?? "text-slate-900"}`}>{value}</span>
    </div>
  );
}

function Delta({ current, previous }: { current: number; previous: number }) {
  if (previous === 0) return null;
  const pct = ((current - previous) / Math.abs(previous)) * 100;
  const positive = pct >= 0;
  return (
    <span className={`text-xs ${positive ? "text-emerald-600" : "text-red-500"}`}>
      {positive ? "▲" : "▼"} {Math.abs(pct).toFixed(1)}%
    </span>
  );
}

const DAY_LABELS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

// ───────── Main page ─────────

export default function ReportsPage() {
  const { data: planStatus } = usePlanStatus();
  const hasAdvanced = planStatus?.hasAdvancedReports ?? true;
  const biz = useBusiness();

  const currencyMeta: Record<string, string> = { NGN: "\u20A6", GHS: "GH\u20B5", USD: "$", GBP: "\u00A3", KES: "KSh", ZAR: "R", TZS: "TSh", UGX: "USh", RWF: "RF", XAF: "FCFA", XOF: "CFA", EGP: "E\u00A3", ETB: "Br" };
  const currencySymbol = currencyMeta[biz?.currency?.toUpperCase() ?? "NGN"] ?? biz?.currency ?? "\u20A6";

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-2xl font-bold text-slate-900">Reports</h2>
        <p className="text-slate-500 text-sm mt-0.5">Daily summaries plus deep financial, customer, and inventory insights</p>
      </div>

      <Tabs defaultValue="overview" className="space-y-4">
        <TabsList className="flex-wrap self-start">
          <TabsTrigger value="overview">Overview</TabsTrigger>
          <TabsTrigger value="financial">Financial</TabsTrigger>
          <TabsTrigger value="customers">Customers</TabsTrigger>
          <TabsTrigger value="inventory">Inventory</TabsTrigger>
          <TabsTrigger value="debts">Debts</TabsTrigger>
        </TabsList>

        <TabsContent value="overview"><OverviewTab hasAdvanced={hasAdvanced} /></TabsContent>
        <TabsContent value="financial"><FinancialTab hasAdvanced={hasAdvanced} currencySymbol={currencySymbol} /></TabsContent>
        <TabsContent value="customers"><CustomersTab hasAdvanced={hasAdvanced} /></TabsContent>
        <TabsContent value="inventory"><InventoryTab hasAdvanced={hasAdvanced} /></TabsContent>
        <TabsContent value="debts"><DebtsTab hasAdvanced={hasAdvanced} /></TabsContent>
      </Tabs>
    </div>
  );
}

// ───────── Overview tab (existing reports, all roles) ─────────

// eslint-disable-next-line @typescript-eslint/no-unused-vars
function OverviewTab({ hasAdvanced }: { hasAdvanced: boolean }) {
  const { data: daily, isLoading: loadingDaily } = useQuery({
    queryKey: ["report-daily"],
    queryFn: async () => (await api.get<{ data: DailySummaryDto }>("/reports/daily")).data.data!,
  });
  const { data: weekly, isLoading: loadingWeekly } = useQuery({
    queryKey: ["report-weekly"],
    queryFn: async () => (await api.get<{ data: WeeklySummaryDto }>("/reports/weekly")).data.data!,
  });
  const { data: cashPos, isLoading: loadingCash } = useQuery({
    queryKey: ["cash-position"],
    queryFn: async () => (await api.get<{ data: CashPositionDto }>("/reports/cash-position")).data.data!,
  });
  const { data: deadStock } = useQuery({
    queryKey: ["dead-stock"],
    queryFn: async () => (await api.get<{ data: DeadStockItemDto[] }>("/reports/dead-stock")).data.data!,
  });
  const { data: predictions } = useQuery({
    queryKey: ["stockout-predictions"],
    queryFn: async () => (await api.get<{ data: StockoutPredictionDto[] }>("/reports/stockout-predictions")).data.data!,
  });
  const { data: profitData } = useQuery({
    queryKey: ["profit-by-product"],
    queryFn: async () => (await api.get<{ data: ProductProfitDto[] }>("/reports/profit-by-product")).data.data!,
  });
  const { data: staffSales } = useQuery({
    queryKey: ["staff-sales"],
    queryFn: async () => (await api.get<{ data: StaffSalesDto[] }>("/reports/staff-sales")).data.data!,
  });

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
              <TrendingUp size={15} className="text-emerald-500" />
              Today&apos;s Summary
            </CardTitle>
          </CardHeader>
          <CardContent>
            {loadingDaily ? (
              <div className="space-y-2">{Array.from({ length: 5 }).map((_, i) => <Skeleton key={i} className="h-8" />)}</div>
            ) : daily ? (
              <>
                <StatRow label="Total Sales" value={formatNaira(daily.totalSales)} accent="text-emerald-600" />
                <StatRow label="Transactions" value={`${daily.saleCount}`} />
                <StatRow label="Total Expenses" value={formatNaira(daily.totalExpenses)} accent="text-red-500" />
                <StatRow label="Net Cash In" value={formatNaira(daily.netCashIn)} accent={daily.netCashIn >= 0 ? "text-emerald-600" : "text-red-500"} />
                <StatRow label="Receivables" value={formatNaira(daily.outstandingReceivables)} accent="text-sky-600" />
                <StatRow label="Payables" value={formatNaira(daily.outstandingPayables)} accent="text-orange-500" />
                {daily.lowStockCount > 0 && (
                  <div className="mt-3 flex items-center gap-2 text-amber-600 text-xs">
                    <AlertTriangle size={12} />
                    {daily.lowStockCount} item{daily.lowStockCount !== 1 ? "s" : ""} low on stock
                  </div>
                )}
              </>
            ) : null}
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
              <TrendingUp size={15} className="text-sky-500" />
              This Week
            </CardTitle>
          </CardHeader>
          <CardContent>
            {loadingWeekly ? <div className="space-y-2">{Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-8" />)}</div>
              : weekly ? (
                <>
                  <p className="text-xs text-slate-400 mb-3">{weekly.weekStart} — {weekly.weekEnd}</p>
                  <StatRow label="Total Sales" value={formatNaira(weekly.totalSales)} accent="text-emerald-600" />
                  <StatRow label="Total Expenses" value={formatNaira(weekly.totalExpenses)} accent="text-red-500" />
                  <StatRow label={weekly.isProfitEstimate ? "Est. Profit*" : "Est. Profit"} value={formatNaira(weekly.estimatedProfit)} accent={weekly.estimatedProfit >= 0 ? "text-emerald-600" : "text-red-500"} />
                  {weekly.topProducts.length > 0 && (
                    <div className="mt-3">
                      <p className="text-xs text-slate-400 mb-1">Top Products</p>
                      {weekly.topProducts.slice(0, 3).map((p) => (
                        <div key={p.productId} className="flex justify-between text-xs py-1">
                          <span className="text-slate-600 truncate">{p.productName}</span>
                          <span className="text-slate-900 font-medium ml-2 flex-shrink-0">{formatNaira(p.totalRevenue)}</span>
                        </div>
                      ))}
                    </div>
                  )}
                  {weekly.isProfitEstimate && <p className="text-xs text-slate-400 mt-2">*Add cost prices for accuracy</p>}
                </>
              ) : null}
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
              <TrendingDown size={15} className="text-purple-500" />
              Month Cash Position
            </CardTitle>
          </CardHeader>
          <CardContent>
            {loadingCash ? <div className="space-y-2">{Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-8" />)}</div>
              : cashPos ? (
                <>
                  <StatRow label="Sales (MTD)" value={formatNaira(cashPos.totalSalesThisMonth)} accent="text-emerald-600" />
                  <StatRow label="Expenses (MTD)" value={formatNaira(cashPos.totalExpensesThisMonth)} accent="text-red-500" />
                  <StatRow label="Est. Cash In" value={formatNaira(cashPos.estimatedCashIn)} />
                  <StatRow label="Receivables" value={formatNaira(cashPos.outstandingReceivables)} accent="text-sky-600" />
                  <StatRow label="Payables" value={formatNaira(cashPos.outstandingPayables)} accent="text-orange-500" />
                  <div className="mt-3 pt-3 border-t">
                    <div className="flex justify-between">
                      <span className="text-sm font-bold text-slate-700">Net Position</span>
                      <span className={`text-sm font-bold ${cashPos.netPosition >= 0 ? "text-emerald-600" : "text-red-500"}`}>{formatNaira(cashPos.netPosition)}</span>
                    </div>
                  </div>
                </>
              ) : null}
          </CardContent>
        </Card>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
              <Target size={15} className="text-red-500" />
              Stockout Predictions
            </CardTitle>
          </CardHeader>
          <CardContent>
            {predictions && predictions.length > 0 ? (
              <div className="space-y-2">
                {predictions.map((p) => (
                  <div key={p.productId} className="flex items-center justify-between border rounded-lg px-3 py-2">
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2">
                        <span className={`text-xs ${p.urgency === "critical" ? "text-red-500" : p.urgency === "warning" ? "text-amber-500" : "text-green-500"}`}>●</span>
                        <p className="text-sm font-medium text-slate-900 truncate">{p.productName}</p>
                      </div>
                      <p className="text-xs text-slate-500 ml-5">{p.currentStock} {p.unit} left — ~{p.daysLeft} days at {p.dailyRate}/{p.unit} per day</p>
                    </div>
                    {p.restockQty > 0 && (<Badge variant="outline" className="text-xs ml-2 shrink-0">Restock {p.restockQty} {p.unit}</Badge>)}
                  </div>
                ))}
              </div>
            ) : <p className="text-xs text-slate-400 italic py-4 text-center">All products have 2+ weeks of stock based on current sales.</p>}
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
              <Clock size={15} className="text-slate-400" />
              Dead Stock
              {deadStock && deadStock.length > 0 && <Badge variant="secondary" className="text-xs">{deadStock.length}</Badge>}
            </CardTitle>
          </CardHeader>
          <CardContent>
            {deadStock && deadStock.length > 0 ? (
              <div className="space-y-2">
                {deadStock.map((p) => (
                  <div key={p.productId} className="flex items-center justify-between border rounded-lg px-3 py-2">
                    <div className="flex-1 min-w-0">
                      <p className="text-sm font-medium text-slate-900 truncate">{p.productName}</p>
                      <p className="text-xs text-slate-500">{p.currentStock} {p.unit} in stock — {p.daysSinceLastSale >= 0 ? `last sold ${p.daysSinceLastSale} days ago` : "never sold"}</p>
                    </div>
                  </div>
                ))}
                <p className="text-xs text-slate-400 pt-1">Products with stock but no sales in 14+ days. Consider discounting or returning.</p>
              </div>
            ) : <p className="text-xs text-slate-400 italic py-4 text-center">No dead stock. All products with stock have sold recently.</p>}
          </CardContent>
        </Card>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
              <DollarSign size={15} className="text-emerald-500" />
              Profit by Product (30 Days)
            </CardTitle>
          </CardHeader>
          <CardContent>
            {profitData && profitData.length > 0 ? (
              <div className="space-y-2">
                {profitData.map((p) => (
                  <div key={p.productName} className="flex items-center justify-between border rounded-lg px-3 py-2">
                    <div className="flex-1 min-w-0">
                      <p className="text-sm font-medium text-slate-900 truncate">{p.productName}</p>
                      <p className="text-xs text-slate-500">Revenue: {formatNaira(p.revenue)} — Cost: {formatNaira(p.cost)}</p>
                    </div>
                    <div className="text-right ml-2 shrink-0">
                      <p className={`text-sm font-semibold ${p.profit >= 0 ? "text-emerald-600" : "text-red-500"}`}>{p.profit >= 0 ? "+" : ""}{formatNaira(p.profit)}</p>
                      <p className="text-xs text-slate-400">{p.margin}% margin</p>
                    </div>
                  </div>
                ))}
              </div>
            ) : <p className="text-xs text-slate-400 italic py-4 text-center">No profit data yet. Add cost prices to your products to see profitability.</p>}
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
              <Users size={15} className="text-sky-500" />
              Staff Sales Today
            </CardTitle>
          </CardHeader>
          <CardContent>
            {staffSales && staffSales.length > 0 ? (
              <div className="space-y-3">
                {staffSales.map((s) => (
                  <div key={s.staffName} className="border rounded-lg px-3 py-2">
                    <div className="flex items-center justify-between mb-1">
                      <p className="text-sm font-semibold text-slate-900">{s.staffName}</p>
                      <p className="text-sm font-semibold text-emerald-600">{formatNaira(s.totalRevenue)}</p>
                    </div>
                    <p className="text-xs text-slate-400 mb-1">{s.saleCount} sale{s.saleCount !== 1 ? "s" : ""}</p>
                    {s.items.slice(0, 5).map((item) => (
                      <div key={item.productName} className="flex justify-between text-xs py-0.5">
                        <span className="text-slate-600 truncate">{item.quantity} {item.unit} {item.productName}</span>
                        <span className="text-slate-900 font-medium ml-2 shrink-0">{formatNaira(item.revenue)}</span>
                      </div>
                    ))}
                  </div>
                ))}
              </div>
            ) : <p className="text-xs text-slate-400 italic py-4 text-center">No staff sales recorded today.</p>}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

// ───────── Financial tab ─────────

function FinancialTab({ hasAdvanced, currencySymbol }: { hasAdvanced: boolean; currencySymbol: string }) {
  const { data: pnl } = useQuery({
    queryKey: ["monthly-pnl"],
    queryFn: async () => (await api.get<{ data: MonthlyPnlDto }>("/reports/monthly-pnl")).data.data!,
    enabled: hasAdvanced,
  });
  const { data: expenses } = useQuery({
    queryKey: ["expense-breakdown"],
    queryFn: async () => (await api.get<{ data: ExpenseBreakdownDto }>("/reports/expense-breakdown")).data.data!,
    enabled: hasAdvanced,
  });
  const { data: trend } = useQuery({
    queryKey: ["monthly-trend"],
    queryFn: async () => (await api.get<{ data: MonthlyTrendDto }>("/reports/monthly-trend?months=12")).data.data!,
    enabled: hasAdvanced,
  });
  const { data: avgTxn } = useQuery({
    queryKey: ["avg-transaction-value"],
    queryFn: async () => (await api.get<{ data: AvgTransactionValueDto }>("/reports/avg-transaction-value?months=12")).data.data!,
    enabled: hasAdvanced,
  });
  const { data: payments } = useQuery({
    queryKey: ["payment-methods"],
    queryFn: async () => (await api.get<{ data: PaymentMethodSplitDto }>("/reports/payment-method-split?months=6")).data.data!,
    enabled: hasAdvanced,
  });
  const { data: heatmap } = useQuery({
    queryKey: ["sales-heatmap"],
    queryFn: async () => (await api.get<{ data: SalesHeatmapDto }>("/reports/sales-heatmap?weeks=12")).data.data!,
    enabled: hasAdvanced,
  });
  const [weeklyMonths, setWeeklyMonths] = useState(6);
  const { data: weeklyTrend } = useQuery({
    queryKey: ["weekly-sales-trend", weeklyMonths],
    queryFn: async () => (await api.get<{ data: WeeklySalesTrendDto }>(`/reports/weekly-sales-trend?months=${weeklyMonths}`)).data.data!,
    enabled: hasAdvanced,
  });

  if (!hasAdvanced) return <UpgradePrompt feature="Financial Reports" plan="Shop" />;

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        {/* Monthly P&L */}
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
              <FileBarChart size={15} className="text-emerald-500" />
              Monthly P&amp;L
            </CardTitle>
          </CardHeader>
          <CardContent>
            {pnl ? (
              <>
                <p className="text-xs text-slate-400 mb-3">{new Date(pnl.month).toLocaleString("en", { month: "long", year: "numeric" })}</p>
                <div className="space-y-1">
                  <div className="flex justify-between items-center py-2 border-b border-slate-100">
                    <span className="text-sm text-slate-500">Revenue</span>
                    <div className="text-right">
                      <span className="text-sm font-semibold text-emerald-600">{formatNaira(pnl.revenue)}</span>
                      <div className="mt-0.5"><Delta current={pnl.revenue} previous={pnl.previousRevenue} /></div>
                    </div>
                  </div>
                  <StatRow label="Cost of Goods Sold" value={`(${formatNaira(pnl.costOfGoodsSold)})`} accent="text-red-400" />
                  <div className="flex justify-between items-center py-2 border-b border-slate-100 bg-slate-50 -mx-4 px-4">
                    <span className="text-sm font-medium text-slate-700">Gross Profit</span>
                    <span className="text-sm font-semibold text-slate-900">{formatNaira(pnl.grossProfit)} <span className="text-xs text-slate-400">({pnl.grossMarginPercent}%)</span></span>
                  </div>
                  <StatRow label="Operating Expenses" value={`(${formatNaira(pnl.operatingExpenses)})`} accent="text-red-400" />
                  <div className="flex justify-between items-center py-3 bg-emerald-50 -mx-4 px-4 mt-2 rounded">
                    <span className="text-sm font-bold text-slate-800">Net Profit</span>
                    <div className="text-right">
                      <span className={`text-base font-bold ${pnl.netProfit >= 0 ? "text-emerald-600" : "text-red-500"}`}>{formatNaira(pnl.netProfit)}</span>
                      <div className="text-xs text-slate-400">{pnl.netMarginPercent}% margin</div>
                    </div>
                  </div>
                </div>
                {pnl.isEstimate && <p className="text-xs text-slate-400 mt-3">* COGS estimated from current product cost prices.</p>}
              </>
            ) : <Skeleton className="h-40" />}
          </CardContent>
        </Card>

        {/* Expense Breakdown */}
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
              <PieChart size={15} className="text-purple-500" />
              Expense Breakdown
            </CardTitle>
          </CardHeader>
          <CardContent>
            {expenses ? (
              <>
                <p className="text-xs text-slate-400 mb-3">Total: {formatNaira(expenses.totalExpenses)}</p>
                {expenses.categories.length > 0 ? (
                  <div className="space-y-2">
                    {expenses.categories.slice(0, 8).map((c, i) => {
                      const colors = [
                        "bg-sky-500", "bg-emerald-500", "bg-violet-500", "bg-amber-500",
                        "bg-rose-500", "bg-teal-500", "bg-orange-500", "bg-indigo-500"
                      ];
                      return (
                        <div key={c.category}>
                          <div className="flex justify-between text-xs mb-1">
                            <div className="flex items-center gap-1.5">
                              <span className={`inline-block w-2.5 h-2.5 rounded-sm ${colors[i % colors.length]}`} />
                              <span className="text-slate-600">{c.category}</span>
                            </div>
                            <span className="text-slate-900 font-medium">{formatNaira(c.amount)} <span className="text-slate-400">({c.percentOfTotal}%)</span></span>
                          </div>
                          <div className="h-1.5 bg-slate-100 rounded-full overflow-hidden">
                            <div className={`h-full ${colors[i % colors.length]}`} style={{ width: `${Math.min(100, Number(c.percentOfTotal))}%` }} />
                          </div>
                        </div>
                      );
                    })}
                  </div>
                ) : <p className="text-xs text-slate-400 italic">No expenses recorded this month.</p>}
              </>
            ) : <Skeleton className="h-40" />}
          </CardContent>
        </Card>
      </div>

      {/* Monthly trend line chart */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
            <Activity size={15} className="text-sky-500" />
            12-Month Trend
          </CardTitle>
        </CardHeader>
        <CardContent>
          {trend && trend.points.length > 0 ? (
            <ResponsiveContainer width="100%" height={260}>
              <LineChart data={trend.points.map(p => ({
                month: new Date(p.month).toLocaleString("en", { month: "short", year: "2-digit" }),
                Revenue: p.revenue,
                Expenses: p.expenses,
                Profit: p.profit
              }))}>
                <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
                <XAxis dataKey="month" tick={{ fontSize: 11 }} />
                <YAxis tick={{ fontSize: 11 }} tickFormatter={(v) => `${currencySymbol}${(v / 1000).toFixed(0)}k`} />
                <Tooltip formatter={(v) => formatNaira(Number(v))} />
                <Legend wrapperStyle={{ fontSize: 12 }} />
                <Line type="monotone" dataKey="Revenue" stroke="#10b981" strokeWidth={2} dot={false} />
                <Line type="monotone" dataKey="Expenses" stroke="#ef4444" strokeWidth={2} dot={false} />
                <Line type="monotone" dataKey="Profit" stroke="#0ea5e9" strokeWidth={2} dot={false} />
              </LineChart>
            </ResponsiveContainer>
          ) : <Skeleton className="h-60" />}
        </CardContent>
      </Card>

      {/* Weekly Sales Velocity */}
      <Card>
        <CardHeader className="pb-2">
          <div className="flex items-center justify-between">
            <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
              <CalendarClock size={15} className="text-violet-500" />
              Weekly Sales Velocity
            </CardTitle>
            <select
              className="h-7 px-2 rounded border border-slate-200 text-xs"
              value={weeklyMonths}
              onChange={(e) => setWeeklyMonths(Number(e.target.value))}
            >
              <option value={3}>3 months</option>
              <option value={6}>6 months</option>
              <option value={9}>9 months</option>
              <option value={12}>12 months</option>
            </select>
          </div>
        </CardHeader>
        <CardContent>
          {weeklyTrend && weeklyTrend.weeks.length > 0 ? (
            <>
              {/* Summary cards */}
              <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 mb-4">
                <div className="bg-slate-50 rounded-lg p-3 text-center">
                  <p className="text-xs text-slate-400">Avg / Week</p>
                  <p className="text-sm font-bold text-slate-900">{formatNaira(weeklyTrend.avgWeeklyRevenue)}</p>
                </div>
                <div className="bg-emerald-50 rounded-lg p-3 text-center">
                  <p className="text-xs text-emerald-500">Best Week</p>
                  <p className="text-sm font-bold text-emerald-700">{formatNaira(weeklyTrend.bestWeekRevenue)}</p>
                  <p className="text-[10px] text-emerald-500">{weeklyTrend.bestWeekLabel}</p>
                </div>
                <div className="bg-amber-50 rounded-lg p-3 text-center">
                  <p className="text-xs text-amber-500">Slowest Week</p>
                  <p className="text-sm font-bold text-amber-700">{formatNaira(weeklyTrend.worstWeekRevenue)}</p>
                  <p className="text-[10px] text-amber-500">{weeklyTrend.worstWeekLabel}</p>
                </div>
                <div className="bg-sky-50 rounded-lg p-3 text-center">
                  <p className="text-xs text-sky-500">Avg Growth</p>
                  <p className={`text-sm font-bold ${weeklyTrend.avgGrowthPercent >= 0 ? "text-emerald-700" : "text-red-600"}`}>
                    {weeklyTrend.avgGrowthPercent >= 0 ? "+" : ""}{weeklyTrend.avgGrowthPercent}%
                  </p>
                  <p className="text-[10px] text-sky-500">week-over-week</p>
                </div>
              </div>

              {/* Chart */}
              <ResponsiveContainer width="100%" height={300}>
                <ComposedChart data={weeklyTrend.weeks.map(w => ({
                  label: w.label,
                  Revenue: w.revenue,
                  "4-wk Avg": w.movingAvg,
                  Sales: w.saleCount,
                  Growth: w.growthPercent
                }))}>
                  <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
                  <XAxis dataKey="label" tick={{ fontSize: 9 }} angle={-35} textAnchor="end" height={55} interval={Math.max(0, Math.floor(weeklyTrend.weeks.length / 12))} />
                  <YAxis tick={{ fontSize: 11 }} tickFormatter={(v) => `${currencySymbol}${(v / 1000).toFixed(0)}k`} />
                  <Tooltip formatter={(v, name) => name === "Sales" ? v : formatNaira(Number(v))} labelStyle={{ fontSize: 12, fontWeight: 600 }} />
                  <Legend wrapperStyle={{ fontSize: 11 }} />
                  <ReferenceLine y={weeklyTrend.avgWeeklyRevenue} stroke="#94a3b8" strokeDasharray="6 3" label={{ value: "Avg", position: "right", fontSize: 10, fill: "#94a3b8" }} />
                  <Area type="monotone" dataKey="Revenue" fill="#dbeafe" stroke="#3b82f6" strokeWidth={2} fillOpacity={0.4} />
                  <Line type="monotone" dataKey="4-wk Avg" stroke="#f59e0b" strokeWidth={2} dot={false} strokeDasharray="5 3" />
                </ComposedChart>
              </ResponsiveContainer>

              {/* Weekly breakdown table */}
              <details className="mt-4">
                <summary className="text-xs text-slate-500 cursor-pointer hover:text-slate-700">
                  View weekly breakdown ({weeklyTrend.totalWeeks} weeks, {weeklyTrend.totalSales} sales, {formatNaira(weeklyTrend.totalRevenue)} total)
                </summary>
                <div className="mt-2 max-h-64 overflow-y-auto">
                  <table className="w-full text-xs">
                    <thead className="text-slate-400 border-b">
                      <tr>
                        <th className="text-left py-1 font-medium">Week</th>
                        <th className="text-right py-1 font-medium">Revenue</th>
                        <th className="text-right py-1 font-medium">Sales</th>
                        <th className="text-right py-1 font-medium">Avg Order</th>
                        <th className="text-right py-1 font-medium">Growth</th>
                      </tr>
                    </thead>
                    <tbody>
                      {[...weeklyTrend.weeks].reverse().map((w) => (
                        <tr key={w.weekStart} className="border-b border-slate-50 hover:bg-slate-50">
                          <td className="py-1.5 text-slate-700">{w.label}</td>
                          <td className="py-1.5 text-right font-medium text-slate-900">{formatNaira(w.revenue)}</td>
                          <td className="py-1.5 text-right text-slate-500">{w.saleCount}</td>
                          <td className="py-1.5 text-right text-slate-500">{formatNaira(w.avgOrderValue)}</td>
                          <td className={`py-1.5 text-right font-medium ${w.growthPercent == null ? "text-slate-300" : w.growthPercent >= 0 ? "text-emerald-600" : "text-red-500"}`}>
                            {w.growthPercent != null ? `${w.growthPercent >= 0 ? "+" : ""}${w.growthPercent}%` : "—"}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </details>
            </>
          ) : <Skeleton className="h-80" />}
        </CardContent>
      </Card>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        {/* Avg Transaction Value */}
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
              <TrendingUp size={15} className="text-emerald-500" />
              Average Transaction Value
            </CardTitle>
          </CardHeader>
          <CardContent>
            {avgTxn && avgTxn.points.length > 0 ? (
              <ResponsiveContainer width="100%" height={200}>
                <LineChart data={avgTxn.points.map(p => ({
                  month: new Date(p.month).toLocaleString("en", { month: "short" }),
                  Average: p.averageValue
                }))}>
                  <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
                  <XAxis dataKey="month" tick={{ fontSize: 11 }} />
                  <YAxis tick={{ fontSize: 11 }} tickFormatter={(v) => `${currencySymbol}${(v / 1000).toFixed(0)}k`} />
                  <Tooltip formatter={(v) => formatNaira(Number(v))} />
                  <Line type="monotone" dataKey="Average" stroke="#10b981" strokeWidth={2} />
                </LineChart>
              </ResponsiveContainer>
            ) : <Skeleton className="h-48" />}
          </CardContent>
        </Card>

        {/* Payment Methods */}
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
              <CreditCard size={15} className="text-sky-500" />
              Payment Methods (6 months)
            </CardTitle>
          </CardHeader>
          <CardContent>
            {payments && payments.months.length > 0 ? (
              <ResponsiveContainer width="100%" height={200}>
                <BarChart data={payments.months.map(m => ({
                  month: new Date(m.month).toLocaleString("en", { month: "short" }),
                  Cash: m.cash, Transfer: m.transfer, POS: m.pos, Credit: m.credit, Other: m.other
                }))}>
                  <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
                  <XAxis dataKey="month" tick={{ fontSize: 11 }} />
                  <YAxis tick={{ fontSize: 11 }} tickFormatter={(v) => `${currencySymbol}${(v / 1000).toFixed(0)}k`} />
                  <Tooltip formatter={(v) => formatNaira(Number(v))} />
                  <Legend wrapperStyle={{ fontSize: 10 }} />
                  <Bar dataKey="Cash" stackId="a" fill="#10b981" />
                  <Bar dataKey="Transfer" stackId="a" fill="#0ea5e9" />
                  <Bar dataKey="POS" stackId="a" fill="#8b5cf6" />
                  <Bar dataKey="Credit" stackId="a" fill="#f59e0b" />
                  <Bar dataKey="Other" stackId="a" fill="#94a3b8" />
                </BarChart>
              </ResponsiveContainer>
            ) : <Skeleton className="h-48" />}
          </CardContent>
        </Card>
      </div>

      {/* Sales Heatmap */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
            <Activity size={15} className="text-orange-500" />
            Sales Heatmap — Peak hour/day
          </CardTitle>
        </CardHeader>
        <CardContent>
          {heatmap ? <Heatmap data={heatmap} /> : <Skeleton className="h-40" />}
        </CardContent>
      </Card>
    </div>
  );
}

function Heatmap({ data }: { data: SalesHeatmapDto }) {
  const grid = new Map<string, number>();
  let max = 0;
  for (const c of data.cells) {
    grid.set(`${c.dayOfWeek}-${c.hour}`, c.revenue);
    if (c.revenue > max) max = c.revenue;
  }

  return (
    <div>
      <p className="text-xs text-slate-400 mb-3">
        Past {data.weeksAnalyzed} weeks. Peak: {DAY_LABELS[data.peakDayOfWeek]} {data.peakHour}:00 ({formatNaira(data.peakRevenue)})
      </p>
      <div className="overflow-x-auto">
        <div className="inline-block">
          <div className="flex">
            <div className="w-10" />
            {Array.from({ length: 24 }, (_, h) => (
              <div key={h} className="w-5 text-[9px] text-slate-400 text-center">{h % 3 === 0 ? h : ""}</div>
            ))}
          </div>
          {DAY_LABELS.map((day, d) => (
            <div key={day} className="flex items-center">
              <div className="w-10 text-[10px] text-slate-500 pr-2 text-right">{day}</div>
              {Array.from({ length: 24 }, (_, h) => {
                const v = grid.get(`${d}-${h}`) ?? 0;
                const intensity = max > 0 ? v / max : 0;
                const bg = intensity === 0 ? "bg-slate-50"
                  : intensity < 0.25 ? "bg-sky-100"
                  : intensity < 0.5 ? "bg-sky-300"
                  : intensity < 0.75 ? "bg-sky-500"
                  : "bg-sky-700";
                return <div key={h} className={`w-5 h-5 ${bg} border border-white`} title={v > 0 ? `${day} ${h}:00 — ${formatNaira(v)}` : ""} />;
              })}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

// ───────── Customers tab ─────────

function CustomersTab({ hasAdvanced }: { hasAdvanced: boolean }) {
  const { data: aging } = useQuery({
    queryKey: ["aging-receivables"],
    queryFn: async () => (await api.get<{ data: AgingReportDto }>("/reports/aging-receivables")).data.data!,
    enabled: hasAdvanced,
  });
  const { data: topCustomers } = useQuery({
    queryKey: ["top-customers"],
    queryFn: async () => (await api.get<{ data: TopCustomersReportDto }>("/reports/top-customers?limit=20")).data.data!,
    enabled: hasAdvanced,
  });
  const { data: reliability } = useQuery({
    queryKey: ["customer-reliability"],
    queryFn: async () => (await api.get<{ data: CustomerReliabilityDto[] }>("/reports/customer-reliability")).data.data!,
    enabled: hasAdvanced,
  });
  const { data: retention } = useQuery({
    queryKey: ["customer-retention"],
    queryFn: async () => (await api.get<{ data: CustomerRetentionDto }>("/reports/customer-retention?months=6")).data.data!,
    enabled: hasAdvanced,
  });
  const { data: affinity } = useQuery({
    queryKey: ["product-affinity"],
    queryFn: async () => (await api.get<{ data: ProductAffinityDto[] }>("/reports/product-affinity?limit=15")).data.data!,
    enabled: hasAdvanced,
  });

  if (!hasAdvanced) return <UpgradePrompt feature="Customer Reports" plan="Shop" />;

  return (
    <div className="space-y-4">
      {/* Aging Receivables */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
            <CalendarClock size={15} className="text-amber-500" />
            Aging Receivables
          </CardTitle>
        </CardHeader>
        <CardContent>
          {aging ? (
            <>
              <div className="grid grid-cols-4 gap-3 mb-4">
                <AgingBucket label="0-30 days" amount={aging.total0To30} tone="text-emerald-600" />
                <AgingBucket label="31-60 days" amount={aging.total31To60} tone="text-sky-600" />
                <AgingBucket label="61-90 days" amount={aging.total61To90} tone="text-amber-600" />
                <AgingBucket label="90+ days" amount={aging.total90Plus} tone="text-red-600" />
              </div>
              <p className="text-xs text-slate-400 mb-2">Total outstanding: <span className="font-semibold text-slate-700">{formatNaira(aging.grandTotal)}</span></p>
              {aging.contacts.length > 0 ? (
                <div className="overflow-x-auto">
                  <table className="w-full text-xs">
                    <thead>
                      <tr className="border-b border-slate-200 text-slate-500">
                        <th className="text-left py-2 font-medium">Customer</th>
                        <th className="text-right py-2 font-medium">0-30</th>
                        <th className="text-right py-2 font-medium">31-60</th>
                        <th className="text-right py-2 font-medium">61-90</th>
                        <th className="text-right py-2 font-medium">90+</th>
                        <th className="text-right py-2 font-medium">Total</th>
                        <th className="text-right py-2 font-medium">Oldest</th>
                      </tr>
                    </thead>
                    <tbody>
                      {aging.contacts.slice(0, 25).map((c) => (
                        <tr key={c.contactId} className="border-b border-slate-100">
                          <td className="py-2 text-slate-900">{c.contactName}</td>
                          <td className="text-right text-slate-600">{c.bucket0To30 > 0 ? formatNaira(c.bucket0To30) : "—"}</td>
                          <td className="text-right text-slate-600">{c.bucket31To60 > 0 ? formatNaira(c.bucket31To60) : "—"}</td>
                          <td className="text-right text-amber-600">{c.bucket61To90 > 0 ? formatNaira(c.bucket61To90) : "—"}</td>
                          <td className="text-right text-red-600 font-medium">{c.bucket90Plus > 0 ? formatNaira(c.bucket90Plus) : "—"}</td>
                          <td className="text-right text-slate-900 font-semibold">{formatNaira(c.total)}</td>
                          <td className="text-right text-slate-400">{c.oldestDays}d</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              ) : <p className="text-xs text-slate-400 italic">No outstanding receivables.</p>}
            </>
          ) : <Skeleton className="h-48" />}
        </CardContent>
      </Card>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        {/* Top Customers */}
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
              <Users size={15} className="text-emerald-500" />
              Top Customers (12 months)
            </CardTitle>
          </CardHeader>
          <CardContent>
            {topCustomers ? (
              <>
                {topCustomers.concentrationRisk && (
                  <div className="mb-3 p-2 bg-amber-50 border border-amber-200 rounded text-xs text-amber-800">
                    <AlertTriangle size={12} className="inline mr-1" />
                    <strong>Concentration risk:</strong> top customer is {topCustomers.topCustomerPercent}% of revenue. Losing them would hurt.
                  </div>
                )}
                {topCustomers.customers.length > 0 ? (
                  <div className="space-y-1">
                    {topCustomers.customers.slice(0, 10).map((c) => (
                      <div key={c.contactId} className="flex items-center justify-between py-1.5 border-b border-slate-100">
                        <div className="flex-1 min-w-0">
                          <p className="text-sm text-slate-900 truncate">{c.contactName}</p>
                          <p className="text-xs text-slate-400">{c.transactionCount} purchases</p>
                        </div>
                        <div className="text-right ml-2 shrink-0">
                          <p className="text-sm font-semibold text-emerald-600">{formatNaira(c.totalRevenue)}</p>
                          <p className="text-xs text-slate-400">{c.percentOfTotal}%</p>
                        </div>
                      </div>
                    ))}
                  </div>
                ) : <p className="text-xs text-slate-400 italic">No customer sales yet.</p>}
              </>
            ) : <Skeleton className="h-40" />}
          </CardContent>
        </Card>

        {/* Customer Reliability */}
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
              <UserCheck size={15} className="text-sky-500" />
              Customer Payment Reliability
            </CardTitle>
          </CardHeader>
          <CardContent>
            {reliability ? (
              reliability.length > 0 ? (
                <div className="space-y-1">
                  {reliability.slice(0, 10).map((c) => (
                    <div key={c.contactId} className="flex items-center justify-between py-1.5 border-b border-slate-100">
                      <div className="flex-1 min-w-0">
                        <p className="text-sm text-slate-900 truncate">{c.contactName}</p>
                        <p className="text-xs text-slate-400">{c.paidReceivables} invoices paid</p>
                      </div>
                      <div className="text-right ml-2 shrink-0">
                        <Badge
                          variant="outline"
                          className={
                            c.classification === "Prompt" ? "text-emerald-700 border-emerald-200"
                            : c.classification === "Regular" ? "text-sky-700 border-sky-200"
                            : c.classification === "Slow" ? "text-amber-700 border-amber-200"
                            : "text-red-700 border-red-200"
                          }
                        >
                          {c.classification === "Unknown" ? "No payments" : `${c.averageDaysToPay}d — ${c.classification}`}
                        </Badge>
                      </div>
                    </div>
                  ))}
                </div>
              ) : <p className="text-xs text-slate-400 italic">No credit history yet.</p>
            ) : <Skeleton className="h-40" />}
          </CardContent>
        </Card>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        {/* Customer Retention */}
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
              <Repeat size={15} className="text-purple-500" />
              New vs. Returning Customers
            </CardTitle>
          </CardHeader>
          <CardContent>
            {retention && retention.months.length > 0 ? (
              <ResponsiveContainer width="100%" height={200}>
                <BarChart data={retention.months.map(m => ({
                  month: new Date(m.month).toLocaleString("en", { month: "short" }),
                  New: m.newCustomers,
                  Returning: m.returningCustomers
                }))}>
                  <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
                  <XAxis dataKey="month" tick={{ fontSize: 11 }} />
                  <YAxis tick={{ fontSize: 11 }} />
                  <Tooltip />
                  <Legend wrapperStyle={{ fontSize: 12 }} />
                  <Bar dataKey="New" fill="#10b981" />
                  <Bar dataKey="Returning" fill="#0ea5e9" />
                </BarChart>
              </ResponsiveContainer>
            ) : <Skeleton className="h-48" />}
          </CardContent>
        </Card>

        {/* Product Affinity */}
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
              <Link2 size={15} className="text-sky-500" />
              Frequently Bought Together
            </CardTitle>
          </CardHeader>
          <CardContent>
            {affinity ? (
              affinity.length > 0 ? (
                <div className="space-y-1">
                  {affinity.map((a, i) => (
                    <div key={i} className="flex items-center justify-between py-1.5 border-b border-slate-100 text-xs">
                      <span className="text-slate-900">
                        {a.productA} <span className="text-slate-400">+</span> {a.productB}
                      </span>
                      <div className="text-right ml-2 shrink-0">
                        <span className="text-slate-700 font-medium">{a.coOccurrenceCount}x</span>
                        <span className="text-slate-400 ml-2">{formatNaira(a.combinedRevenue)}</span>
                      </div>
                    </div>
                  ))}
                </div>
              ) : <p className="text-xs text-slate-400 italic py-4">Not enough multi-item sales yet to detect patterns.</p>
            ) : <Skeleton className="h-40" />}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

function AgingBucket({ label, amount, tone }: { label: string; amount: number; tone: string }) {
  return (
    <div className="border rounded-lg p-3">
      <p className="text-xs text-slate-500">{label}</p>
      <p className={`text-sm font-semibold mt-1 ${tone}`}>{formatNaira(amount)}</p>
    </div>
  );
}

// ───────── Inventory tab ─────────

function InventoryTab({ hasAdvanced }: { hasAdvanced: boolean }) {
  const { data: turnover } = useQuery({
    queryKey: ["inventory-turnover"],
    queryFn: async () => (await api.get<{ data: InventoryTurnoverDto[] }>("/reports/inventory-turnover")).data.data!,
    enabled: hasAdvanced,
  });
  const { data: reorder } = useQuery({
    queryKey: ["reorder-suggestions"],
    queryFn: async () => (await api.get<{ data: ReorderSuggestionDto[] }>("/reports/reorder-suggestions?safetyDays=7")).data.data!,
    enabled: hasAdvanced,
  });
  const { data: wastage } = useQuery({
    queryKey: ["wastage"],
    queryFn: async () => (await api.get<{ data: WastageReportDto }>("/reports/wastage?days=30")).data.data!,
    enabled: hasAdvanced,
  });

  if (!hasAdvanced) return <UpgradePrompt feature="Inventory Reports" plan="Shop" />;

  return (
    <div className="space-y-4">
      {/* Reorder Suggestions */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
            <PackageCheck size={15} className="text-amber-500" />
            Reorder Suggestions
          </CardTitle>
        </CardHeader>
        <CardContent>
          {reorder ? (
            reorder.length > 0 ? (
              <div className="space-y-2">
                {reorder.map((r) => (
                  <div key={r.productId} className="flex items-center justify-between border rounded-lg px-3 py-2">
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2">
                        <Badge variant="outline" className={
                          r.urgency === "Critical" ? "text-red-700 border-red-200"
                          : r.urgency === "High" ? "text-amber-700 border-amber-200"
                          : "text-slate-600"
                        }>{r.urgency}</Badge>
                        <p className="text-sm font-medium text-slate-900 truncate">{r.productName}</p>
                      </div>
                      <p className="text-xs text-slate-500 mt-0.5">
                        {r.currentStock} {r.unit} left — {r.dailyVelocity}/day velocity
                      </p>
                    </div>
                    <div className="text-right ml-2 shrink-0">
                      <p className="text-sm font-semibold text-slate-900">Reorder {r.suggestedReorderQty} {r.unit}</p>
                      {r.estimatedCost > 0 && <p className="text-xs text-slate-400">~{formatNaira(r.estimatedCost)}</p>}
                    </div>
                  </div>
                ))}
              </div>
            ) : <p className="text-xs text-slate-400 italic py-4 text-center">All fast-moving products have enough stock.</p>
          ) : <Skeleton className="h-40" />}
        </CardContent>
      </Card>

      {/* Inventory Turnover */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
            <Activity size={15} className="text-sky-500" />
            Inventory Turnover (30 days)
          </CardTitle>
        </CardHeader>
        <CardContent>
          {turnover ? (
            turnover.length > 0 ? (
              <div className="overflow-x-auto">
                <table className="w-full text-xs">
                  <thead>
                    <tr className="border-b border-slate-200 text-slate-500">
                      <th className="text-left py-2 font-medium">Product</th>
                      <th className="text-right py-2 font-medium">Stock</th>
                      <th className="text-right py-2 font-medium">Sold 30d</th>
                      <th className="text-right py-2 font-medium">Days Left</th>
                      <th className="text-right py-2 font-medium">Inventory $</th>
                      <th className="text-right py-2 font-medium">Status</th>
                    </tr>
                  </thead>
                  <tbody>
                    {turnover.slice(0, 30).map((t) => (
                      <tr key={t.productId} className="border-b border-slate-100">
                        <td className="py-2 text-slate-900">{t.productName}</td>
                        <td className="text-right text-slate-600">{t.currentStock} {t.unit}</td>
                        <td className="text-right text-slate-600">{t.soldLast30Days}</td>
                        <td className="text-right text-slate-600">{t.daysOfStockRemaining >= 999 ? "∞" : t.daysOfStockRemaining}</td>
                        <td className="text-right text-slate-600">{formatNaira(t.inventoryValue)}</td>
                        <td className="text-right">
                          <Badge variant="outline" className={
                            t.classification === "Fast" ? "text-emerald-700 border-emerald-200"
                            : t.classification === "Healthy" ? "text-sky-700 border-sky-200"
                            : t.classification === "Slow" ? "text-amber-700 border-amber-200"
                            : "text-red-700 border-red-200"
                          }>{t.classification}</Badge>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : <p className="text-xs text-slate-400 italic">No products yet.</p>
          ) : <Skeleton className="h-48" />}
        </CardContent>
      </Card>

      {/* Wastage */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
            <PackageX size={15} className="text-red-500" />
            Wastage &amp; Damage
          </CardTitle>
        </CardHeader>
        <CardContent>
          {wastage ? (
            <>
              <div className="flex items-baseline gap-3 mb-3">
                <p className="text-xl font-bold text-red-600">{formatNaira(wastage.totalValue)}</p>
                <p className="text-xs text-slate-500">{wastage.eventCount} event{wastage.eventCount !== 1 ? "s" : ""} — {wastage.period}</p>
              </div>
              {wastage.topProducts.length > 0 ? (
                <div className="space-y-1">
                  {wastage.topProducts.slice(0, 10).map((w) => (
                    <div key={w.productId} className="flex items-center justify-between py-1.5 border-b border-slate-100 text-xs">
                      <span className="text-slate-900 truncate">{w.productName}</span>
                      <div className="text-right ml-2 shrink-0">
                        <span className="text-red-600 font-medium">{formatNaira(w.estimatedLoss)}</span>
                        <span className="text-slate-400 ml-2">({w.quantityDamaged} {w.unit})</span>
                      </div>
                    </div>
                  ))}
                </div>
              ) : <p className="text-xs text-slate-400 italic">No damage recorded in this window.</p>}
            </>
          ) : <Skeleton className="h-40" />}
        </CardContent>
      </Card>
    </div>
  );
}

// ───────── Debts tab (aging payables) ─────────

function DebtsTab({ hasAdvanced }: { hasAdvanced: boolean }) {
  const { data: aging } = useQuery({
    queryKey: ["aging-payables"],
    queryFn: async () => (await api.get<{ data: AgingReportDto }>("/reports/aging-payables")).data.data!,
    enabled: hasAdvanced,
  });

  if (!hasAdvanced) return <UpgradePrompt feature="Aging Reports" plan="Shop" />;

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
            <CalendarClock size={15} className="text-orange-500" />
            Aging Payables — what you owe suppliers
          </CardTitle>
        </CardHeader>
        <CardContent>
          {aging ? (
            <>
              <div className="grid grid-cols-4 gap-3 mb-4">
                <AgingBucket label="0-30 days" amount={aging.total0To30} tone="text-emerald-600" />
                <AgingBucket label="31-60 days" amount={aging.total31To60} tone="text-sky-600" />
                <AgingBucket label="61-90 days" amount={aging.total61To90} tone="text-amber-600" />
                <AgingBucket label="90+ days" amount={aging.total90Plus} tone="text-red-600" />
              </div>
              <p className="text-xs text-slate-400 mb-2">Total owed: <span className="font-semibold text-slate-700">{formatNaira(aging.grandTotal)}</span></p>
              {aging.contacts.length > 0 ? (
                <div className="overflow-x-auto">
                  <table className="w-full text-xs">
                    <thead>
                      <tr className="border-b border-slate-200 text-slate-500">
                        <th className="text-left py-2 font-medium">Supplier</th>
                        <th className="text-right py-2 font-medium">0-30</th>
                        <th className="text-right py-2 font-medium">31-60</th>
                        <th className="text-right py-2 font-medium">61-90</th>
                        <th className="text-right py-2 font-medium">90+</th>
                        <th className="text-right py-2 font-medium">Total</th>
                        <th className="text-right py-2 font-medium">Oldest</th>
                      </tr>
                    </thead>
                    <tbody>
                      {aging.contacts.slice(0, 25).map((c) => (
                        <tr key={c.contactId} className="border-b border-slate-100">
                          <td className="py-2 text-slate-900">{c.contactName}</td>
                          <td className="text-right text-slate-600">{c.bucket0To30 > 0 ? formatNaira(c.bucket0To30) : "—"}</td>
                          <td className="text-right text-slate-600">{c.bucket31To60 > 0 ? formatNaira(c.bucket31To60) : "—"}</td>
                          <td className="text-right text-amber-600">{c.bucket61To90 > 0 ? formatNaira(c.bucket61To90) : "—"}</td>
                          <td className="text-right text-red-600 font-medium">{c.bucket90Plus > 0 ? formatNaira(c.bucket90Plus) : "—"}</td>
                          <td className="text-right text-slate-900 font-semibold">{formatNaira(c.total)}</td>
                          <td className="text-right text-slate-400">{c.oldestDays}d</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              ) : <p className="text-xs text-slate-400 italic">No outstanding payables.</p>}
            </>
          ) : <Skeleton className="h-48" />}
        </CardContent>
      </Card>
    </div>
  );
}

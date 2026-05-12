"use client";

import { useState, useEffect, Suspense } from "react";
import { useSearchParams } from "next/navigation";
import { api } from "@/lib/api";

/**
 * Combined Admin Overview — single screen aggregating every admin metric group.
 * Each section fetches its own backing endpoint in parallel; one failure doesn't break
 * the others (renders an inline error placeholder for the affected section only).
 *
 * Section header links jump to the dedicated detail page where one exists; sections
 * without a detail page are info-only.
 */

// ── Response shapes (every one matches AdminController exactly — don't drift) ──

type Overview = {
  totalBusinesses: number;
  totalUsers: number;
  dailyActiveBusinesses: number;
  weeklyActiveBusinesses: number;
  monthlyActiveBusinesses: number;
  newSignups: number;
  trialConversion: { started: number; converted: number; rate: string };
  recentChurnEvents: number;
};

type BillingOverview = {
  totalActiveSubscribers: number;
  byPlan: Array<{ plan: string; count: number }>;
  byProvider: Array<{ provider: string | null; count: number }>;
  autoRenew: number;
  manualRenew: number;
  expiringIn7Days: number;
  inGrace: number;
  pastDue: number;
};

type Revenue = {
  period: string;
  estimatedMrr: number;
  totalRevenue: number;
  totalPayments: number;
  byPlan?: Array<{ plan: string | null; total: number; count: number }>;
  byCurrency?: Array<{ currency: string | null; total: number; count: number }>;
  byProvider?: Array<{ provider: string | null; total: number; count: number }>;
  byMonth?: Array<{ month: string; total: number; count: number }>;
};

type Churn = {
  period: string;
  totalEvents: number;
  uniqueBusinesses: number;
  byCancelled: number;
  byExpired: number;
  byRefunded: number;
  details?: Array<{ businessName: string; eventType: string; createdAtUtc: string }>;
};

type Misparse = {
  overall: { Total: number; Problems: number; Rate: number };
  byIntent?: Array<{ Intent: string; Total: number; Problems: number; Rate: number }>;
};

type RetryPatterns = {
  windowDays: number;
  chainCount: number;
};

type ConfidenceDist = {
  windowDays: number;
  totalMessages: number;
  mean: number;
  distribution: Array<{ bucket: string; count: number; percent: number }>;
};

type TopFailures = {
  windowDays: number;
  totalFailures: number;
  clusters: Array<{ normalized: string; count: number; sampleMessage: string; commonIntent: string | null }>;
};

// `business` is an object {id, name, plan, currency, country} — the controller returns the
// whole entity summary, not just the name. Render `business.name` (and guard for null in
// case a deleted business still has historical rows referencing its id).
type BusinessRef = { id: string; name: string; plan?: string; currency?: string; country?: string } | null;
type TopBusinesses = {
  windowDays: number;
  byMessages: Array<{ business: BusinessRef; messageCount: number }>;
  bySalesVolume: Array<{ business: BusinessRef; salesCount: number; salesTotal: number }>;
};

type MessageVolume = {
  period: string;
  totalInbound: number;
  averagePerDay: number;
  peakDay: { date: string; count: number } | null;
  byChannel?: Array<{ channel: string; count: number }>;
};

type FailedPayments = {
  totalFailed: number;
  byType: Array<{ type: string; count: number }>;
  details: Array<{
    businessName: string | null;
    eventType: string;
    amount: number | null;
    currency: string | null;
    createdAtUtc: string;
  }>;
};

type VoiceAI = {
  totalEnabled: number;
  overrides: number;
  byStatus?: Array<{ status: string | null; count: number }>;
};

type AuditLog = {
  recent: Array<{ at: string; endpoint: string; ip: string; success: boolean; status: number }>;
  failuresByIp: Array<{ ip: string; count: number }>;
};

type OnboardingAnalytics = {
  funnel: Array<{ step: string; count: number }>;
  activeFlows: Array<{ phoneNumber: string; step: string; businessName: string | null; createdAtUtc: string }>;
  recentSignups: Array<{ name: string; createdAtUtc: string }>;
};

// ── Page ─────────────────────────────────────────────────────────────────────

export default function AdminOverviewWrapper() {
  return (
    <Suspense fallback={<div className="p-8 text-slate-500 dark:text-slate-400">Loading...</div>}>
      <AdminOverviewPage />
    </Suspense>
  );
}

function AdminOverviewPage() {
  const searchParams = useSearchParams();
  const key = searchParams.get("key");
  const k = key ? encodeURIComponent(key) : "";

  const [overview, setOverview] = useState<Overview | null>(null);
  const [onboarding, setOnboarding] = useState<OnboardingAnalytics | null>(null);
  const [billing, setBilling] = useState<BillingOverview | null>(null);
  const [revenue, setRevenue] = useState<Revenue | null>(null);
  const [churn, setChurn] = useState<Churn | null>(null);
  const [misparse, setMisparse] = useState<Misparse | null>(null);
  const [retry, setRetry] = useState<RetryPatterns | null>(null);
  const [confidence, setConfidence] = useState<ConfidenceDist | null>(null);
  const [topFailures, setTopFailures] = useState<TopFailures | null>(null);
  const [topBusinesses, setTopBusinesses] = useState<TopBusinesses | null>(null);
  const [messageVolume, setMessageVolume] = useState<MessageVolume | null>(null);
  const [voiceAI, setVoiceAI] = useState<VoiceAI | null>(null);
  const [failedPayments, setFailedPayments] = useState<FailedPayments | null>(null);
  const [audit, setAudit] = useState<AuditLog | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!key) { setError("Missing ?key= parameter"); setLoading(false); return; }
    let alive = true;
    const safe = <T,>(p: Promise<{ data: T }>, set: (v: T) => void) =>
      p.then(r => alive && set(r.data)).catch(() => undefined);

    Promise.allSettled([
      safe(api.get<Overview>(`/admin/metrics/overview?key=${k}&days=30`), setOverview),
      safe(api.get<OnboardingAnalytics>(`/admin/onboarding-analytics?key=${k}`), setOnboarding),
      safe(api.get<BillingOverview>(`/admin/billing-overview?key=${k}`), setBilling),
      safe(api.get<Revenue>(`/admin/metrics/revenue?key=${k}&months=3`), setRevenue),
      safe(api.get<Churn>(`/admin/metrics/churn?key=${k}&days=30`), setChurn),
      safe(api.get<Misparse>(`/admin/telemetry/misparse-rate?key=${k}&days=7`), setMisparse),
      safe(api.get<RetryPatterns>(`/admin/telemetry/retry-patterns?key=${k}&days=7`), setRetry),
      safe(api.get<ConfidenceDist>(`/admin/telemetry/confidence-distribution?key=${k}&days=7`), setConfidence),
      safe(api.get<TopFailures>(`/admin/telemetry/top-failures?key=${k}&days=7&limit=5`), setTopFailures),
      safe(api.get<TopBusinesses>(`/admin/metrics/top-businesses?key=${k}&days=30&limit=5`), setTopBusinesses),
      safe(api.get<MessageVolume>(`/admin/metrics/message-volume?key=${k}&days=7`), setMessageVolume),
      safe(api.get<VoiceAI>(`/admin/voice-ai/overview?key=${k}`), setVoiceAI),
      safe(api.get<FailedPayments>(`/admin/metrics/failed-payments?key=${k}&days=7`), setFailedPayments),
      safe(api.get<AuditLog>(`/admin/audit-log?key=${k}&days=7&limit=50`), setAudit),
    ]).finally(() => alive && setLoading(false));

    return () => { alive = false; };
  }, [key, k]);

  if (loading) return <div className="p-8 text-slate-500 dark:text-slate-400">Loading admin overview...</div>;
  if (error) return <div className="p-8 text-red-500">{error}</div>;

  return (
    <div className="p-8 max-w-7xl mx-auto space-y-10">
      <header>
        <h1 className="text-2xl font-bold text-slate-900 dark:text-slate-50">Admin Overview</h1>
        <p className="text-sm text-slate-500 dark:text-slate-400 mt-1">
          Single-screen snapshot. Each section&apos;s header links to its detail view where one exists.
        </p>
      </header>

      {/* ─────────── Growth & Activity ─────────── */}
      <section>
        <SectionHeader
          title="Growth & Activity"
          subtitle="last 30 days"
          linkTo={`/admin/analytics?key=${k}`}
          linkLabel="View onboarding analytics"
        />
        {overview ? (
          <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
            <Stat label="Active (24h)" value={overview.dailyActiveBusinesses ?? 0} />
            <Stat label="Active (7d)" value={overview.weeklyActiveBusinesses ?? 0} />
            <Stat label="Active (30d)" value={overview.monthlyActiveBusinesses ?? 0} />
            <Stat label="Total businesses" value={overview.totalBusinesses ?? 0} />
            <Stat label="Total users" value={overview.totalUsers ?? 0} />
            <Stat label="New signups (30d)" value={overview.newSignups ?? 0} />
            <Stat
              label="Trial conversion"
              value={overview.trialConversion?.rate ?? "—"}
              sub={overview.trialConversion ? `${overview.trialConversion.converted}/${overview.trialConversion.started}` : undefined}
            />
            <Stat
              label="Churn events (30d)"
              value={overview.recentChurnEvents ?? 0}
              highlight={(overview.recentChurnEvents ?? 0) > 5}
            />
          </div>
        ) : <SectionError />}
      </section>

      {/* ─────────── Onboarding ─────────── */}
      <section>
        <SectionHeader
          title="Onboarding"
          subtitle="signups in-flow + completions"
          linkTo={`/admin/analytics?key=${k}`}
          linkLabel="Full funnel"
        />
        {onboarding ? (
          <div className="space-y-3">
            <div className="grid grid-cols-2 md:grid-cols-3 gap-3">
              <Stat label="In progress" value={(onboarding.activeFlows ?? []).length} />
              <Stat label="Recent completions" value={(onboarding.recentSignups ?? []).length} />
              <Stat label="Biggest drop-off" value={biggestDropoff(onboarding.funnel ?? [])} sub="step name" />
            </div>
            {(onboarding.activeFlows ?? []).length > 0 && (
              <div className="bg-white dark:bg-slate-900 rounded-lg border border-slate-200 dark:border-slate-800 p-4">
                <h3 className="text-xs uppercase font-semibold text-slate-600 dark:text-slate-400 mb-2">
                  Active flows (latest)
                </h3>
                <div className="space-y-1">
                  {onboarding.activeFlows.slice(0, 5).map((f, i) => (
                    <div key={i} className="flex justify-between text-sm">
                      <span className="text-slate-700 dark:text-slate-300">
                        {f.businessName ?? f.phoneNumber} — stuck at <code className="text-xs">{f.step}</code>
                      </span>
                      <span className="text-xs text-slate-400">{new Date(f.createdAtUtc).toLocaleDateString()}</span>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>
        ) : <SectionError />}
      </section>

      {/* ─────────── Subscriptions ─────────── */}
      <section>
        <SectionHeader
          title="Subscriptions"
          subtitle="active paid accounts"
          linkTo={`/api/admin/billing-overview?key=${k}`}
          linkLabel="Raw JSON"
        />
        {billing ? (
          <div className="space-y-3">
            <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
              <Stat label="Active subscribers" value={billing.totalActiveSubscribers ?? 0} />
              <Stat label="Auto-renew on" value={billing.autoRenew ?? 0} />
              <Stat label="Expiring in 7 days" value={billing.expiringIn7Days ?? 0} highlight={(billing.expiringIn7Days ?? 0) > 5} />
              <Stat label="Past due" value={billing.pastDue ?? 0} highlight={(billing.pastDue ?? 0) > 0} />
            </div>
            <KeyedList title="By plan" rows={(billing.byPlan ?? []).map(p => ({ label: p.plan ?? "unknown", value: p.count }))} emptyText="No paid plans yet." />
          </div>
        ) : <SectionError />}
      </section>

      {/* ─────────── Revenue ─────────── */}
      <section>
        <SectionHeader
          title="Revenue"
          subtitle={revenue?.period ?? "last 3 months"}
          linkTo={`/api/admin/metrics/revenue?key=${k}&months=3`}
          linkLabel="Raw JSON"
        />
        {revenue ? (
          <div className="space-y-3">
            <div className="grid grid-cols-2 md:grid-cols-3 gap-3">
              <Stat label="Estimated MRR" value={(revenue.estimatedMrr ?? 0).toLocaleString()} />
              <Stat label="Total revenue" value={(revenue.totalRevenue ?? 0).toLocaleString()} sub={`${revenue.totalPayments ?? 0} payments`} />
              <Stat
                label="Latest month"
                value={revenue.byMonth && revenue.byMonth.length > 0
                  ? revenue.byMonth[revenue.byMonth.length - 1].total.toLocaleString()
                  : "—"}
                sub={revenue.byMonth && revenue.byMonth.length > 0
                  ? revenue.byMonth[revenue.byMonth.length - 1].month
                  : undefined}
              />
            </div>
            <KeyedList
              title="By currency"
              rows={(revenue.byCurrency ?? []).map(c => ({ label: c.currency ?? "unknown", value: c.total }))}
              emptyText="No payments yet."
            />
          </div>
        ) : <SectionError />}
      </section>

      {/* ─────────── Churn ─────────── */}
      <section>
        <SectionHeader
          title="Churn"
          subtitle={churn?.period ?? "last 30 days"}
          linkTo={`/api/admin/metrics/churn?key=${k}&days=30`}
          linkLabel="Raw JSON"
        />
        {churn ? (
          (churn.totalEvents ?? 0) === 0 ? (
            <div className="text-sm text-slate-500 dark:text-slate-400 italic">No churn events in the period.</div>
          ) : (
            <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
              <Stat label="Total events" value={churn.totalEvents ?? 0} highlight={(churn.totalEvents ?? 0) > 10} />
              <Stat label="Unique businesses" value={churn.uniqueBusinesses ?? 0} />
              <Stat label="Cancelled" value={churn.byCancelled ?? 0} />
              <Stat label="Expired" value={churn.byExpired ?? 0} />
              <Stat label="Refunded" value={churn.byRefunded ?? 0} />
            </div>
          )
        ) : <SectionError />}
      </section>

      {/* ─────────── Bot Quality — Misparse ─────────── */}
      <section>
        <SectionHeader
          title="Bot Quality — Misparse Rate"
          subtitle="last 7 days"
          linkTo={`/admin/telemetry?key=${k}`}
          linkLabel="Open telemetry"
        />
        {misparse ? (
          <div className="grid grid-cols-2 md:grid-cols-3 gap-3">
            <Stat
              label="Misparse rate"
              value={misparse.overall ? `${misparse.overall.Rate}%` : "—"}
              highlight={(misparse.overall?.Rate ?? 0) > 5}
              sub={misparse.overall ? `${misparse.overall.Problems} of ${misparse.overall.Total} messages` : undefined}
            />
            <Stat
              label="Top failing intent"
              value={(misparse.byIntent ?? [])[0]?.Intent ?? "—"}
              sub={(misparse.byIntent ?? [])[0] ? `${(misparse.byIntent ?? [])[0].Rate}% fail rate` : undefined}
            />
          </div>
        ) : <SectionError />}
      </section>

      {/* ─────────── Bot Quality — Retry patterns ─────────── */}
      <section>
        <SectionHeader
          title="Bot Quality — Retry Patterns"
          subtitle="users frustrated with the bot (last 7 days)"
          linkTo={`/admin/telemetry?key=${k}`}
          linkLabel="Full chains"
        />
        {retry ? (
          <div className="grid grid-cols-2 md:grid-cols-3 gap-3">
            <Stat
              label="Retry chains observed"
              value={retry.chainCount ?? 0}
              highlight={(retry.chainCount ?? 0) > 20}
              sub="messages user had to re-send"
            />
          </div>
        ) : <SectionError />}
      </section>

      {/* ─────────── Bot Quality — Confidence ─────────── */}
      <section>
        <SectionHeader
          title="Bot Quality — Confidence Distribution"
          subtitle="how sure the AI is when classifying (last 7 days)"
          linkTo={`/admin/telemetry?key=${k}`}
          linkLabel="Open telemetry"
        />
        {confidence ? (
          <div className="space-y-3">
            <div className="grid grid-cols-2 md:grid-cols-3 gap-3">
              <Stat label="Mean confidence" value={confidence.mean?.toFixed(2) ?? "—"} sub={`${confidence.totalMessages} messages`} />
            </div>
            <div className="bg-white dark:bg-slate-900 rounded-lg border border-slate-200 dark:border-slate-800 p-4">
              <h3 className="text-xs uppercase font-semibold text-slate-600 dark:text-slate-400 mb-2">Distribution</h3>
              <div className="space-y-1.5">
                {(confidence.distribution ?? []).map(b => (
                  <div key={b.bucket} className="flex items-center gap-3 text-xs">
                    <span className="w-20 font-mono text-slate-500 dark:text-slate-400">{b.bucket}</span>
                    <div className="flex-1 bg-slate-100 dark:bg-slate-800 h-3 rounded overflow-hidden">
                      <div
                        className="h-full bg-sky-500"
                        style={{ width: `${Math.min(100, b.percent)}%` }}
                      />
                    </div>
                    <span className="w-20 text-right font-mono text-slate-700 dark:text-slate-300">{b.percent}%</span>
                  </div>
                ))}
              </div>
            </div>
          </div>
        ) : <SectionError />}
      </section>

      {/* ─────────── Bot Quality — Top failures ─────────── */}
      <section>
        <SectionHeader
          title="Bot Quality — Top Failure Phrasings"
          subtitle="user inputs the AI most often fails to parse (last 7 days)"
          linkTo={`/admin/telemetry?key=${k}`}
          linkLabel="Open telemetry"
        />
        {topFailures ? (
          (topFailures.clusters ?? []).length === 0 ? (
            <div className="text-sm text-slate-500 dark:text-slate-400 italic">No failed-parse phrasings in the last 7 days.</div>
          ) : (
            <div className="bg-white dark:bg-slate-900 rounded-lg border border-slate-200 dark:border-slate-800 overflow-hidden">
              <table className="w-full text-sm">
                <thead className="bg-slate-50 dark:bg-slate-800 text-xs uppercase">
                  <tr>
                    <th className="px-3 py-2 text-left">Sample phrasing</th>
                    <th className="px-3 py-2 text-left">Common intent attempt</th>
                    <th className="px-3 py-2 text-right">Count</th>
                  </tr>
                </thead>
                <tbody>
                  {topFailures.clusters.slice(0, 5).map((c, i) => (
                    <tr key={i} className="border-t border-slate-100 dark:border-slate-800">
                      <td className="px-3 py-2 text-slate-700 dark:text-slate-300 italic">&ldquo;{c.sampleMessage}&rdquo;</td>
                      <td className="px-3 py-2 text-slate-500 dark:text-slate-400 font-mono text-xs">{c.commonIntent ?? "—"}</td>
                      <td className="px-3 py-2 text-right font-mono text-slate-900 dark:text-slate-100">{c.count}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )
        ) : <SectionError />}
      </section>

      {/* ─────────── Top Businesses ─────────── */}
      <section>
        <SectionHeader
          title="Top Businesses"
          subtitle="last 30 days — by message volume + sales"
          linkTo={`/api/admin/metrics/top-businesses?key=${k}&days=30&limit=25`}
          linkLabel="Full list (JSON)"
        />
        {topBusinesses ? (
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div className="bg-white dark:bg-slate-900 rounded-lg border border-slate-200 dark:border-slate-800 p-4">
              <h3 className="text-xs uppercase font-semibold text-slate-600 dark:text-slate-400 mb-2">Most active (messages)</h3>
              <div className="space-y-1">
                {(topBusinesses.byMessages ?? []).slice(0, 5).map((b, i) => (
                  <div key={i} className="flex justify-between text-sm">
                    <span className="text-slate-700 dark:text-slate-300">{b.business?.name ?? "—"}</span>
                    <span className="font-mono text-slate-900 dark:text-slate-100">{b.messageCount}</span>
                  </div>
                ))}
                {(topBusinesses.byMessages ?? []).length === 0 && (
                  <div className="text-xs italic text-slate-500 dark:text-slate-400">No activity yet.</div>
                )}
              </div>
            </div>
            <div className="bg-white dark:bg-slate-900 rounded-lg border border-slate-200 dark:border-slate-800 p-4">
              <h3 className="text-xs uppercase font-semibold text-slate-600 dark:text-slate-400 mb-2">Top sellers (revenue)</h3>
              <div className="space-y-1">
                {(topBusinesses.bySalesVolume ?? []).slice(0, 5).map((b, i) => (
                  <div key={i} className="flex justify-between text-sm">
                    <span className="text-slate-700 dark:text-slate-300">{b.business?.name ?? "—"}</span>
                    <span className="font-mono text-slate-900 dark:text-slate-100">{b.salesTotal?.toLocaleString() ?? 0}</span>
                  </div>
                ))}
                {(topBusinesses.bySalesVolume ?? []).length === 0 && (
                  <div className="text-xs italic text-slate-500 dark:text-slate-400">No sales yet.</div>
                )}
              </div>
            </div>
          </div>
        ) : <SectionError />}
      </section>

      {/* ─────────── Message Volume ─────────── */}
      <section>
        <SectionHeader
          title="Message Volume"
          subtitle="last 7 days — across all channels"
          linkTo={`/api/admin/metrics/message-volume?key=${k}&days=30`}
          linkLabel="30-day data (JSON)"
        />
        {messageVolume ? (
          <div className="space-y-3">
            <div className="grid grid-cols-2 md:grid-cols-3 gap-3">
              <Stat label="Total inbound (7d)" value={messageVolume.totalInbound ?? 0} />
              <Stat label="Average per day" value={messageVolume.averagePerDay ?? 0} />
              <Stat
                label="Peak day"
                value={messageVolume.peakDay?.count ?? 0}
                sub={messageVolume.peakDay ? new Date(messageVolume.peakDay.date).toLocaleDateString() : undefined}
              />
            </div>
            <KeyedList
              title="By channel"
              rows={(messageVolume.byChannel ?? []).map(c => ({ label: c.channel, value: c.count }))}
              emptyText="No volume data."
            />
          </div>
        ) : <SectionError />}
      </section>

      {/* ─────────── Voice AI ─────────── */}
      <section>
        <SectionHeader
          title="Voice AI"
          subtitle="businesses with Voice AI enabled"
          linkTo={`/admin/voice-ai?key=${k}`}
          linkLabel="Voice AI detail"
        />
        {voiceAI ? (
          <div className="space-y-3">
            <div className="grid grid-cols-2 md:grid-cols-3 gap-3">
              <Stat label="Total enabled" value={voiceAI.totalEnabled ?? 0} />
              <Stat label="Internal overrides" value={voiceAI.overrides ?? 0} sub="set via admin tool" />
            </div>
            <KeyedList
              title="By status"
              rows={(voiceAI.byStatus ?? []).map(s => ({ label: s.status ?? "unknown", value: s.count }))}
              emptyText="No Voice AI accounts yet."
            />
          </div>
        ) : <SectionError />}
      </section>

      {/* ─────────── Failed Payments ─────────── */}
      <section>
        <SectionHeader
          title="Failed Payments"
          subtitle="last 7 days"
          linkTo={`/api/admin/metrics/failed-payments?key=${k}&days=30`}
          linkLabel="30-day data (JSON)"
        />
        {failedPayments ? (
          (failedPayments.totalFailed ?? 0) === 0 ? (
            <div className="text-sm text-slate-500 dark:text-slate-400 italic">No payment failures in the last 7 days.</div>
          ) : (
            <div className="bg-white dark:bg-slate-900 rounded-lg border border-slate-200 dark:border-slate-800 overflow-hidden">
              <table className="w-full text-sm">
                <thead className="bg-slate-50 dark:bg-slate-800 text-xs uppercase">
                  <tr>
                    <th className="px-3 py-2 text-left">Business</th>
                    <th className="px-3 py-2 text-left">Event</th>
                    <th className="px-3 py-2 text-right">Amount</th>
                    <th className="px-3 py-2 text-right">When</th>
                  </tr>
                </thead>
                <tbody>
                  {(failedPayments.details ?? []).slice(0, 10).map((e, i) => (
                    <tr key={i} className="border-t border-slate-100 dark:border-slate-800">
                      <td className="px-3 py-2 text-slate-700 dark:text-slate-300">{e.businessName ?? "—"}</td>
                      <td className="px-3 py-2 text-slate-700 dark:text-slate-300">{e.eventType ?? "—"}</td>
                      <td className="px-3 py-2 text-right text-slate-700 dark:text-slate-300 font-mono">
                        {e.amount != null ? `${e.currency ?? ""}${e.amount.toLocaleString()}` : "—"}
                      </td>
                      <td className="px-3 py-2 text-right text-slate-500 dark:text-slate-400">
                        {e.createdAtUtc ? new Date(e.createdAtUtc).toLocaleString() : "—"}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )
        ) : <SectionError />}
      </section>

      {/* ─────────── Admin Access Audit ─────────── */}
      <section>
        <SectionHeader
          title="Admin Access Audit"
          subtitle="last 7 days"
          linkTo={`/api/admin/audit-log?key=${k}&days=30&limit=500`}
          linkLabel="30-day log (JSON)"
        />
        {audit ? (
          <div className="space-y-3">
            {(audit.failuresByIp ?? []).length > 0 && (
              <div className="bg-amber-50 dark:bg-amber-950/40 border border-amber-200 dark:border-amber-900 rounded-lg p-3 text-sm">
                <strong className="text-amber-800 dark:text-amber-300">Failed access attempts:</strong>{" "}
                {audit.failuresByIp.map(f => `${f.ip} (${f.count}×)`).join(", ")}
              </div>
            )}
            <div className="bg-white dark:bg-slate-900 rounded-lg border border-slate-200 dark:border-slate-800 overflow-hidden">
              <table className="w-full text-xs">
                <thead className="bg-slate-50 dark:bg-slate-800 text-xs uppercase">
                  <tr>
                    <th className="px-3 py-2 text-left">When</th>
                    <th className="px-3 py-2 text-left">Endpoint</th>
                    <th className="px-3 py-2 text-left">IP</th>
                    <th className="px-3 py-2 text-right">Status</th>
                  </tr>
                </thead>
                <tbody>
                  {(audit.recent ?? []).slice(0, 25).map((r, i) => (
                    <tr key={i} className="border-t border-slate-100 dark:border-slate-800">
                      <td className="px-3 py-2 text-slate-500 dark:text-slate-400">{new Date(r.at).toLocaleString()}</td>
                      <td className="px-3 py-2 font-mono text-slate-700 dark:text-slate-300">{r.endpoint}</td>
                      <td className="px-3 py-2 font-mono text-slate-600 dark:text-slate-400">{r.ip ?? "—"}</td>
                      <td className={`px-3 py-2 text-right font-mono ${r.success ? "text-emerald-600 dark:text-emerald-400" : "text-red-600 dark:text-red-400"}`}>
                        {r.status}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        ) : <SectionError />}
      </section>

      <footer className="text-xs text-slate-400 dark:text-slate-500 pt-4 border-t border-slate-200 dark:border-slate-800">
        Detail views:{" "}
        <a className="underline" href={`/admin/analytics?key=${k}`}>Onboarding</a>{" · "}
        <a className="underline" href={`/admin/telemetry?key=${k}`}>Telemetry</a>{" · "}
        <a className="underline" href={`/admin/voice-ai?key=${k}`}>Voice AI</a>
      </footer>
    </div>
  );
}

// ── Helpers ──────────────────────────────────────────────────────────────────

function biggestDropoff(funnel: Array<{ step: string; count: number }>): string {
  // Find the step → step transition with the biggest absolute drop in count.
  // Useful as a single-glance "where am I bleeding signups" indicator.
  if (funnel.length < 2) return "—";
  let worstStep = "—";
  let worstDrop = 0;
  for (let i = 1; i < funnel.length; i++) {
    const drop = funnel[i - 1].count - funnel[i].count;
    if (drop > worstDrop) {
      worstDrop = drop;
      worstStep = funnel[i].step;
    }
  }
  return worstStep;
}

function SectionHeader({
  title,
  subtitle,
  linkTo,
  linkLabel,
}: {
  title: string;
  subtitle: string;
  linkTo?: string;
  linkLabel?: string;
}) {
  return (
    <div className="mb-3 flex items-end justify-between gap-4">
      <div className="min-w-0">
        <h2 className="text-lg font-semibold text-slate-900 dark:text-slate-100">{title}</h2>
        <p className="text-xs text-slate-500 dark:text-slate-400">{subtitle}</p>
      </div>
      {linkTo && (
        <a
          href={linkTo}
          className="text-xs text-sky-600 dark:text-sky-400 hover:text-sky-700 dark:hover:text-sky-300 hover:underline font-medium whitespace-nowrap"
        >
          {linkLabel ?? "View details"} →
        </a>
      )}
    </div>
  );
}

function SectionError() {
  return (
    <div className="text-sm text-red-500 italic">
      Failed to load this section. Endpoint may be misconfigured or returning an error.
    </div>
  );
}

function Stat({ label, value, sub, highlight }: { label: string; value: string | number; sub?: string; highlight?: boolean }) {
  return (
    <div className={`rounded-lg border p-3 ${
      highlight
        ? "bg-amber-50 dark:bg-amber-950/40 border-amber-200 dark:border-amber-900"
        : "bg-white dark:bg-slate-900 border-slate-200 dark:border-slate-800"
    }`}>
      <p className="text-xs text-slate-500 dark:text-slate-400">{label}</p>
      <p className="text-xl font-bold text-slate-900 dark:text-slate-100 mt-0.5">{value}</p>
      {sub && <p className="text-[10px] text-slate-400 dark:text-slate-500 mt-0.5">{sub}</p>}
    </div>
  );
}

function KeyedList({
  title,
  rows,
  emptyText,
}: {
  title: string;
  rows: Array<{ label: string; value: number }>;
  emptyText: string;
}) {
  return (
    <div className="bg-white dark:bg-slate-900 rounded-lg border border-slate-200 dark:border-slate-800 p-4">
      <h3 className="text-xs uppercase font-semibold text-slate-600 dark:text-slate-400 mb-2">{title}</h3>
      <div className="space-y-1">
        {rows.length === 0 ? (
          <div className="text-xs italic text-slate-500 dark:text-slate-400">{emptyText}</div>
        ) : (
          rows.map(r => (
            <div key={r.label} className="flex justify-between text-sm">
              <span className="text-slate-700 dark:text-slate-300">{r.label}</span>
              <span className="font-mono text-slate-900 dark:text-slate-100">{r.value}</span>
            </div>
          ))
        )}
      </div>
    </div>
  );
}

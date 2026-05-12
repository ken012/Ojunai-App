"use client";

import { useState, useEffect, Suspense } from "react";
import { useSearchParams } from "next/navigation";
import { api } from "@/lib/api";

/**
 * Combined Admin Overview — single screen aggregating the four most-watched admin metric
 * groups. Each section fetches its own backing endpoint in parallel; failures in one don't
 * break the others (renders an error placeholder for that section only). Read-only — no
 * write actions on this page.
 */

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
  totalActive: number;
  byPlan: Array<{ plan: string; count: number }>;
  recentRevenue: number;
  currency: string;
};

type Misparse = {
  overall: { Total: number; Problems: number; Rate: number };
};

type FailedPayments = {
  events: Array<{ businessName: string; eventType: string; amount: number; createdAtUtc: string }>;
  total: number;
};

type AuditLog = {
  recent: Array<{ at: string; endpoint: string; ip: string; success: boolean; status: number }>;
  failuresByIp: Array<{ ip: string; count: number }>;
};

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

  const [overview, setOverview] = useState<Overview | null>(null);
  const [billing, setBilling] = useState<BillingOverview | null>(null);
  const [misparse, setMisparse] = useState<Misparse | null>(null);
  const [failedPayments, setFailedPayments] = useState<FailedPayments | null>(null);
  const [audit, setAudit] = useState<AuditLog | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!key) { setError("Missing ?key= parameter"); setLoading(false); return; }
    let alive = true;
    const k = encodeURIComponent(key);

    // Fire everything in parallel. Each branch tolerates its own failure.
    Promise.allSettled([
      api.get<Overview>(`/admin/metrics/overview?key=${k}&days=30`).then(r => alive && setOverview(r.data)),
      api.get<BillingOverview>(`/admin/billing-overview?key=${k}`).then(r => alive && setBilling(r.data)),
      api.get<Misparse>(`/admin/telemetry/misparse-rate?key=${k}&days=7`).then(r => alive && setMisparse(r.data)),
      api.get<FailedPayments>(`/admin/metrics/failed-payments?key=${k}&days=7`).then(r => alive && setFailedPayments(r.data)),
      api.get<AuditLog>(`/admin/audit-log?key=${k}&days=7&limit=50`).then(r => alive && setAudit(r.data)),
    ]).then(results => {
      if (!alive) return;
      const allFailed = results.every(r => r.status === "rejected");
      if (allFailed) setError("Unauthorized or all sections failed to load");
      setLoading(false);
    });

    return () => { alive = false; };
  }, [key]);

  if (loading) return <div className="p-8 text-slate-500 dark:text-slate-400">Loading admin overview...</div>;
  if (error) return <div className="p-8 text-red-500">{error}</div>;

  return (
    <div className="p-8 max-w-7xl mx-auto space-y-8">
      <header>
        <h1 className="text-2xl font-bold text-slate-900 dark:text-slate-50">Admin Overview</h1>
        <p className="text-sm text-slate-500 dark:text-slate-400 mt-1">
          Single-screen snapshot. Each section links to its detail view.
        </p>
      </header>

      {/* ── Growth metrics ── */}
      <section>
        <SectionHeader title="Growth & Activity" subtitle="last 30 days" />
        {overview ? (
          <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
            <Stat label="Active businesses (24h)" value={overview.dailyActiveBusinesses} />
            <Stat label="Active businesses (7d)" value={overview.weeklyActiveBusinesses} />
            <Stat label="Active businesses (30d)" value={overview.monthlyActiveBusinesses} />
            <Stat label="Total businesses" value={overview.totalBusinesses} />
            <Stat label="Total users" value={overview.totalUsers} />
            <Stat label="New signups (30d)" value={overview.newSignups} />
            <Stat label="Trial conversion" value={overview.trialConversion.rate} sub={`${overview.trialConversion.converted}/${overview.trialConversion.started}`} />
            <Stat label="Churn events (30d)" value={overview.recentChurnEvents} highlight={overview.recentChurnEvents > 5} />
          </div>
        ) : <SectionError />}
      </section>

      {/* ── Billing ── */}
      <section>
        <SectionHeader title="Subscriptions" subtitle="active accounts by plan" />
        {billing ? (
          <div className="space-y-3">
            <div className="grid grid-cols-2 md:grid-cols-3 gap-3">
              <Stat label="Active subscriptions" value={billing.totalActive} />
              <Stat label={`Revenue (7d, ${billing.currency})`} value={billing.recentRevenue.toLocaleString()} />
            </div>
            <div className="bg-white dark:bg-slate-900 rounded-lg border border-slate-200 dark:border-slate-800 p-4">
              <h3 className="text-xs uppercase font-semibold text-slate-600 dark:text-slate-400 mb-2">By plan</h3>
              <div className="space-y-1">
                {billing.byPlan?.map(p => (
                  <div key={p.plan} className="flex justify-between text-sm">
                    <span className="text-slate-700 dark:text-slate-300">{p.plan}</span>
                    <span className="font-mono text-slate-900 dark:text-slate-100">{p.count}</span>
                  </div>
                ))}
              </div>
            </div>
          </div>
        ) : <SectionError />}
      </section>

      {/* ── Bot quality ── */}
      <section>
        <SectionHeader title="Bot Quality" subtitle="misparse rate (last 7d)" />
        {misparse ? (
          <div className="grid grid-cols-2 md:grid-cols-3 gap-3">
            <Stat
              label="Misparse rate"
              value={`${misparse.overall.Rate}%`}
              highlight={misparse.overall.Rate > 5}
              sub={`${misparse.overall.Problems} of ${misparse.overall.Total} messages`}
            />
          </div>
        ) : <SectionError />}
      </section>

      {/* ── Failed payments ── */}
      <section>
        <SectionHeader title="Failed Payments" subtitle="last 7 days" />
        {failedPayments ? (
          failedPayments.total === 0 ? (
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
                  {failedPayments.events.slice(0, 10).map((e, i) => (
                    <tr key={i} className="border-t border-slate-100 dark:border-slate-800">
                      <td className="px-3 py-2 text-slate-700 dark:text-slate-300">{e.businessName ?? "—"}</td>
                      <td className="px-3 py-2 text-slate-700 dark:text-slate-300">{e.eventType}</td>
                      <td className="px-3 py-2 text-right text-slate-700 dark:text-slate-300 font-mono">{e.amount?.toLocaleString() ?? "—"}</td>
                      <td className="px-3 py-2 text-right text-slate-500 dark:text-slate-400">
                        {new Date(e.createdAtUtc).toLocaleString()}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )
        ) : <SectionError />}
      </section>

      {/* ── Audit log ── */}
      <section>
        <SectionHeader title="Admin Access Audit" subtitle="last 7 days" />
        {audit ? (
          <div className="space-y-3">
            {audit.failuresByIp.length > 0 && (
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
                  {audit.recent.slice(0, 25).map((r, i) => (
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
        <a className="underline" href={`/admin/analytics?key=${encodeURIComponent(key ?? "")}`}>Onboarding</a>{" · "}
        <a className="underline" href={`/admin/telemetry?key=${encodeURIComponent(key ?? "")}`}>Telemetry</a>{" · "}
        <a className="underline" href={`/admin/voice-ai?key=${encodeURIComponent(key ?? "")}`}>Voice AI</a>
      </footer>
    </div>
  );
}

function SectionHeader({ title, subtitle }: { title: string; subtitle: string }) {
  return (
    <div className="mb-3">
      <h2 className="text-lg font-semibold text-slate-900 dark:text-slate-100">{title}</h2>
      <p className="text-xs text-slate-500 dark:text-slate-400">{subtitle}</p>
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

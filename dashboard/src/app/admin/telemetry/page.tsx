"use client";

import { useState, useEffect, Suspense } from "react";
import { useSearchParams } from "next/navigation";
import { api } from "@/lib/api";

// Types matching the backend AdminController telemetry endpoints
type ByIntentRow = { intent: string; total: number; problems: number; rate: number };
type MisparseResponse = {
  windowDays: number;
  overall: { total: number; problems: number; rate: number };
  byIntent: ByIntentRow[];
};

type RetryChainMessage = { message: string; intent: string | null; status: string; at: string };
type RetryChain = { userId: string; messages: RetryChainMessage[] };
type RetryResponse = { windowDays: number; chainCount: number; chains: RetryChain[] };

type ConfidenceBucket = { bucket: string; count: number; percent: number };
type ConfidenceResponse = {
  windowDays: number;
  totalMessages: number;
  mean: number;
  distribution: ConfidenceBucket[];
};

type FailureCluster = {
  normalized: string;
  count: number;
  sampleMessage: string;
  commonIntent: string | null;
  avgConfidence: number | null;
};
type FailureResponse = { windowDays: number; totalFailures: number; clusters: FailureCluster[] };

function TelemetryPageInner() {
  const searchParams = useSearchParams();
  const initialKey = searchParams.get("key") || "";
  const [key, setKey] = useState(initialKey);
  const [windowDays, setWindowDays] = useState(7);
  const [misparse, setMisparse] = useState<MisparseResponse | null>(null);
  const [retries, setRetries] = useState<RetryResponse | null>(null);
  const [confidence, setConfidence] = useState<ConfidenceResponse | null>(null);
  const [failures, setFailures] = useState<FailureResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function fetchAll() {
    if (!key) { setError("Admin key required"); return; }
    setLoading(true);
    setError(null);
    try {
      const [mr, rr, cr, fr] = await Promise.all([
        api.get<MisparseResponse>(`/admin/telemetry/misparse-rate?key=${encodeURIComponent(key)}&days=${windowDays}`),
        api.get<RetryResponse>(`/admin/telemetry/retry-patterns?key=${encodeURIComponent(key)}&days=${windowDays}`),
        api.get<ConfidenceResponse>(`/admin/telemetry/confidence-distribution?key=${encodeURIComponent(key)}&days=${windowDays}`),
        api.get<FailureResponse>(`/admin/telemetry/top-failures?key=${encodeURIComponent(key)}&days=${windowDays}`),
      ]);
      setMisparse(mr.data);
      setRetries(rr.data);
      setConfidence(cr.data);
      setFailures(fr.data);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : "Failed to load telemetry";
      setError(msg);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    if (initialKey) fetchAll();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="min-h-screen bg-slate-50 dark:bg-slate-950 p-6">
      <div className="max-w-6xl mx-auto space-y-6">
        <div>
          <h1 className="text-2xl font-bold text-slate-900 dark:text-slate-50">Ojunai Telemetry</h1>
          <p className="text-sm text-slate-500 dark:text-slate-400 mt-1">
            Production observability over WhatsApp message logs. Misparse rate, retry chains, confidence distribution, and top failing phrasings.
          </p>
        </div>

        <div className="bg-white dark:bg-slate-900 rounded-lg border border-slate-200 dark:border-slate-800 p-4 flex flex-wrap gap-3 items-end">
          <div className="flex-1 min-w-[250px]">
            <label className="text-xs font-medium text-slate-600 dark:text-slate-400 block mb-1">Admin key</label>
            <input
              type="password"
              value={key}
              onChange={(e) => setKey(e.target.value)}
              className="w-full h-9 px-3 rounded border border-slate-300 dark:border-slate-700 text-sm"
              placeholder="Paste the Admin:AnalyticsKey"
            />
          </div>
          <div>
            <label className="text-xs font-medium text-slate-600 dark:text-slate-400 block mb-1">Window</label>
            <select
              value={windowDays}
              onChange={(e) => setWindowDays(Number(e.target.value))}
              className="h-9 px-3 rounded border border-slate-300 dark:border-slate-700 text-sm bg-white dark:bg-slate-900"
            >
              <option value={1}>1 day</option>
              <option value={7}>7 days</option>
              <option value={14}>14 days</option>
              <option value={30}>30 days</option>
            </select>
          </div>
          <button
            onClick={fetchAll}
            disabled={loading || !key}
            className="h-9 px-4 rounded bg-cyan-600 text-white text-sm font-medium hover:bg-cyan-700 disabled:opacity-50"
          >
            {loading ? "Loading..." : "Load telemetry"}
          </button>
        </div>

        {error && (
          <div className="bg-red-50 border border-red-200 text-red-700 rounded-lg p-3 text-sm">{error}</div>
        )}

        {/* Misparse rate */}
        {misparse && (
          <section className="bg-white dark:bg-slate-900 rounded-lg border border-slate-200 dark:border-slate-800 p-5">
            <div className="flex items-baseline justify-between mb-3">
              <h2 className="font-semibold text-slate-900 dark:text-slate-50">Misparse rate</h2>
              <span className="text-xs text-slate-500 dark:text-slate-400">{misparse.windowDays}-day window</span>
            </div>
            <div className="grid grid-cols-3 gap-3 mb-4">
              <Stat label="Total messages" value={misparse.overall.total.toLocaleString()} />
              <Stat label="Problems" value={misparse.overall.problems.toLocaleString()} />
              <Stat
                label="Overall rate"
                value={`${misparse.overall.rate}%`}
                tone={misparse.overall.rate > 5 ? "red" : misparse.overall.rate > 3 ? "amber" : "green"}
              />
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-xs">
                <thead>
                  <tr className="border-b border-slate-200 dark:border-slate-800 text-slate-500 dark:text-slate-400">
                    <th className="text-left py-2 font-medium">Intent</th>
                    <th className="text-right py-2 font-medium">Total</th>
                    <th className="text-right py-2 font-medium">Problems</th>
                    <th className="text-right py-2 font-medium">Rate</th>
                  </tr>
                </thead>
                <tbody>
                  {misparse.byIntent.slice(0, 20).map((row) => (
                    <tr key={row.intent} className="border-b border-slate-100 dark:border-slate-800">
                      <td className="py-2 text-slate-900 dark:text-slate-50">{row.intent}</td>
                      <td className="text-right text-slate-600 dark:text-slate-400">{row.total}</td>
                      <td className="text-right text-slate-600 dark:text-slate-400">{row.problems}</td>
                      <td className={`text-right font-medium ${row.rate > 5 ? "text-red-600" : row.rate > 3 ? "text-amber-600" : "text-slate-700 dark:text-slate-300"}`}>
                        {row.rate}%
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        )}

        {/* Confidence distribution */}
        {confidence && (
          <section className="bg-white dark:bg-slate-900 rounded-lg border border-slate-200 dark:border-slate-800 p-5">
            <div className="flex items-baseline justify-between mb-3">
              <h2 className="font-semibold text-slate-900 dark:text-slate-50">Confidence distribution</h2>
              <span className="text-xs text-slate-500 dark:text-slate-400">
                {confidence.totalMessages.toLocaleString()} parsed messages · mean {confidence.mean}
              </span>
            </div>
            <div className="space-y-1">
              {confidence.distribution.map((b) => (
                <div key={b.bucket}>
                  <div className="flex justify-between text-xs mb-1">
                    <span className="text-slate-600 dark:text-slate-400 font-mono">{b.bucket}</span>
                    <span className="text-slate-900 dark:text-slate-50 font-medium">
                      {b.count} <span className="text-slate-400 dark:text-slate-500">({b.percent}%)</span>
                    </span>
                  </div>
                  <div className="h-2 bg-slate-100 dark:bg-slate-800 rounded-full overflow-hidden">
                    <div
                      className={
                        b.bucket.startsWith("0.90") ? "h-full bg-emerald-500"
                        : b.bucket.startsWith("0.75") ? "h-full bg-cyan-500"
                        : b.bucket.startsWith("0.60") ? "h-full bg-amber-500"
                        : "h-full bg-red-500"
                      }
                      style={{ width: `${Math.min(100, b.percent)}%` }}
                    />
                  </div>
                </div>
              ))}
            </div>
          </section>
        )}

        {/* Top failures */}
        {failures && (
          <section className="bg-white dark:bg-slate-900 rounded-lg border border-slate-200 dark:border-slate-800 p-5">
            <div className="flex items-baseline justify-between mb-3">
              <h2 className="font-semibold text-slate-900 dark:text-slate-50">Top failing phrasings</h2>
              <span className="text-xs text-slate-500 dark:text-slate-400">
                {failures.totalFailures.toLocaleString()} total failures in window
              </span>
            </div>
            <p className="text-xs text-slate-500 dark:text-slate-400 mb-3">
              Clusters of messages that failed to parse. Each is a candidate for a new corpus entry.
            </p>
            {failures.clusters.length === 0 ? (
              <p className="text-sm text-slate-400 dark:text-slate-500 italic">No failures in this window. Nice.</p>
            ) : (
              <div className="space-y-2">
                {failures.clusters.map((c, i) => (
                  <div key={i} className="border border-slate-200 dark:border-slate-800 rounded p-3">
                    <div className="flex items-baseline justify-between">
                      <p className="text-sm font-medium text-slate-900 dark:text-slate-50">&quot;{c.sampleMessage}&quot;</p>
                      <span className="text-xs text-slate-500 dark:text-slate-400">×{c.count}</span>
                    </div>
                    <p className="text-xs text-slate-500 dark:text-slate-400 mt-1">
                      Claude parsed as: <span className="font-mono">{c.commonIntent || "—"}</span>
                      {c.avgConfidence !== null && (
                        <span className="ml-2">avg confidence {c.avgConfidence}</span>
                      )}
                    </p>
                  </div>
                ))}
              </div>
            )}
          </section>
        )}

        {/* Retry patterns */}
        {retries && (
          <section className="bg-white dark:bg-slate-900 rounded-lg border border-slate-200 dark:border-slate-800 p-5">
            <div className="flex items-baseline justify-between mb-3">
              <h2 className="font-semibold text-slate-900 dark:text-slate-50">Retry chains</h2>
              <span className="text-xs text-slate-500 dark:text-slate-400">
                {retries.chainCount} users retried after clarification
              </span>
            </div>
            <p className="text-xs text-slate-500 dark:text-slate-400 mb-3">
              Each chain is a user who sent multiple messages in quick succession after the bot asked for clarification. The bot didn&apos;t understand them the first time — likely not the second time either.
            </p>
            {retries.chains.length === 0 ? (
              <p className="text-sm text-slate-400 dark:text-slate-500 italic">No retry chains in this window.</p>
            ) : (
              <div className="space-y-3">
                {retries.chains.slice(0, 15).map((chain, i) => (
                  <div key={i} className="border border-slate-200 dark:border-slate-800 rounded p-3">
                    <p className="text-xs text-slate-400 dark:text-slate-500 mb-2">User {chain.userId.slice(0, 8)}…</p>
                    <div className="space-y-1">
                      {chain.messages.map((m, j) => (
                        <div key={j} className="text-xs">
                          <span className="text-slate-400 dark:text-slate-500 font-mono">{new Date(m.at).toLocaleTimeString()}</span>
                          <span className="text-slate-900 dark:text-slate-50 ml-2">&quot;{m.message}&quot;</span>
                          <span className={`ml-2 ${m.status === "NeedsClarification" ? "text-amber-600" : "text-slate-500 dark:text-slate-400"}`}>
                            → {m.intent || "—"} ({m.status})
                          </span>
                        </div>
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </section>
        )}

        {!misparse && !error && (
          <div className="text-center py-16 text-slate-400 dark:text-slate-500 text-sm">
            Paste the admin key and click &quot;Load telemetry&quot; to view metrics.
          </div>
        )}
      </div>
    </div>
  );
}

function Stat({ label, value, tone }: { label: string; value: string; tone?: "green" | "amber" | "red" }) {
  const toneClass =
    tone === "red" ? "text-red-600"
    : tone === "amber" ? "text-amber-600"
    : tone === "green" ? "text-emerald-600"
    : "text-slate-900 dark:text-slate-50";
  return (
    <div className="border border-slate-200 dark:border-slate-800 rounded p-3">
      <p className="text-xs text-slate-500 dark:text-slate-400">{label}</p>
      <p className={`text-xl font-bold mt-1 ${toneClass}`}>{value}</p>
    </div>
  );
}

export default function TelemetryPage() {
  return (
    <Suspense fallback={<div className="p-8">Loading...</div>}>
      <TelemetryPageInner />
    </Suspense>
  );
}

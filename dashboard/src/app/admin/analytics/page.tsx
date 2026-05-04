"use client";

import { useState, useEffect, Suspense } from "react";
import { useSearchParams } from "next/navigation";
import { api } from "@/lib/api";

type FunnelStep = { step: string; count: number };
type ActiveFlow = {
  phoneNumber: string;
  step: string;
  businessName: string | null;
  businessType: string | null;
  city: string | null;
  ownerName: string | null;
  createdAtUtc: string;
  lastActivityUtc: string;
};
type RecentSignup = {
  name: string;
  businessType: string | null;
  city: string | null;
  owner: string | null;
  phone: string | null;
  plan: string;
  trialEndsAt: string | null;
  createdAtUtc: string;
};

const STEP_ORDER = [
  "onboarding:menu",
  "onboarding:started",
  "onboarding:business_name",
  "onboarding:business_type",
  "onboarding:city",
  "onboarding:owner_name",
  "onboarding:awaiting_confirm",
  "onboarding:complete",
];

const STEP_LABELS: Record<string, string> = {
  "onboarding:menu": "Saw menu",
  "onboarding:started": "Started signup",
  "onboarding:business_name": "Gave business name",
  "onboarding:business_type": "Gave business type",
  "onboarding:city": "Gave city",
  "onboarding:owner_name": "Gave owner name",
  "onboarding:awaiting_confirm": "At confirmation",
  "onboarding:complete": "Account created",
  "onboarding:cancelled": "Cancelled",
  "onboarding:restart": "Restarted",
  "onboarding:expired": "Expired",
  "onboarding:correction": "Made correction",
  "onboarding:resume_prompt": "Saw resume prompt",
  "onboarding:staff_inquiry": "Staff inquiry",
  "onboarding:help": "Asked for help",
  "onboarding:create_failed": "Create failed",
};

export default function AdminAnalyticsWrapper() {
  return (
    <Suspense fallback={<div className="p-8 text-slate-500">Loading...</div>}>
      <AdminAnalyticsPage />
    </Suspense>
  );
}

function AdminAnalyticsPage() {
  const searchParams = useSearchParams();
  const key = searchParams.get("key");
  const [data, setData] = useState<{ funnel: FunnelStep[]; activeFlows: ActiveFlow[]; recentSignups: RecentSignup[] } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!key) { setError("Missing key"); setLoading(false); return; }
    api.get(`/admin/onboarding-analytics?key=${key}`)
      .then(({ data: res }) => setData(res as { funnel: FunnelStep[]; activeFlows: ActiveFlow[]; recentSignups: RecentSignup[] }))
      .catch(() => setError("Unauthorized or failed to load"))
      .finally(() => setLoading(false));
  }, [key]);

  if (loading) return <div className="p-8 text-slate-500">Loading...</div>;
  if (error) return <div className="p-8 text-red-500">{error}</div>;
  if (!data) return null;

  const funnelSteps = STEP_ORDER.map((step) => ({
    step,
    label: STEP_LABELS[step] ?? step,
    count: data.funnel.find((f) => f.step === step)?.count ?? 0,
  }));

  const otherSteps = data.funnel.filter((f) => !STEP_ORDER.includes(f.step));
  const maxCount = Math.max(...funnelSteps.map((f) => f.count), 1);

  return (
    <div className="p-8 max-w-5xl mx-auto space-y-8">
      <div>
        <h1 className="text-2xl font-bold text-slate-900">Onboarding Analytics</h1>
        <p className="text-sm text-slate-500 mt-1">Private admin view — last 500 onboarding events</p>
      </div>

      {/* Funnel */}
      <div className="bg-white border rounded-xl p-6">
        <h2 className="text-sm font-semibold text-slate-700 mb-4">Signup Funnel</h2>
        <div className="space-y-2">
          {funnelSteps.map((f) => (
            <div key={f.step} className="flex items-center gap-3">
              <span className="text-xs text-slate-500 w-36 text-right">{f.label}</span>
              <div className="flex-1 bg-slate-100 rounded-full h-6 overflow-hidden">
                <div
                  className="bg-cyan-500 h-full rounded-full transition-all"
                  style={{ width: `${(f.count / maxCount) * 100}%` }}
                />
              </div>
              <span className="text-sm font-semibold text-slate-700 w-10 text-right">{f.count}</span>
            </div>
          ))}
        </div>
        {otherSteps.length > 0 && (
          <div className="mt-4 pt-4 border-t">
            <p className="text-xs text-slate-400 mb-2">Other events</p>
            <div className="flex flex-wrap gap-2">
              {otherSteps.map((f) => (
                <span key={f.step} className="text-xs bg-slate-100 px-2 py-1 rounded">
                  {STEP_LABELS[f.step] ?? f.step}: {f.count}
                </span>
              ))}
            </div>
          </div>
        )}
      </div>

      {/* Active Flows */}
      <div className="bg-white border rounded-xl p-6">
        <h2 className="text-sm font-semibold text-slate-700 mb-4">
          Active / Abandoned Flows ({data.activeFlows.length})
        </h2>
        {data.activeFlows.length > 0 ? (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="text-left text-xs text-slate-400 border-b">
                  <th className="pb-2">Phone</th>
                  <th className="pb-2">Step</th>
                  <th className="pb-2">Business</th>
                  <th className="pb-2">Started</th>
                  <th className="pb-2">Last Activity</th>
                </tr>
              </thead>
              <tbody>
                {data.activeFlows.map((f) => {
                  const idle = (Date.now() - new Date(f.lastActivityUtc).getTime()) / 60000;
                  const status = idle > 1440 ? "text-red-500" : idle > 30 ? "text-amber-500" : "text-green-500";
                  return (
                    <tr key={f.phoneNumber} className="border-b border-slate-50">
                      <td className="py-2 font-mono text-xs">{f.phoneNumber}</td>
                      <td className="py-2">
                        <span className={`text-xs font-medium ${status}`}>{f.step}</span>
                      </td>
                      <td className="py-2 text-slate-600">{f.businessName ?? "—"}</td>
                      <td className="py-2 text-xs text-slate-400">{new Date(f.createdAtUtc).toLocaleString()}</td>
                      <td className="py-2 text-xs text-slate-400">{Math.round(idle)}m ago</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        ) : (
          <p className="text-sm text-slate-400">No active onboarding flows</p>
        )}
      </div>

      {/* Recent Signups */}
      <div className="bg-white border rounded-xl p-6">
        <h2 className="text-sm font-semibold text-slate-700 mb-4">
          Recent Signups (last 30 days)
        </h2>
        {data.recentSignups.length > 0 ? (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="text-left text-xs text-slate-400 border-b">
                  <th className="pb-2">Business</th>
                  <th className="pb-2">Type</th>
                  <th className="pb-2">City</th>
                  <th className="pb-2">Owner</th>
                  <th className="pb-2">Plan</th>
                  <th className="pb-2">Created</th>
                </tr>
              </thead>
              <tbody>
                {data.recentSignups.map((s, i) => (
                  <tr key={i} className="border-b border-slate-50">
                    <td className="py-2 font-medium">{s.name}</td>
                    <td className="py-2 text-slate-500">{s.businessType ?? "—"}</td>
                    <td className="py-2 text-slate-500">{s.city ?? "—"}</td>
                    <td className="py-2 text-slate-600">{s.owner ?? "—"}</td>
                    <td className="py-2">
                      <span className="text-xs bg-slate-100 px-2 py-0.5 rounded">{s.plan}</span>
                    </td>
                    <td className="py-2 text-xs text-slate-400">{new Date(s.createdAtUtc).toLocaleString()}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <p className="text-sm text-slate-400">No signups in the last 30 days</p>
        )}
      </div>
    </div>
  );
}

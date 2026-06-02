"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { usePlanStatus } from "@/lib/use-plan-status";
import { useBusiness } from "@/lib/data-sync";
import { api } from "@/lib/api";
import { formatNaira, formatDateTime } from "@/lib/format";
import type { PaginatedActivityResult, ActivityFeedDto } from "@/lib/types";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/empty-state";
import { Phone, CheckCircle, AlertTriangle, Clock, Save, ShoppingCart, Receipt as ReceiptIcon, Package, ArrowRight, Activity as ActivityIcon } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import {
  VOICE_AI_ANNUAL_DISCOUNT,
  VOICE_AI_TIER_CODES,
  VOICE_AI_TIER_LABELS,
  VOICE_AI_TIER_MINUTES,
  VOICE_AI_TIER_CONCURRENT_LINES,
  VOICE_AI_TIER_PRICING,
  VOICE_AI_TIER_FEATURES,
  VOICE_AI_TIER_TAGLINES,
  VOICE_AI_TRIAL_MINUTES,
  type VoiceAITier,
} from "@/lib/voice-ai-pricing";
import { CURRENCY_META, SUPPORTED_CURRENCIES } from "@/lib/pricing";
import type { SupportedCurrency, BillingCycle } from "@/lib/pricing";

type VoiceAISettings = {
  id: string;
  name: string;
  defaultLanguage: "en" | "yo" | "ha" | "ig";
  reservationHoldHours: number;
  voiceTransport: "record" | "streaming";
  fallbackHandoffPhone: string | null;
  address: string | null;
  greetingTemplateEn: string | null;
  greetingTemplateYo: string | null;
  greetingTemplateHa: string | null;
  greetingTemplateIg: string | null;
};

const LANG_LABELS: Record<string, string> = { en: "English", yo: "Yoruba", ha: "Hausa", ig: "Igbo" };
const LANGUAGES = ["en", "yo", "ha", "ig"] as const;

// ── Action log: the hero feature of the Voice AI page ────────────────────────
// Shows Voice-recorded sales/expenses/inventory/payments in reverse-chronological order.
// Filterable by type only — source is locked to Voice since this page is Voice-specific.
// (WhatsApp actions live under Activity / their own list pages.)

type ActionFilter = "all" | "sale" | "expense" | "inventory" | "payment_received" | "debt_recorded";

const ACTION_FILTERS: { id: ActionFilter; label: string }[] = [
  { id: "all", label: "All" },
  { id: "sale", label: "Sales" },
  { id: "expense", label: "Expenses" },
  { id: "inventory", label: "Stock" },
  { id: "payment_received", label: "Payments" },
  { id: "debt_recorded", label: "Debts" },
];

function actionIcon(type: string) {
  if (type.startsWith("sale")) return <ShoppingCart size={14} />;
  if (type.startsWith("expense")) return <ReceiptIcon size={14} />;
  if (type === "inventory") return <Package size={14} />;
  if (type === "payment_received" || type === "payment_made") return <CheckCircle size={14} />;
  return <ActivityIcon size={14} />;
}
function actionTone(type: string): { bg: string; text: string } {
  if (type.includes("voided")) return { bg: "bg-rose-50 dark:bg-rose-950/30", text: "text-rose-600 dark:text-rose-400" };
  if (type.startsWith("sale")) return { bg: "bg-emerald-50 dark:bg-emerald-950/30", text: "text-emerald-600 dark:text-emerald-400" };
  if (type.startsWith("expense")) return { bg: "bg-orange-50 dark:bg-orange-950/30", text: "text-orange-600 dark:text-orange-400" };
  if (type === "inventory") return { bg: "bg-cyan-50 dark:bg-cyan-950/30", text: "text-cyan-600 dark:text-cyan-400" };
  if (type === "payment_received") return { bg: "bg-emerald-50 dark:bg-emerald-950/30", text: "text-emerald-600 dark:text-emerald-400" };
  return { bg: "bg-slate-100 dark:bg-slate-800", text: "text-slate-600 dark:text-slate-400" };
}

function ActionLog() {
  const router = useRouter();
  const [typeFilter, setTypeFilter] = useState<ActionFilter>("all");

  const { data, isLoading } = useQuery({
    queryKey: ["voice-ai-action-log", typeFilter],
    queryFn: async () => {
      const params = new URLSearchParams({
        page: "1",
        pageSize: "30",
        source: "Voice",
      });
      if (typeFilter !== "all") params.append("type", typeFilter);
      const { data } = await api.get<{ data: PaginatedActivityResult }>(`/dashboard/activity?${params}`);
      return data.data!;
    },
  });

  function navigateToSource(item: ActivityFeedDto) {
    if (item.type.startsWith("sale")) router.push("/sales");
    else if (item.type.startsWith("expense")) router.push("/expenses");
    else if (item.type === "inventory") router.push("/inventory");
    else if (item.type.includes("payment") || item.type.includes("debt")) router.push("/contacts");
  }

  const items = data?.items ?? [];
  const lastActionAt = items[0]?.createdAtUtc;

  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="text-sm font-semibold text-slate-900 dark:text-slate-50 flex items-center gap-2">
          <ActivityIcon size={15} className="text-cyan-500" />
          Recent Voice AI Actions
        </CardTitle>
        <p className="text-xs text-slate-500 dark:text-slate-400 mt-1">
          {lastActionAt
            ? <>Last action <span className="font-medium text-slate-700 dark:text-slate-300">{formatDateTime(lastActionAt)}</span></>
            : "Every sale, expense, and stock change recorded by your Voice AI lives here."}
        </p>
      </CardHeader>
      <CardContent className="space-y-3">
        {/* Type filter chips */}
        <div className="flex items-center gap-1.5 flex-wrap">
          {ACTION_FILTERS.map((f) => (
            <button
              key={f.id}
              onClick={() => setTypeFilter(f.id)}
              className={`px-2.5 py-1 text-xs font-medium rounded-md transition-colors ${
                typeFilter === f.id
                  ? "bg-slate-900 dark:bg-slate-100 text-white dark:text-slate-900"
                  : "bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400 hover:bg-slate-200 dark:hover:bg-slate-700"
              }`}
            >
              {f.label}
            </button>
          ))}
        </div>

        {/* Feed */}
        {isLoading ? (
          <div className="space-y-2">
            {Array.from({ length: 5 }).map((_, i) => (
              <Skeleton key={i} className="h-14 rounded-lg" />
            ))}
          </div>
        ) : items.length === 0 ? (
          <EmptyState
            icon={<Phone size={20} />}
            title="No Voice AI actions yet"
            description="When customers call your Voice AI line and place orders or pay debts, they'll show up here."
          />
        ) : (
          <div className="divide-y divide-slate-100 dark:divide-slate-800 -mx-2">
            {items.map((item) => {
              const tone = actionTone(item.type);
              const voided = item.type.includes("voided");
              return (
                <button
                  key={item.id}
                  onClick={() => navigateToSource(item)}
                  className="w-full text-left flex items-center gap-3 px-2 py-2.5 rounded-lg hover:bg-slate-50 dark:hover:bg-slate-800/50 transition-colors group"
                >
                  <div className={`p-2 rounded-md flex-shrink-0 ${tone.bg} ${tone.text}`}>
                    {actionIcon(item.type)}
                  </div>
                  <div className="flex-1 min-w-0">
                    <p className={`text-sm font-medium text-slate-900 dark:text-slate-50 truncate ${voided ? "line-through text-slate-400 dark:text-slate-500" : ""}`}>
                      {item.description}
                    </p>
                    <div className="flex items-center gap-2 mt-0.5 text-xs text-slate-500 dark:text-slate-400">
                      <span>{formatDateTime(item.createdAtUtc)}</span>
                      {item.contactName && <span className="truncate">· {item.contactName}</span>}
                    </div>
                  </div>
                  {item.amount != null && (
                    <span className={`text-sm font-semibold tabular-nums whitespace-nowrap ${
                      item.type.startsWith("expense") || item.type === "payment_made"
                        ? "text-rose-600 dark:text-rose-400"
                        : "text-slate-900 dark:text-slate-50"
                    } ${voided ? "line-through text-slate-400 dark:text-slate-500" : ""}`}>
                      {item.type.startsWith("expense") || item.type === "payment_made" ? "-" : ""}
                      {formatNaira(item.amount)}
                    </span>
                  )}
                  <ArrowRight size={14} className="text-slate-300 dark:text-slate-600 group-hover:text-slate-500 dark:group-hover:text-slate-400 flex-shrink-0" />
                </button>
              );
            })}
          </div>
        )}
      </CardContent>
    </Card>
  );
}

export default function VoiceAIPage() {
  const { data: planStatus } = usePlanStatus();
  const business = useBusiness();

  if (!planStatus?.voiceAIFeatureVisible) {
    return (
      <div className="flex items-center justify-center min-h-[60vh]">
        <p className="text-slate-400 dark:text-slate-500">This feature is not available yet.</p>
      </div>
    );
  }

  if (planStatus.voiceAIPlanStatus === "suspended") {
    return <SuspendedView />;
  }

  if (planStatus.voiceAIEnabled) {
    return <EnabledView planStatus={planStatus} business={business} />;
  }

  return <MarketingView currency={(business?.currency ?? "NGN") as SupportedCurrency} />;
}

// ── Marketing View (not enabled) ─────────────────────────────────────────────

function MarketingView({ currency: defaultCurrency }: { currency: SupportedCurrency }) {
  const [cycle, setCycle] = useState<BillingCycle>("monthly");
  const [currency, setCurrency] = useState<SupportedCurrency>(defaultCurrency);

  const contactSubject = encodeURIComponent("OjunaiVoice — Enable for my business");
  const contactBody = encodeURIComponent("Hi Ojunai Team,\n\nI'm interested in enabling OjunaiVoice for my business.\n\nPlease get in touch to set it up.\n\nThank you.");

  return (
    <div className="space-y-6 max-w-5xl mx-auto">
      <div className="text-center">
        <div className="inline-flex items-center justify-center w-16 h-16 rounded-full bg-violet-100 mb-4">
          <Phone size={28} className="text-violet-600" />
        </div>
        <h2 className="text-2xl font-bold text-slate-900 dark:text-slate-50">OjunaiVoice</h2>
        <p className="text-slate-500 dark:text-slate-400 mt-2 max-w-md mx-auto">
          AI-powered phone receptionist that handles customer calls in English, Yoruba, Hausa, and Igbo.
          Pick the tier that matches your call volume.
        </p>
        <p className="text-xs text-emerald-600 dark:text-emerald-400 mt-3">
          ✓ Try it free — {VOICE_AI_TRIAL_MINUTES} inbound minutes on the house
        </p>
      </div>

      <div className="flex items-center justify-center gap-3">
        <div className="inline-flex items-center gap-1 p-1 rounded-lg bg-slate-100 dark:bg-slate-800">
          <button
            onClick={() => setCycle("monthly")}
            className={`px-4 py-1.5 text-sm font-medium rounded-md transition-colors ${cycle === "monthly" ? "bg-white dark:bg-slate-900 text-slate-900 dark:text-slate-50 shadow-sm" : "text-slate-600 dark:text-slate-400"}`}
          >
            Monthly
          </button>
          <button
            onClick={() => setCycle("annual")}
            className={`px-4 py-1.5 text-sm font-medium rounded-md transition-colors ${cycle === "annual" ? "bg-white dark:bg-slate-900 text-slate-900 dark:text-slate-50 shadow-sm" : "text-slate-600 dark:text-slate-400"}`}
          >
            Annual <span className="ml-1 text-xs text-emerald-500">-{VOICE_AI_ANNUAL_DISCOUNT}%</span>
          </button>
        </div>
        <select
          value={currency}
          onChange={(e) => setCurrency(e.target.value as SupportedCurrency)}
          className="h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 text-xs bg-white dark:bg-slate-900"
        >
          {SUPPORTED_CURRENCIES.map((c) => <option key={c} value={c}>{CURRENCY_META[c].symbol} {c}</option>)}
        </select>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {VOICE_AI_TIER_CODES.map((tier) => {
          const price = VOICE_AI_TIER_PRICING[tier][cycle][currency] ?? 0;
          const monthlyEquiv = cycle === "annual" ? Math.round(price / 12) : price;
          const sym = CURRENCY_META[currency]?.symbol ?? currency;
          const isPro = tier === "pro";
          return (
            <Card key={tier} className={isPro ? "border-violet-300 dark:border-violet-700 ring-1 ring-violet-200 dark:ring-violet-800/50" : undefined}>
              <CardContent className="pt-6">
                <div className="flex items-baseline justify-between mb-1">
                  <h3 className="text-lg font-bold text-slate-900 dark:text-slate-50">{VOICE_AI_TIER_LABELS[tier]}</h3>
                  {isPro && <Badge className="bg-violet-100 text-violet-700 dark:bg-violet-900/40 dark:text-violet-300">Recommended</Badge>}
                </div>
                <p className="text-xs text-slate-500 dark:text-slate-400 mb-4">{VOICE_AI_TIER_TAGLINES[tier]}</p>
                <div className="mb-5">
                  <p className="text-3xl font-bold text-slate-900 dark:text-slate-50">
                    {sym}{price.toLocaleString()}
                  </p>
                  <p className="text-xs text-slate-400 dark:text-slate-500">
                    {cycle === "annual" ? `per year (≈ ${sym}${monthlyEquiv.toLocaleString()}/mo)` : "per month"}
                  </p>
                </div>
                <div className="space-y-2 mb-5">
                  {VOICE_AI_TIER_FEATURES[tier].map((f) => (
                    <div key={f} className="flex items-start gap-2">
                      <CheckCircle size={14} className="text-emerald-500 mt-0.5 flex-shrink-0" />
                      <span className="text-sm text-slate-700 dark:text-slate-300">{f}</span>
                    </div>
                  ))}
                </div>
                <a
                  href={`mailto:contact@ojunai.com?subject=${contactSubject}&body=${contactBody}`}
                  className={`flex items-center justify-center gap-2 w-full py-2.5 rounded-lg text-sm font-semibold transition-colors ${
                    isPro
                      ? "bg-violet-600 hover:bg-violet-700 text-white"
                      : "bg-slate-100 hover:bg-slate-200 dark:bg-slate-800 dark:hover:bg-slate-700 text-slate-900 dark:text-slate-50"
                  }`}
                >
                  <Phone size={14} />
                  Contact us to enable
                </a>
              </CardContent>
            </Card>
          );
        })}
      </div>

      <p className="text-[11px] text-slate-400 dark:text-slate-500 text-center">
        Our team configures your phone number, voice persona, and call routing. Most businesses are live within 24 hours.
      </p>
    </div>
  );
}

// ── Tier + Minutes meter (shown inside EnabledView header) ──────────────────

function VoiceMeter({ planStatus }: {
  planStatus: {
    voiceAIPlanStatus: string;
    voiceAITier: VoiceAITier | null;
    voiceAITierMinutesIncluded: number | null;
    voiceAICycleMinutesUsed: number;
    voiceAICycleMinutesRemaining: number | null;
    voiceAITrialMinutesRemaining: number | null;
    voiceAITrialMinutesUsed: number;
  };
}) {
  const isTrial = planStatus.voiceAIPlanStatus === "trial";
  const used = isTrial ? planStatus.voiceAITrialMinutesUsed : planStatus.voiceAICycleMinutesUsed;
  const cap = isTrial ? VOICE_AI_TRIAL_MINUTES : (planStatus.voiceAITierMinutesIncluded ?? 0);
  const remaining = isTrial ? (planStatus.voiceAITrialMinutesRemaining ?? 0) : (planStatus.voiceAICycleMinutesRemaining ?? 0);
  const pct = cap > 0 ? Math.min(100, (used / cap) * 100) : 0;
  const tone = pct >= 90 ? "bg-rose-500" : pct >= 70 ? "bg-amber-500" : "bg-emerald-500";

  return (
    <div className="rounded-xl border border-slate-200 dark:border-slate-800 p-4 bg-white dark:bg-slate-900">
      <div className="flex items-baseline justify-between mb-2">
        <p className="text-xs font-medium text-slate-600 dark:text-slate-400">
          {isTrial ? "Free trial usage" : `${planStatus.voiceAITier ? VOICE_AI_TIER_LABELS[planStatus.voiceAITier] : "Voice"} — this cycle`}
        </p>
        <p className="text-xs text-slate-500 dark:text-slate-400 tabular-nums">
          {used} / {cap} min
        </p>
      </div>
      <div className="h-2 rounded-full bg-slate-100 dark:bg-slate-800 overflow-hidden">
        <div className={`h-full ${tone} transition-all`} style={{ width: `${pct}%` }} />
      </div>
      <p className="text-[11px] text-slate-400 dark:text-slate-500 mt-2">
        {remaining} inbound minute{remaining === 1 ? "" : "s"} remaining
        {isTrial && " on your trial — subscribe to keep your line live"}
      </p>
    </div>
  );
}

// ── Enabled View (with settings) ─────────────────────────────────────────────

function EnabledView({ planStatus, business }: {
  planStatus: {
    voiceAIPlanStatus: string;
    voiceAITier: VoiceAITier | null;
    voiceAITierMinutesIncluded: number | null;
    voiceAICycleMinutesUsed: number;
    voiceAICycleMinutesRemaining: number | null;
    voiceAITrialMinutesRemaining: number | null;
    voiceAITrialMinutesUsed: number;
    voiceAISubscriptionEndsAt: string | null;
  };
  business: { name?: string; accountNumber?: string; timezone?: string } | null;
}) {
  const isTrial = planStatus.voiceAIPlanStatus === "trial";
  const minutesLeft = isTrial
    ? (planStatus.voiceAITrialMinutesRemaining ?? 0)
    : (planStatus.voiceAICycleMinutesRemaining ?? 0);
  const tierLabel = planStatus.voiceAITier ? VOICE_AI_TIER_LABELS[planStatus.voiceAITier] : null;

  const { data: settings, isLoading, error: fetchError } = useQuery({
    queryKey: ["voice-ai-settings"],
    queryFn: async () => {
      const { data } = await api.get<VoiceAISettings>("/business/voice-ai-settings");
      return data;
    },
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  });

  return (
    <div className="space-y-6">
      <PageHeader
        title="OjunaiVoice"
        subtitle="Configure your AI phone inventory specialist"
        actions={
          <Badge className={isTrial ? "bg-amber-100 text-amber-700" : "bg-emerald-100 text-emerald-700"}>
            {isTrial ? `Trial — ${minutesLeft} min left` : (tierLabel ?? "Active")}
          </Badge>
        }
      />

      <VoiceMeter planStatus={planStatus} />

      {isTrial && minutesLeft <= 3 && (
        <div className="bg-amber-50 border border-amber-200 rounded-xl p-4 flex items-start gap-3">
          <Clock size={18} className="text-amber-500 flex-shrink-0 mt-0.5" />
          <div>
            <p className="text-sm font-semibold text-amber-800">Trial nearly out</p>
            <p className="text-xs text-amber-600 mt-0.5">
              You have {minutesLeft} inbound minute{minutesLeft === 1 ? "" : "s"} left on your free trial. Subscribe to a tier to keep your line live.
            </p>
          </div>
        </div>
      )}

      {/* Read-only info */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300 flex items-center gap-2">
            <Phone size={15} className="text-violet-500" />
            Account Info
          </CardTitle>
          <p className="text-xs text-slate-500 dark:text-slate-400 mt-1">Voice AI subscription status and account details</p>
        </CardHeader>
        <CardContent>
          <div className="grid grid-cols-2 sm:grid-cols-3 gap-4 text-sm">
            <div><span className="text-slate-500 dark:text-slate-400 block text-xs">Status</span><span className="font-medium">{isTrial ? "Trial" : (tierLabel ?? "Active")}</span></div>
            {business?.accountNumber && <div><span className="text-slate-500 dark:text-slate-400 block text-xs">Account #</span><span className="font-mono font-medium">{business.accountNumber}</span></div>}
            {!isTrial && planStatus.voiceAISubscriptionEndsAt && (
              <div>
                <span className="text-slate-500 dark:text-slate-400 block text-xs">Renews</span>
                <span className="font-medium">{new Date(planStatus.voiceAISubscriptionEndsAt).toLocaleDateString()}</span>
              </div>
            )}
            {isTrial && (
              <div>
                <span className="text-slate-500 dark:text-slate-400 block text-xs">Trial cap</span>
                <span className="font-medium">{VOICE_AI_TRIAL_MINUTES} min</span>
              </div>
            )}
          </div>
        </CardContent>
      </Card>

      {/* Action log — the hero feature */}
      <ActionLog />

      {isLoading && <Skeleton className="h-96" />}
      {fetchError && (
        <Card className="border-red-200">
          <CardContent className="pt-4">
            <p className="text-sm text-red-600">{fetchError instanceof Error ? fetchError.message : "Failed to load Voice AI settings. Please contact support."}</p>
          </CardContent>
        </Card>
      )}
      {settings && <SettingsForm initial={settings} businessTimezone={business?.timezone ?? "Africa/Lagos"} />}
    </div>
  );
}

// ── Settings Form ────────────────────────────────────────────────────────────

function SettingsForm({ initial, businessTimezone }: { initial: VoiceAISettings; businessTimezone: string }) {
  const qc = useQueryClient();
  const [form, setForm] = useState<VoiceAISettings>(initial);
  const [saving, setSaving] = useState(false);
  const [saveResult, setSaveResult] = useState<{ ok: boolean; msg: string } | null>(null);
  const [greetingTab, setGreetingTab] = useState<string>(initial.defaultLanguage || "en");

  function set<K extends keyof VoiceAISettings>(key: K, value: VoiceAISettings[K]) {
    setForm(f => ({ ...f, [key]: value }));
    setSaveResult(null);
  }

  function getDiff(): Partial<VoiceAISettings> {
    const diff: Record<string, unknown> = {};
    for (const key of Object.keys(form) as (keyof VoiceAISettings)[]) {
      if (key === "id") continue;
      if (form[key] !== initial[key]) diff[key] = form[key];
    }
    return diff;
  }

  async function handleSave() {
    const diff = getDiff();
    if (Object.keys(diff).length === 0) { setSaveResult({ ok: true, msg: "No changes to save." }); return; }

    setSaving(true);
    setSaveResult(null);
    try {
      await api.patch("/business/voice-ai-settings", { ...diff, timezone: businessTimezone });
      qc.invalidateQueries({ queryKey: ["voice-ai-settings"] });
      setSaveResult({ ok: true, msg: "Settings saved." });
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[]; message?: string } } };
      setSaveResult({ ok: false, msg: ax.response?.data?.errors?.[0] ?? ax.response?.data?.message ?? "Failed to save." });
    } finally {
      setSaving(false);
    }
  }

  const hasChanges = Object.keys(getDiff()).length > 0;

  return (
    <div className="space-y-4">
      {/* Greeting Templates */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300">Greeting</CardTitle>
          <p className="text-xs text-slate-400 dark:text-slate-500 mt-0.5">What callers hear when they pick up. Plain text, no SSML. Blank = generic greeting.</p>
        </CardHeader>
        <CardContent>
          <div className="flex gap-1 mb-3">
            {LANGUAGES.map((lang) => (
              <button key={lang} onClick={() => setGreetingTab(lang)}
                className={`px-3 py-1 text-xs font-medium rounded-md transition-colors ${greetingTab === lang ? "bg-slate-900 dark:bg-slate-100 text-white dark:text-slate-900" : "bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400 hover:bg-slate-200 dark:hover:bg-slate-700"}`}
              >{LANG_LABELS[lang]}</button>
            ))}
          </div>
          {greetingTab === "en" && <textarea className="w-full h-24 p-3 rounded-md border border-slate-200 dark:border-slate-800 text-sm resize-none" maxLength={500} placeholder="Hi, you've reached our store. How can I help you today?" value={form.greetingTemplateEn ?? ""} onChange={(e) => set("greetingTemplateEn", e.target.value || null)} />}
          {greetingTab === "yo" && <textarea className="w-full h-24 p-3 rounded-md border border-slate-200 dark:border-slate-800 text-sm resize-none" maxLength={500} placeholder="Yoruba greeting template..." value={form.greetingTemplateYo ?? ""} onChange={(e) => set("greetingTemplateYo", e.target.value || null)} />}
          {greetingTab === "ha" && <textarea className="w-full h-24 p-3 rounded-md border border-slate-200 dark:border-slate-800 text-sm resize-none" maxLength={500} placeholder="Hausa greeting template..." value={form.greetingTemplateHa ?? ""} onChange={(e) => set("greetingTemplateHa", e.target.value || null)} />}
          {greetingTab === "ig" && <textarea className="w-full h-24 p-3 rounded-md border border-slate-200 dark:border-slate-800 text-sm resize-none" maxLength={500} placeholder="Igbo greeting template..." value={form.greetingTemplateIg ?? ""} onChange={(e) => set("greetingTemplateIg", e.target.value || null)} />}
          <p className="text-[10px] text-slate-400 dark:text-slate-500 mt-1 text-right">{(greetingTab === "en" ? form.greetingTemplateEn : greetingTab === "yo" ? form.greetingTemplateYo : greetingTab === "ha" ? form.greetingTemplateHa : form.greetingTemplateIg)?.length ?? 0}/500</p>
        </CardContent>
      </Card>

      {/* Behavior */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300">Behavior</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <div>
            <Label className="text-xs text-slate-500 dark:text-slate-400">Business Name</Label>
            <Input value={form.name} onChange={(e) => set("name", e.target.value)} maxLength={200} placeholder="Your business name" />
            <p className="text-[10px] text-slate-400 dark:text-slate-500 mt-1">Business name read aloud by the AI when callers ask.</p>
          </div>
          <div>
            <Label className="text-xs text-slate-500 dark:text-slate-400">Default Language</Label>
            <p className="text-[10px] text-slate-400 dark:text-slate-500 mb-2">Language the AI greets in if it can{"'"}t auto-detect the caller{"'"}s language.</p>
            <div className="flex gap-2">
              {LANGUAGES.map((lang) => (
                <label key={lang} className={`flex items-center gap-1.5 px-3 py-1.5 rounded-md border cursor-pointer text-sm ${form.defaultLanguage === lang ? "border-cyan-300 bg-cyan-50 text-cyan-700" : "border-slate-200 dark:border-slate-800 text-slate-600 dark:text-slate-400"}`}>
                  <input type="radio" name="defaultLang" value={lang} checked={form.defaultLanguage === lang} onChange={() => set("defaultLanguage", lang)} className="sr-only" />
                  {LANG_LABELS[lang]}
                </label>
              ))}
            </div>
          </div>
          <div>
            <Label className="text-xs text-slate-500 dark:text-slate-400">Voice Transport</Label>
            <p className="text-[10px] text-slate-400 dark:text-slate-500 mb-2">Record: TwiML turn-by-turn (reliable, recommended). Streaming: real-time via Twilio ConversationRelay (lower latency, experimental).</p>
            <div className="flex gap-2">
              {(["record", "streaming"] as const).map((t) => (
                <label key={t} className={`flex items-center gap-1.5 px-3 py-1.5 rounded-md border cursor-pointer text-sm ${form.voiceTransport === t ? "border-cyan-300 bg-cyan-50 text-cyan-700" : "border-slate-200 dark:border-slate-800 text-slate-600 dark:text-slate-400"}`}>
                  <input type="radio" name="transport" value={t} checked={form.voiceTransport === t} onChange={() => set("voiceTransport", t)} className="sr-only" />
                  {t === "record" ? "Record (recommended)" : "Streaming (experimental)"}
                </label>
              ))}
            </div>
          </div>
          <div>
            <Label className="text-xs text-slate-500 dark:text-slate-400">Timezone</Label>
            <div className="flex items-center gap-2 mt-1">
              <span className="text-sm font-medium text-slate-700 dark:text-slate-300 bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800 rounded-md px-3 py-1.5">{businessTimezone}</span>
              <a href="/settings" className="text-xs text-cyan-600 hover:underline">Change in Settings</a>
            </div>
            <p className="text-[10px] text-slate-400 dark:text-slate-500 mt-1">Synced from your business settings. Used by Voice AI to interpret caller pickup times correctly.</p>
          </div>
        </CardContent>
      </Card>

      {/* Reservations */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300">Reservations</CardTitle>
        </CardHeader>
        <CardContent>
          <Label className="text-xs text-slate-500 dark:text-slate-400">Reservation Hold Duration (hours)</Label>
          <Input type="number" min={1} max={168} value={form.reservationHoldHours} onChange={(e) => set("reservationHoldHours", Math.max(1, Math.min(168, Number(e.target.value) || 1)))} className="w-32" />
          <p className="text-[10px] text-slate-400 dark:text-slate-500 mt-1">Hours to hold a reservation before auto-expiring. 4 hours = same-day pickup; 24 = next-day.</p>
        </CardContent>
      </Card>

      {/* Location */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300">Location</CardTitle>
        </CardHeader>
        <CardContent>
          <Label className="text-xs text-slate-500 dark:text-slate-400">Business Address</Label>
          <Input value={form.address ?? ""} onChange={(e) => set("address", e.target.value || null)} placeholder="e.g. 12 Lekki Road, Lagos" maxLength={500} />
          <p className="text-[10px] text-slate-400 dark:text-slate-500 mt-1">The AI uses this to confirm pickup location with callers (e.g. {'"'}see you at 12 Lekki Road{'"'}).</p>
        </CardContent>
      </Card>

      {/* Fallback */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300">Fallback</CardTitle>
        </CardHeader>
        <CardContent>
          <Label className="text-xs text-slate-500 dark:text-slate-400">Handoff Phone Number</Label>
          <Input value={form.fallbackHandoffPhone ?? ""} onChange={(e) => set("fallbackHandoffPhone", e.target.value || null)} placeholder="+2348012345678" className="w-64" />
          <p className="text-[10px] text-slate-400 dark:text-slate-500 mt-1">If the AI can{"'"}t help, calls forward to this number. Leave blank to play a closing message.</p>
        </CardContent>
      </Card>

      {/* Save */}
      <div className="flex items-center justify-between pt-2">
        <div>
          {saveResult && (
            <p className={`text-sm ${saveResult.ok ? "text-emerald-600" : "text-red-500"}`}>{saveResult.msg}</p>
          )}
        </div>
        <Button onClick={handleSave} disabled={saving || !hasChanges} size="lg">
          <Save size={16} className="mr-2" />
          {saving ? "Saving..." : "Save Changes"}
        </Button>
      </div>
    </div>
  );
}

// ── Suspended View ───────────────────────────────────────────────────────────

function SuspendedView() {
  return (
    <div className="space-y-6 max-w-lg mx-auto text-center">
      <div className="inline-flex items-center justify-center w-16 h-16 rounded-full bg-red-100 mb-2">
        <AlertTriangle size={28} className="text-red-500" />
      </div>
      <h2 className="text-2xl font-bold text-slate-900 dark:text-slate-50">Voice AI is Inactive</h2>
      <p className="text-slate-500 dark:text-slate-400">Your Voice AI subscription has been suspended due to billing. Resubscribe to reactivate your AI receptionist.</p>
      <Card>
        <CardContent className="pt-6">
          <Button onClick={() => window.location.href = "/settings"} className="w-full" size="lg">Go to Settings to Resubscribe</Button>
        </CardContent>
      </Card>
    </div>
  );
}

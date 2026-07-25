"use client";

import { useState, useEffect, useRef } from "react";
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
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/empty-state";
import { Phone, CheckCircle, AlertTriangle, Clock, Save, ShoppingCart, Receipt as ReceiptIcon, Package, ArrowRight, Activity as ActivityIcon } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import {
  VOICE_AI_ANNUAL_DISCOUNT,
  VOICE_AI_TIER_CODES,
  VOICE_AI_TIER_LABELS,
  VOICE_AI_TIER_FEATURES,
  VOICE_AI_TIER_TAGLINES,
  VOICE_AI_TRIAL_MINUTES,
  type VoiceAITier,
} from "@/lib/voice-ai-pricing";
import { CURRENCY_META, SUPPORTED_CURRENCIES, getProvider } from "@/lib/pricing";
import type { SupportedCurrency, BillingCycle } from "@/lib/pricing";
import { useVoicePricing } from "@/lib/use-pricing";

type VoiceLang = "en" | "fr" | "es" | "zh" | "ar";

type VoicePreset = "warm_female" | "professional_male" | "energetic_youthful";
const VOICE_PRESETS: { value: VoicePreset; label: string }[] = [
  { value: "warm_female", label: "Warm & Friendly (Female)" },
  { value: "professional_male", label: "Professional (Male)" },
  // "energetic_youthful" is a male voice for now (temporary placeholder); a future backend
  // release swaps it to a young female persona with the same enum key — no UI change needed then.
  { value: "energetic_youthful", label: "Energetic & Youthful (Male)" },
];

type VoiceAISettings = {
  id: string;
  name: string;
  defaultLanguage: VoiceLang;
  strictLanguage: boolean;
  greetingTemplate: string | null;
  botName: string | null;
  region: string | null;
  voiceTransport: "record" | "streaming";
  voicePreset: VoicePreset | null;
  elevenLabsVoiceId: string | null;
  elevenLabsVoiceIds: Record<string, string> | null;
  voiceNumberExternal: string | null;
  fallbackHandoffPhone: string | null;
  reservationHoldHours: number;
  address: string | null;
};

const LANG_LABELS: Record<VoiceLang, string> = {
  en: "English",
  fr: "French (Français)", es: "Spanish (Español)", zh: "Mandarin (中文)",
  ar: "Arabic (العربية)",
};
const LANGUAGES: VoiceLang[] = ["en", "fr", "es", "zh", "ar"];

// Greeting placeholder by selected default language — illustrative; the merchant overwrites it.
const GREETING_PLACEHOLDERS: Record<VoiceLang, string> = {
  en: "e.g. Welcome to {businessName}. How may I help you today?",
  fr: "ex: Bienvenue chez {businessName}. Comment je peux t'aider ?",
  es: "ej: Bienvenido a {businessName}. ¿En qué te puedo ayudar?",
  zh: "例: 欢迎来到{businessName}。需要什么帮助？",
  ar: "مثال: مرحباً بك في {businessName}. كيف يمكنني مساعدتك؟",
};

// Non-default languages that can carry their own ElevenLabs voice (advanced section).
const PER_LANGUAGE_VOICE_LANGS: { code: VoiceLang; label: string }[] = [
  { code: "fr", label: "French" },
  { code: "es", label: "Spanish" },
  { code: "zh", label: "Mandarin" },
  { code: "ar", label: "Arabic" },
];

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

  // Live Voice prices from the backend (single source of truth) — no hardcoded numbers.
  const { getVoiceTierPrice } = useVoicePricing();
  const [subscribing, setSubscribing] = useState<string | null>(null);

  // OjunaiVoice self-serve checkout for hosted-redirect providers (Stripe for USD/GBP/CAD/EUR,
  // Paystack for NGN). African currencies (Flutterwave) keep the concierge "contact us" flow —
  // the inline checkout SDK isn't wired into this marketing view.
  async function handleSubscribeVoice(tier: string) {
    setSubscribing(tier);
    try {
      const { data } = await api.post<{ data: { provider?: string; paymentUrl?: string } }>(
        "/subscription/voice-ai/initialize",
        { tier, billingCycle: cycle, currency },
      );
      const result = data.data ?? {};
      if ((result.provider === "stripe" || result.provider === "paystack") && result.paymentUrl) {
        window.location.href = result.paymentUrl;
        return;
      }
      setSubscribing(null);
      window.alert("Couldn't start checkout for this currency here — please contact us to enable OjunaiVoice.");
    } catch {
      setSubscribing(null);
      window.alert("Could not start checkout. Please try again or contact support.");
    }
  }

  return (
    <div className="space-y-6 max-w-5xl mx-auto">
      <div className="text-center">
        <div className="inline-flex items-center justify-center w-16 h-16 rounded-full bg-violet-100 mb-4">
          <Phone size={28} className="text-violet-600" />
        </div>
        <h2 className="text-2xl font-bold text-slate-900 dark:text-slate-50">OjunaiVoice</h2>
        <p className="text-slate-500 dark:text-slate-400 mt-2 max-w-md mx-auto">
          AI-powered phone receptionist that handles customer calls in English, French, Spanish, and Mandarin.
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
          const price = getVoiceTierPrice(tier, cycle, currency);
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
                {getProvider(currency) === "flutterwave" ? (
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
                ) : (
                  <button
                    type="button"
                    onClick={() => handleSubscribeVoice(tier)}
                    disabled={subscribing !== null}
                    className={`flex items-center justify-center gap-2 w-full py-2.5 rounded-lg text-sm font-semibold transition-colors disabled:opacity-60 ${
                      isPro
                        ? "bg-violet-600 hover:bg-violet-700 text-white"
                        : "bg-slate-100 hover:bg-slate-200 dark:bg-slate-800 dark:hover:bg-slate-700 text-slate-900 dark:text-slate-50"
                    }`}
                  >
                    <Phone size={14} />
                    {subscribing === tier ? "Starting checkout…" : "Subscribe"}
                  </button>
                )}
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
    // Keep other logged-in devices in sync: revalidate when the tab regains focus, so a
    // save made on one device shows up when you return to another. (Was staleTime:Infinity
    // + no focus refetch, which froze every other device on its first load.)
    staleTime: 15_000,
    refetchOnWindowFocus: true,
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
  const [confirmStreaming, setConfirmStreaming] = useState(false);
  // Holds a not-yet-applied default-language change while we ask the merchant what to do with
  // their custom greeting (which is literal text and would otherwise play in the old language).
  const [pendingLang, setPendingLang] = useState<VoiceLang | null>(null);
  const [presetError, setPresetError] = useState<string | null>(null);
  const [voicesOpen, setVoicesOpen] = useState(
    !!(initial.elevenLabsVoiceIds && Object.keys(initial.elevenLabsVoiceIds).length)
  );
  // Custom voice ID + per-language overrides start expanded only for merchants who already
  // set one (so their config stays visible); otherwise collapsed — the preset is the primary input.
  const [advancedOpen, setAdvancedOpen] = useState(
    !!(initial.elevenLabsVoiceId || (initial.elevenLabsVoiceIds && Object.keys(initial.elevenLabsVoiceIds).length))
  );

  // When a focus-refetch pulls newer settings (e.g. saved on another device), adopt them —
  // but only if this form has no unsaved edits, so we never clobber what the merchant is
  // typing. React Query's structural sharing keeps `initial`'s reference stable when the
  // server data is unchanged, so this runs only on a genuine change.
  const loadedRef = useRef(initial);
  useEffect(() => {
    const loaded = loadedRef.current;
    if (initial === loaded) return;
    loadedRef.current = initial;
    setForm(current =>
      JSON.stringify(current) === JSON.stringify(loaded) ? initial : current,
    );
  }, [initial]);

  function set<K extends keyof VoiceAISettings>(key: K, value: VoiceAISettings[K]) {
    setForm(f => ({ ...f, [key]: value }));
    setSaveResult(null);
  }

  // Per-language ElevenLabs voice IDs live in one JSON map; an edit replaces the whole object so
  // getDiff() (which JSON-compares this field) sends the complete map, per the API contract.
  function setVoiceId(lang: VoiceLang, id: string) {
    const next: Record<string, string> = { ...(form.elevenLabsVoiceIds ?? {}) };
    if (id.trim()) next[lang] = id.trim(); else delete next[lang];
    set("elevenLabsVoiceIds", Object.keys(next).length ? next : null);
  }

  // Wipe every raw-voice override so the chosen persona takes full effect. Clears BOTH the
  // per-language map and the single fallback (the backend nulls both per its clear-semantics).
  function clearAllOverrides() {
    setForm(f => ({ ...f, elevenLabsVoiceId: null, elevenLabsVoiceIds: null }));
    setSaveResult(null);
  }

  function getDiff(): Partial<VoiceAISettings> {
    const diff: Record<string, unknown> = {};
    for (const key of Object.keys(form) as (keyof VoiceAISettings)[]) {
      if (key === "id") continue;
      const changed = key === "elevenLabsVoiceIds"
        ? JSON.stringify(form[key] ?? null) !== JSON.stringify(initial[key] ?? null)
        : form[key] !== initial[key];
      if (changed) diff[key] = form[key];
    }
    return diff;
  }

  async function handleSave() {
    const diff = getDiff();
    if (Object.keys(diff).length === 0) { setSaveResult({ ok: true, msg: "No changes to save." }); return; }

    setSaving(true);
    setSaveResult(null);
    setPresetError(null);
    try {
      await api.patch("/business/voice-ai-settings", { ...diff, timezone: businessTimezone });
      qc.invalidateQueries({ queryKey: ["voice-ai-settings"] });
      setSaveResult({ ok: true, msg: "Settings saved." });
    } catch (err: unknown) {
      const ax = err as { response?: { status?: number; data?: { errors?: string[]; message?: string } } };
      const msg = ax.response?.data?.errors?.[0] ?? ax.response?.data?.message ?? "Failed to save.";
      // The voice backend validates the voicePreset enum; a 400 means an invalid value, so surface it
      // inline on the field rather than as a banner. Any other status stays in the save banner.
      if (ax.response?.status === 400) setPresetError(msg);
      else setSaveResult({ ok: false, msg });
    } finally {
      setSaving(false);
    }
  }

  const hasChanges = Object.keys(getDiff()).length > 0;
  const greetingPlaceholder = GREETING_PLACEHOLDERS[form.defaultLanguage] ?? GREETING_PLACEHOLDERS.en;
  // Short language name (drops the native-script parenthetical, e.g. "French (Français)" → "French") for prose.
  const pendingLangName = pendingLang ? LANG_LABELS[pendingLang].replace(/\s*\(.*\)$/, "") : "";

  // Voice resolution order is: per-language override → preset → raw voice ID. So when a preset AND
  // per-language overrides both exist, the overrides silently win for those languages — surface that
  // (the overrides otherwise hide inside the collapsed Advanced section) so the preset isn't "ignored".
  const overrideCodes = form.elevenLabsVoiceIds ? Object.keys(form.elevenLabsVoiceIds) : [];
  const presetOverrideConflict = !!form.voicePreset && overrideCodes.length > 0;
  const overrideNames = overrideCodes
    .map((c) => (LANG_LABELS as Record<string, string>)[c]?.replace(/\s*\(.*\)$/, "") ?? c.toUpperCase())
    .join(", ");
  const presetLabel = form.voicePreset
    ? (VOICE_PRESETS.find((p) => p.value === form.voicePreset)?.label ?? form.voicePreset)
    : "";

  return (
    <div className="space-y-4">
      {/* ── General ─────────────────────────────────────────────── */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300">General</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <div>
            <Label className="text-xs text-slate-500 dark:text-slate-400">Business Name</Label>
            <Input dir="auto" value={form.name} onChange={(e) => set("name", e.target.value)} maxLength={200} placeholder="Your business name" />
            <p className="text-[10px] text-slate-400 dark:text-slate-500 mt-1">Read aloud by the bot when callers ask who they&apos;ve reached.</p>
          </div>

          <div>
            <Label className="text-xs text-slate-500 dark:text-slate-400">Default Language</Label>
            <select
              value={form.defaultLanguage}
              onChange={(e) => {
                const next = e.target.value as VoiceLang;
                if (next === form.defaultLanguage) return;
                // A blank greeting auto-localizes to the new language, so only prompt when the
                // merchant has authored a literal greeting that would keep playing in the old language.
                if (form.greetingTemplate?.trim()) setPendingLang(next);
                else set("defaultLanguage", next);
              }}
              className="mt-1 h-9 w-full max-w-xs px-2 rounded-md border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 text-sm"
            >
              {LANGUAGES.map((l) => <option key={l} value={l}>{LANG_LABELS[l]}</option>)}
            </select>
            <p className="text-[10px] text-slate-400 dark:text-slate-500 mt-1">The language your customers usually call in. The bot greets and replies in this language by default; it&apos;ll auto-switch if a caller speaks differently — unless you lock it below.</p>
          </div>

          <div>
            <label className="flex items-start gap-3 cursor-pointer">
              <input
                type="checkbox"
                className="mt-1 h-4 w-4 rounded border-slate-300 dark:border-slate-700 text-cyan-600 focus:ring-cyan-500"
                checked={form.strictLanguage ?? false}
                onChange={(e) => set("strictLanguage", e.target.checked)}
              />
              <div>
                <p className="text-sm font-medium text-slate-700 dark:text-slate-300">Strict language lock</p>
                <p className="text-xs text-slate-400 dark:text-slate-500">When on, the bot always replies in your default language even if a caller speaks something else. Leave off if you sometimes serve customers in other languages.</p>
              </div>
            </label>
          </div>

          <div>
            <Label className="text-xs text-slate-500 dark:text-slate-400">Greeting</Label>
            <textarea
              // dir="auto" so a greeting typed in Arabic (or any RTL language) renders
              // right-to-left instead of being forced LTR and looking mangled.
              dir="auto"
              className="w-full h-24 p-3 mt-1 rounded-md border border-slate-200 dark:border-slate-800 text-sm resize-none bg-white dark:bg-slate-900"
              maxLength={500}
              placeholder={greetingPlaceholder}
              value={form.greetingTemplate ?? ""}
              onChange={(e) => set("greetingTemplate", e.target.value || null)}
            />
            <div className="flex items-start justify-between gap-3 mt-1">
              <p className="text-[10px] text-slate-400 dark:text-slate-500">What the bot says when a caller first connects. Leave blank for an auto-generated greeting in your default language.</p>
              <p className="text-[10px] text-slate-400 dark:text-slate-500 flex-shrink-0">{form.greetingTemplate?.length ?? 0}/500</p>
            </div>
          </div>

          <div>
            <Label className="text-xs text-slate-500 dark:text-slate-400">Bot Name <span className="text-slate-400">(optional)</span></Label>
            <Input dir="auto" value={form.botName ?? ""} onChange={(e) => set("botName", e.target.value || null)} maxLength={50} placeholder="e.g. Tomi" className="max-w-xs" />
            <p className="text-[10px] text-slate-400 dark:text-slate-500 mt-1">Give your bot a persona name. Customers hear &quot;Hi, I&apos;m Tomi from {form.name || "your business"}&quot; instead of a generic &quot;assistant&quot;.</p>
          </div>

          <div>
            <Label className="text-xs text-slate-500 dark:text-slate-400">Region <span className="text-slate-400">(optional)</span></Label>
            <Input value={form.region ?? ""} onChange={(e) => set("region", e.target.value || null)} maxLength={50} placeholder="e.g. Lagos" className="max-w-xs" />
            <p className="text-[10px] text-slate-400 dark:text-slate-500 mt-1">A cultural hint so the bot sounds local (e.g. Lagos, Paris, Quebec).</p>
          </div>

          <div>
            <Label className="text-xs text-slate-500 dark:text-slate-400">Timezone</Label>
            <div className="flex items-center gap-2 mt-1">
              <span className="text-sm font-medium text-slate-700 dark:text-slate-300 bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800 rounded-md px-3 py-1.5">{businessTimezone}</span>
              <a href="/settings" className="text-xs text-cyan-600 hover:underline">Change in Settings</a>
            </div>
            <p className="text-[10px] text-slate-400 dark:text-slate-500 mt-1">Timezone derived from your country set in business settings. Used by Voice AI to interpret caller times correctly.</p>
          </div>
        </CardContent>
      </Card>

      {/* ── Voice ───────────────────────────────────────────────── */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300">Voice</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <div>
            <Label className="text-xs text-slate-500 dark:text-slate-400">Voice Transport</Label>
            <p className="text-[10px] text-slate-400 dark:text-slate-500 mb-2">Streaming (recommended) — sub-second responses, mid-call language switching, custom voices. Record — legacy mode, kept for compatibility.</p>
            <div className="flex gap-2">
              {(["streaming", "record"] as const).map((t) => (
                <label key={t} className={`flex items-center gap-1.5 px-3 py-1.5 rounded-md border cursor-pointer text-sm ${form.voiceTransport === t ? "border-cyan-300 bg-cyan-50 text-cyan-700" : "border-slate-200 dark:border-slate-800 text-slate-600 dark:text-slate-400"}`}>
                  <input
                    type="radio"
                    name="transport"
                    value={t}
                    checked={form.voiceTransport === t}
                    onChange={() => {
                      if (t === "streaming" && form.voiceTransport !== "streaming") setConfirmStreaming(true);
                      else set("voiceTransport", t);
                    }}
                    className="sr-only"
                  />
                  {t === "streaming" ? "Streaming (recommended)" : "Record (legacy)"}
                </label>
              ))}
            </div>
          </div>

          <div>
            <Label className="text-xs text-slate-500 dark:text-slate-400">Voice personality <span className="text-slate-400">(recommended)</span></Label>
            <select
              value={form.voicePreset ?? ""}
              onChange={(e) => { setPresetError(null); set("voicePreset", (e.target.value || null) as VoicePreset | null); }}
              className="w-full max-w-md h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 text-sm text-slate-700 dark:text-slate-200"
            >
              <option value="">Default (use raw voice ID)</option>
              {VOICE_PRESETS.map((p) => (
                <option key={p.value} value={p.value}>{p.label}</option>
              ))}
            </select>
            <p className="text-[10px] text-slate-400 dark:text-slate-500 mt-1">Your bot&apos;s voice personality. Same feel across every language the bot speaks in. Pick one persona instead of hunting for individual ElevenLabs voice IDs.</p>
            {presetError && <p className="text-[10px] text-red-500 mt-1">{presetError}</p>}
          </div>

          {presetOverrideConflict && (
            <div className="rounded-md border border-amber-200 bg-amber-50 dark:border-amber-900/50 dark:bg-amber-950/30 p-3">
              <p className="text-xs text-amber-800 dark:text-amber-300">
                <span className="font-semibold">Per-language voice overrides are active.</span>{" "}
                Your custom voice for {overrideNames} takes priority over the “{presetLabel}” personality for{" "}
                {overrideCodes.length === 1 ? "that language" : "those languages"} — so the personality won&apos;t apply
                there. Clear {overrideCodes.length === 1 ? "it" : "them"} to use the personality everywhere, or keep{" "}
                {overrideCodes.length === 1 ? "it" : "them"} to mix both.
              </p>
              <button
                type="button"
                onClick={clearAllOverrides}
                className="mt-2 text-xs font-semibold text-amber-800 dark:text-amber-300 underline hover:no-underline"
              >
                Clear all overrides
              </button>
            </div>
          )}

          <div>
            <button type="button" onClick={() => setAdvancedOpen(o => !o)} className="text-xs font-medium text-slate-600 dark:text-slate-300 hover:text-slate-900 dark:hover:text-slate-100">
              {advancedOpen ? "▾" : "▸"} Advanced: custom voice ID <span className="text-slate-400 font-normal">(optional)</span>
            </button>
            {advancedOpen && (
              <div className="mt-2 space-y-3 pl-3 border-l-2 border-slate-100 dark:border-slate-800">
                {(form.elevenLabsVoiceId || overrideCodes.length > 0) && (
                  <div className="flex justify-end">
                    <button type="button" onClick={clearAllOverrides} className="text-[10px] font-medium text-rose-600 hover:underline">
                      Clear all overrides
                    </button>
                  </div>
                )}

                <div>
                  <Label className="text-xs text-slate-500 dark:text-slate-400">Custom voice ID <span className="text-slate-400">(optional)</span></Label>
                  <Input value={form.elevenLabsVoiceId ?? ""} onChange={(e) => set("elevenLabsVoiceId", e.target.value || null)} placeholder="ElevenLabs voice ID" className="max-w-md font-mono text-xs" />
                  <p className="text-[10px] text-slate-400 dark:text-slate-500 mt-1">Overrides the persona with one specific voice. Browse <a href="https://elevenlabs.io/voice-library" target="_blank" rel="noreferrer" className="text-cyan-600 hover:underline">elevenlabs.io/voice-library</a> and paste a voice ID.</p>
                </div>

                <div>
                  <button type="button" onClick={() => setVoicesOpen(o => !o)} className="text-xs font-medium text-slate-600 dark:text-slate-300 hover:text-slate-900 dark:hover:text-slate-100">
                    {voicesOpen ? "▾" : "▸"} Native voices per language
                  </button>
                  {voicesOpen && (
                    <div className="mt-2 space-y-2 pl-3 border-l-2 border-slate-100 dark:border-slate-800">
                      <p className="text-[10px] text-slate-400 dark:text-slate-500">If you serve customers in multiple languages, pick a native-speaker voice for each. A voice set here takes priority over your voice personality for that language.</p>
                      {PER_LANGUAGE_VOICE_LANGS.map(({ code, label }) => (
                        <div key={code} className="flex items-center gap-2">
                          <span className="text-xs text-slate-500 dark:text-slate-400 w-20 flex-shrink-0">{label}</span>
                          <Input
                            value={form.elevenLabsVoiceIds?.[code] ?? ""}
                            onChange={(e) => setVoiceId(code, e.target.value)}
                            placeholder="voice ID"
                            className="max-w-xs font-mono text-xs"
                          />
                          {form.elevenLabsVoiceIds?.[code] && (
                            <button
                              type="button"
                              onClick={() => setVoiceId(code, "")}
                              aria-label={`Clear ${label} override`}
                              className="text-slate-400 hover:text-rose-600 text-base leading-none px-1 flex-shrink-0"
                            >
                              ×
                            </button>
                          )}
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </div>
            )}
          </div>
        </CardContent>
      </Card>

      {/* ── Calls ───────────────────────────────────────────────── */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300">Calls</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <div>
            <Label className="text-xs text-slate-500 dark:text-slate-400">Voice Number <span className="text-slate-400">(optional)</span></Label>
            <Input value={form.voiceNumberExternal ?? ""} onChange={(e) => set("voiceNumberExternal", e.target.value || null)} placeholder="+2348012345678" className="w-64" />
            <p className="text-[10px] text-slate-400 dark:text-slate-500 mt-1">Set only if your voice line uses a different number from WhatsApp.</p>
          </div>

          <div>
            <Label className="text-xs text-slate-500 dark:text-slate-400">Handoff Phone Number</Label>
            <Input value={form.fallbackHandoffPhone ?? ""} onChange={(e) => set("fallbackHandoffPhone", e.target.value || null)} placeholder="+2348012345678" className="w-64" />
            <p className="text-[10px] text-slate-400 dark:text-slate-500 mt-1">When a caller asks for a human, the bot transfers to this number. Leave blank to play a closing message.</p>
          </div>

          <div>
            <Label className="text-xs text-slate-500 dark:text-slate-400">Reservation Hold Duration (hours)</Label>
            <Input type="number" min={1} max={48} value={form.reservationHoldHours} onChange={(e) => set("reservationHoldHours", Math.max(1, Math.min(48, Number(e.target.value) || 1)))} className="w-32" />
            <p className="text-[10px] text-slate-400 dark:text-slate-500 mt-1">Hours to hold a reservation before auto-expiring. 4 = same-day pickup; 24 = next-day.</p>
          </div>

          <div>
            <Label className="text-xs text-slate-500 dark:text-slate-400">Business Address</Label>
            <Input value={form.address ?? ""} onChange={(e) => set("address", e.target.value || null)} placeholder="e.g. 12 Lekki Road, Lagos" maxLength={500} />
            <p className="text-[10px] text-slate-400 dark:text-slate-500 mt-1">The bot uses this to confirm pickup location with callers.</p>
          </div>
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

      {/* Confirm before switching to the streaming engine — never auto-flip an existing merchant. */}
      <Dialog open={confirmStreaming} onOpenChange={(o) => !o && setConfirmStreaming(false)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Switch to the streaming engine?</DialogTitle>
          </DialogHeader>
          <p className="text-sm text-slate-600 dark:text-slate-400">
            This switches your call flow to the new streaming engine — sub-second responses, mid-call language switching, and custom voices. Your calls currently use the legacy Record mode. Continue?
          </p>
          <DialogFooter>
            <Button variant="outline" onClick={() => setConfirmStreaming(false)}>Cancel</Button>
            <Button onClick={() => { set("voiceTransport", "streaming"); setConfirmStreaming(false); }}>Continue</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* A saved greeting is literal text, so switching default language would keep playing it in the old
          language. Offer to clear it (→ localized auto-default) or keep it. Only shown when a custom
          greeting exists — the language <select> onChange applies blank-greeting changes directly. */}
      <Dialog open={pendingLang !== null} onOpenChange={(o) => { if (!o) setPendingLang(null); }}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Update your greeting for {pendingLangName}?</DialogTitle>
          </DialogHeader>
          <p className="text-sm text-slate-600 dark:text-slate-400">
            Your default language is now {pendingLangName}, but your saved greeting plays exactly as written — it won&apos;t be translated automatically. Keep it as-is, or clear it so the bot uses an auto-generated {pendingLangName} greeting.
          </p>
          <DialogFooter>
            <Button variant="outline" onClick={() => { if (pendingLang) set("defaultLanguage", pendingLang); setPendingLang(null); }}>Keep my greeting</Button>
            <Button onClick={() => { if (pendingLang) { set("defaultLanguage", pendingLang); set("greetingTemplate", null); } setPendingLang(null); }}>Clear it</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
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

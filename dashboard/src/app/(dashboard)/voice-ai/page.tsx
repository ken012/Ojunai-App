"use client";

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { usePlanStatus } from "@/lib/use-plan-status";
import { useBusiness } from "@/lib/data-sync";
import { api } from "@/lib/api";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { Phone, CheckCircle, AlertTriangle, Clock, Save } from "lucide-react";
import { VOICE_AI_PRICING, VOICE_AI_ANNUAL_DISCOUNT, VOICE_AI_FEATURES } from "@/lib/voice-ai-pricing";
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

export default function VoiceAIPage() {
  const { data: planStatus } = usePlanStatus();
  const business = useBusiness();

  if (!planStatus?.voiceAIFeatureVisible) {
    return (
      <div className="flex items-center justify-center min-h-[60vh]">
        <p className="text-slate-400">This feature is not available yet.</p>
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

  const price = VOICE_AI_PRICING[cycle]?.[currency] ?? 0;
  const monthlyEquiv = cycle === "annual" ? Math.round(price / 12) : price;
  const sym = CURRENCY_META[currency]?.symbol ?? currency;

  const contactSubject = encodeURIComponent("Voice AI — Enable for my business");
  const contactBody = encodeURIComponent("Hi BizPilot Team,\n\nI'm interested in enabling Voice AI for my business.\n\nPlease get in touch to set it up.\n\nThank you.");

  return (
    <div className="space-y-6 max-w-2xl mx-auto">
      <div className="text-center">
        <div className="inline-flex items-center justify-center w-16 h-16 rounded-full bg-violet-100 mb-4">
          <Phone size={28} className="text-violet-600" />
        </div>
        <h2 className="text-2xl font-bold text-slate-900">Voice AI Inventory Control Specialist</h2>
        <p className="text-slate-500 mt-2 max-w-md mx-auto">
          An AI-powered phone assistant that handles customer calls 24/7 — stock checks,
          reservations, bookings, and more. In English, Yoruba, Hausa, and Igbo.
        </p>
      </div>
      <Card>
        <CardContent className="pt-6">
          <div className="space-y-3 mb-6">
            {VOICE_AI_FEATURES.map((f) => (
              <div key={f} className="flex items-start gap-2">
                <CheckCircle size={16} className="text-emerald-500 mt-0.5 flex-shrink-0" />
                <span className="text-sm text-slate-700">{f}</span>
              </div>
            ))}
          </div>
          <div className="border-t pt-5">
            <p className="text-xs text-slate-500 text-center mb-3">Starting from</p>
            <div className="flex items-center justify-center gap-2 mb-4">
              <button onClick={() => setCycle("monthly")} className={`px-4 py-1.5 text-sm font-medium rounded-md transition-colors ${cycle === "monthly" ? "bg-slate-900 text-white" : "bg-slate-100 text-slate-600"}`}>Monthly</button>
              <button onClick={() => setCycle("annual")} className={`px-4 py-1.5 text-sm font-medium rounded-md transition-colors ${cycle === "annual" ? "bg-slate-900 text-white" : "bg-slate-100 text-slate-600"}`}>Annual <span className="ml-1 text-xs text-emerald-500">-{VOICE_AI_ANNUAL_DISCOUNT}%</span></button>
            </div>
            <div className="flex items-center justify-center gap-3 mb-4">
              <select value={currency} onChange={(e) => setCurrency(e.target.value as SupportedCurrency)} className="h-8 px-2 rounded-md border border-slate-200 text-xs">
                {SUPPORTED_CURRENCIES.map((c) => <option key={c} value={c}>{CURRENCY_META[c].symbol} {c}</option>)}
              </select>
            </div>
            <div className="text-center mb-5">
              <p className="text-3xl font-bold text-slate-900">{sym}{price.toLocaleString()}</p>
              <p className="text-xs text-slate-400">{cycle === "annual" ? `per year (${sym}${monthlyEquiv.toLocaleString()}/mo)` : "per month"}</p>
            </div>
            <a
              href={`mailto:hello@bizpilot-ai.com?subject=${contactSubject}&body=${contactBody}`}
              className="flex items-center justify-center gap-2 w-full py-3 bg-violet-600 hover:bg-violet-700 text-white rounded-lg text-sm font-semibold transition-colors"
            >
              <Phone size={16} />
              Contact Our Team to Get Started
            </a>
            <p className="text-[11px] text-slate-400 text-center mt-3">
              Our team will walk you through the setup, configure your phone number, and get your AI receptionist live within 24 hours.
            </p>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

// ── Enabled View (with settings) ─────────────────────────────────────────────

function EnabledView({ planStatus, business }: {
  planStatus: { voiceAIPlanStatus: string; voiceAITrialDaysLeft: number | null; voiceAITrialEndsAt: string | null; voiceAISubscriptionEndsAt: string | null };
  business: { name?: string; accountNumber?: string; timezone?: string } | null;
}) {
  const isTrial = planStatus.voiceAIPlanStatus === "trial";
  const daysLeft = planStatus.voiceAITrialDaysLeft;

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
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold text-slate-900">Voice AI</h2>
          <p className="text-slate-500 text-sm mt-0.5">Configure your AI phone Inventory Control Specialist</p>
        </div>
        <Badge className={isTrial ? "bg-amber-100 text-amber-700" : "bg-emerald-100 text-emerald-700"}>
          {isTrial ? `Trial — ${daysLeft} day${daysLeft !== 1 ? "s" : ""} left` : "Active"}
        </Badge>
      </div>

      {isTrial && daysLeft !== null && daysLeft <= 5 && (
        <div className="bg-amber-50 border border-amber-200 rounded-xl p-4 flex items-start gap-3">
          <Clock size={18} className="text-amber-500 flex-shrink-0 mt-0.5" />
          <div>
            <p className="text-sm font-semibold text-amber-800">Trial ending soon</p>
            <p className="text-xs text-amber-600 mt-0.5">Your Voice AI trial expires in {daysLeft} day{daysLeft !== 1 ? "s" : ""}. Subscribe to keep it active.</p>
          </div>
        </div>
      )}

      {/* Read-only info */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700">Account Info</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="grid grid-cols-2 sm:grid-cols-3 gap-4 text-sm">
            <div><span className="text-slate-500 block text-xs">Status</span><span className="font-medium">{isTrial ? "Trial" : "Active"}</span></div>
            {business?.accountNumber && <div><span className="text-slate-500 block text-xs">Account #</span><span className="font-mono font-medium">{business.accountNumber}</span></div>}
            {planStatus.voiceAISubscriptionEndsAt && <div><span className="text-slate-500 block text-xs">{isTrial ? "Trial ends" : "Renews"}</span><span className="font-medium">{new Date(isTrial ? planStatus.voiceAITrialEndsAt! : planStatus.voiceAISubscriptionEndsAt).toLocaleDateString()}</span></div>}
          </div>
        </CardContent>
      </Card>

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
          <CardTitle className="text-sm font-semibold text-slate-700">Greeting</CardTitle>
          <p className="text-xs text-slate-400 mt-0.5">What callers hear when they pick up. Plain text, no SSML. Blank = generic greeting.</p>
        </CardHeader>
        <CardContent>
          <div className="flex gap-1 mb-3">
            {LANGUAGES.map((lang) => (
              <button key={lang} onClick={() => setGreetingTab(lang)}
                className={`px-3 py-1 text-xs font-medium rounded-md transition-colors ${greetingTab === lang ? "bg-slate-900 text-white" : "bg-slate-100 text-slate-600 hover:bg-slate-200"}`}
              >{LANG_LABELS[lang]}</button>
            ))}
          </div>
          {greetingTab === "en" && <textarea className="w-full h-24 p-3 rounded-md border border-slate-200 text-sm resize-none" maxLength={500} placeholder="Hi, you've reached our store. How can I help you today?" value={form.greetingTemplateEn ?? ""} onChange={(e) => set("greetingTemplateEn", e.target.value || null)} />}
          {greetingTab === "yo" && <textarea className="w-full h-24 p-3 rounded-md border border-slate-200 text-sm resize-none" maxLength={500} placeholder="Yoruba greeting template..." value={form.greetingTemplateYo ?? ""} onChange={(e) => set("greetingTemplateYo", e.target.value || null)} />}
          {greetingTab === "ha" && <textarea className="w-full h-24 p-3 rounded-md border border-slate-200 text-sm resize-none" maxLength={500} placeholder="Hausa greeting template..." value={form.greetingTemplateHa ?? ""} onChange={(e) => set("greetingTemplateHa", e.target.value || null)} />}
          {greetingTab === "ig" && <textarea className="w-full h-24 p-3 rounded-md border border-slate-200 text-sm resize-none" maxLength={500} placeholder="Igbo greeting template..." value={form.greetingTemplateIg ?? ""} onChange={(e) => set("greetingTemplateIg", e.target.value || null)} />}
          <p className="text-[10px] text-slate-400 mt-1 text-right">{(greetingTab === "en" ? form.greetingTemplateEn : greetingTab === "yo" ? form.greetingTemplateYo : greetingTab === "ha" ? form.greetingTemplateHa : form.greetingTemplateIg)?.length ?? 0}/500</p>
        </CardContent>
      </Card>

      {/* Behavior */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700">Behavior</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <div>
            <Label className="text-xs text-slate-500">Business Name</Label>
            <Input value={form.name} onChange={(e) => set("name", e.target.value)} maxLength={200} placeholder="Your business name" />
            <p className="text-[10px] text-slate-400 mt-1">Business name read aloud by the AI when callers ask.</p>
          </div>
          <div>
            <Label className="text-xs text-slate-500">Default Language</Label>
            <p className="text-[10px] text-slate-400 mb-2">Language the AI greets in if it can{"'"}t auto-detect the caller{"'"}s language.</p>
            <div className="flex gap-2">
              {LANGUAGES.map((lang) => (
                <label key={lang} className={`flex items-center gap-1.5 px-3 py-1.5 rounded-md border cursor-pointer text-sm ${form.defaultLanguage === lang ? "border-sky-300 bg-sky-50 text-sky-700" : "border-slate-200 text-slate-600"}`}>
                  <input type="radio" name="defaultLang" value={lang} checked={form.defaultLanguage === lang} onChange={() => set("defaultLanguage", lang)} className="sr-only" />
                  {LANG_LABELS[lang]}
                </label>
              ))}
            </div>
          </div>
          <div>
            <Label className="text-xs text-slate-500">Voice Transport</Label>
            <p className="text-[10px] text-slate-400 mb-2">Record: TwiML turn-by-turn (reliable, recommended). Streaming: real-time via Twilio ConversationRelay (lower latency, experimental).</p>
            <div className="flex gap-2">
              {(["record", "streaming"] as const).map((t) => (
                <label key={t} className={`flex items-center gap-1.5 px-3 py-1.5 rounded-md border cursor-pointer text-sm ${form.voiceTransport === t ? "border-sky-300 bg-sky-50 text-sky-700" : "border-slate-200 text-slate-600"}`}>
                  <input type="radio" name="transport" value={t} checked={form.voiceTransport === t} onChange={() => set("voiceTransport", t)} className="sr-only" />
                  {t === "record" ? "Record (recommended)" : "Streaming (experimental)"}
                </label>
              ))}
            </div>
          </div>
          <div>
            <Label className="text-xs text-slate-500">Timezone</Label>
            <div className="flex items-center gap-2 mt-1">
              <span className="text-sm font-medium text-slate-700 bg-slate-50 border border-slate-200 rounded-md px-3 py-1.5">{businessTimezone}</span>
              <a href="/settings" className="text-xs text-sky-600 hover:underline">Change in Settings</a>
            </div>
            <p className="text-[10px] text-slate-400 mt-1">Synced from your business settings. Used by Voice AI to interpret caller pickup times correctly.</p>
          </div>
        </CardContent>
      </Card>

      {/* Reservations */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700">Reservations</CardTitle>
        </CardHeader>
        <CardContent>
          <Label className="text-xs text-slate-500">Reservation Hold Duration (hours)</Label>
          <Input type="number" min={1} max={168} value={form.reservationHoldHours} onChange={(e) => set("reservationHoldHours", Math.max(1, Math.min(168, Number(e.target.value) || 1)))} className="w-32" />
          <p className="text-[10px] text-slate-400 mt-1">Hours to hold a reservation before auto-expiring. 4 hours = same-day pickup; 24 = next-day.</p>
        </CardContent>
      </Card>

      {/* Location */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700">Location</CardTitle>
        </CardHeader>
        <CardContent>
          <Label className="text-xs text-slate-500">Business Address</Label>
          <Input value={form.address ?? ""} onChange={(e) => set("address", e.target.value || null)} placeholder="e.g. 12 Lekki Road, Lagos" maxLength={500} />
          <p className="text-[10px] text-slate-400 mt-1">The AI uses this to confirm pickup location with callers (e.g. {'"'}see you at 12 Lekki Road{'"'}).</p>
        </CardContent>
      </Card>

      {/* Fallback */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700">Fallback</CardTitle>
        </CardHeader>
        <CardContent>
          <Label className="text-xs text-slate-500">Handoff Phone Number</Label>
          <Input value={form.fallbackHandoffPhone ?? ""} onChange={(e) => set("fallbackHandoffPhone", e.target.value || null)} placeholder="+2348012345678" className="w-64" />
          <p className="text-[10px] text-slate-400 mt-1">If the AI can{"'"}t help, calls forward to this number. Leave blank to play a closing message.</p>
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
      <h2 className="text-2xl font-bold text-slate-900">Voice AI is Inactive</h2>
      <p className="text-slate-500">Your Voice AI subscription has been suspended due to billing. Resubscribe to reactivate your AI receptionist.</p>
      <Card>
        <CardContent className="pt-6">
          <Button onClick={() => window.location.href = "/settings"} className="w-full" size="lg">Go to Settings to Resubscribe</Button>
        </CardContent>
      </Card>
    </div>
  );
}

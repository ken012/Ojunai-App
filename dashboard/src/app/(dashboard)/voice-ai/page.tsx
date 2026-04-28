"use client";

import { useState } from "react";
import { usePlanStatus } from "@/lib/use-plan-status";
import { useBusiness } from "@/lib/data-sync";
import { api } from "@/lib/api";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Phone, CheckCircle, AlertTriangle, Clock, Zap } from "lucide-react";
import { VOICE_AI_PRICING, VOICE_AI_ANNUAL_DISCOUNT, VOICE_AI_FEATURES } from "@/lib/voice-ai-pricing";
import { CURRENCY_META, SUPPORTED_CURRENCIES } from "@/lib/pricing";
import type { SupportedCurrency, BillingCycle } from "@/lib/pricing";

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

function MarketingView({ currency: defaultCurrency }: { currency: SupportedCurrency }) {
  const [cycle, setCycle] = useState<BillingCycle>("monthly");
  const [currency, setCurrency] = useState<SupportedCurrency>(defaultCurrency);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const price = VOICE_AI_PRICING[cycle]?.[currency] ?? 0;
  const monthlyEquiv = cycle === "annual" ? Math.round(price / 12) : price;
  const sym = CURRENCY_META[currency]?.symbol ?? currency;

  async function handleEnable() {
    setLoading(true);
    setError(null);
    try {
      const { data } = await api.post<{ data: { paymentUrl?: string; provider?: string; inlineCheckout?: boolean; [key: string]: unknown } }>(
        "/subscription/voice-ai/initialize",
        { currency, billingCycle: cycle }
      );
      const result = data.data;
      if (result?.paymentUrl) {
        window.location.href = result.paymentUrl;
      }
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to start checkout.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="space-y-6 max-w-2xl mx-auto">
      <div className="text-center">
        <div className="inline-flex items-center justify-center w-16 h-16 rounded-full bg-violet-100 mb-4">
          <Phone size={28} className="text-violet-600" />
        </div>
        <h2 className="text-2xl font-bold text-slate-900">Voice AI Receptionist</h2>
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
            <div className="flex items-center justify-center gap-2 mb-4">
              <button
                onClick={() => setCycle("monthly")}
                className={`px-4 py-1.5 text-sm font-medium rounded-md transition-colors ${
                  cycle === "monthly" ? "bg-slate-900 text-white" : "bg-slate-100 text-slate-600 hover:bg-slate-200"
                }`}
              >
                Monthly
              </button>
              <button
                onClick={() => setCycle("annual")}
                className={`px-4 py-1.5 text-sm font-medium rounded-md transition-colors ${
                  cycle === "annual" ? "bg-slate-900 text-white" : "bg-slate-100 text-slate-600 hover:bg-slate-200"
                }`}
              >
                Annual
                <span className="ml-1 text-xs text-emerald-500">-{VOICE_AI_ANNUAL_DISCOUNT}%</span>
              </button>
            </div>

            <div className="flex items-center justify-center gap-3 mb-4">
              <select
                value={currency}
                onChange={(e) => setCurrency(e.target.value as SupportedCurrency)}
                className="h-8 px-2 rounded-md border border-slate-200 text-xs"
              >
                {SUPPORTED_CURRENCIES.map((c) => (
                  <option key={c} value={c}>{CURRENCY_META[c].symbol} {c}</option>
                ))}
              </select>
            </div>

            <div className="text-center mb-5">
              <p className="text-3xl font-bold text-slate-900">{sym}{price.toLocaleString()}</p>
              <p className="text-xs text-slate-400">
                {cycle === "annual" ? `per year (${sym}${monthlyEquiv.toLocaleString()}/mo)` : "per month"}
              </p>
            </div>

            {error && <p className="text-xs text-red-500 text-center mb-3">{error}</p>}

            <Button onClick={handleEnable} disabled={loading} className="w-full" size="lg">
              <Zap size={16} className="mr-2" />
              {loading ? "Starting checkout..." : "Enable Voice AI"}
            </Button>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

function EnabledView({ planStatus, business }: { planStatus: { voiceAIPlanStatus: string; voiceAITrialDaysLeft: number | null; voiceAITrialEndsAt: string | null; voiceAISubscriptionEndsAt: string | null }; business: { name?: string; accountNumber?: string } | null }) {
  const isTrial = planStatus.voiceAIPlanStatus === "trial";
  const daysLeft = planStatus.voiceAITrialDaysLeft;

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold text-slate-900">Voice AI</h2>
          <p className="text-slate-500 text-sm mt-0.5">Your AI-powered phone receptionist</p>
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
            <p className="text-xs text-amber-600 mt-0.5">
              Your Voice AI trial expires in {daysLeft} day{daysLeft !== 1 ? "s" : ""}. Subscribe to keep your AI receptionist active.
            </p>
          </div>
        </div>
      )}

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-semibold text-slate-700">Status</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="space-y-2">
              <div className="flex justify-between text-sm">
                <span className="text-slate-500">Plan</span>
                <span className="font-medium">{isTrial ? "Trial" : "Active Subscription"}</span>
              </div>
              {planStatus.voiceAISubscriptionEndsAt && (
                <div className="flex justify-between text-sm">
                  <span className="text-slate-500">{isTrial ? "Trial ends" : "Renews"}</span>
                  <span className="font-medium">{new Date(isTrial ? planStatus.voiceAITrialEndsAt! : planStatus.voiceAISubscriptionEndsAt).toLocaleDateString()}</span>
                </div>
              )}
              {business?.accountNumber && (
                <div className="flex justify-between text-sm">
                  <span className="text-slate-500">Account #</span>
                  <span className="font-mono font-medium">{business.accountNumber}</span>
                </div>
              )}
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-semibold text-slate-700">Setup</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="space-y-3 text-sm text-slate-600">
              <p>Your Voice AI receptionist is ready. To connect it to your business phone:</p>
              <ol className="list-decimal list-inside space-y-1 text-xs text-slate-500">
                <li>Contact BizPilot support with your account number</li>
                <li>We{"'"}ll configure your phone number for call forwarding</li>
                <li>Customize your greeting and business hours in the Voice AI admin</li>
              </ol>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

function SuspendedView() {
  return (
    <div className="space-y-6 max-w-lg mx-auto text-center">
      <div className="inline-flex items-center justify-center w-16 h-16 rounded-full bg-red-100 mb-2">
        <AlertTriangle size={28} className="text-red-500" />
      </div>
      <h2 className="text-2xl font-bold text-slate-900">Voice AI is Inactive</h2>
      <p className="text-slate-500">
        Your Voice AI subscription has been suspended due to billing. Resubscribe to reactivate your AI receptionist.
      </p>
      <Card>
        <CardContent className="pt-6">
          <Button
            onClick={() => window.location.href = "/settings"}
            className="w-full"
            size="lg"
          >
            Go to Settings to Resubscribe
          </Button>
        </CardContent>
      </Card>
    </div>
  );
}

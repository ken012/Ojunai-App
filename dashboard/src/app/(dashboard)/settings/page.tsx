"use client";

import { useState, useEffect, useRef, Suspense } from "react";
import { useSearchParams } from "next/navigation";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { getStoredUser } from "@/lib/auth";
import { api, absoluteApiUrl } from "@/lib/api";
import type { UserDto } from "@/lib/types";
import { PageHeader } from "@/components/page-header";
import { useToast } from "@/components/toast";
import { useBusiness, useUser, useDataSync } from "@/lib/data-sync";
import { usePlanStatus } from "@/lib/use-plan-status";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Separator } from "@/components/ui/separator";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { PasswordStrengthHint } from "@/components/password-strength-hint";
import { validatePassword } from "@/lib/password-policy";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import { MessageSquare, Building2, User, Pencil, Bell, Tags, X, Plus, Users, Trash2, KeyRound, CreditCard, Phone, FileText, Save, CheckCircle2, ImageIcon, Upload, Lock, Send, Link as LinkIcon, ExternalLink } from "lucide-react";
import { CATEGORY_NAMES } from "@/lib/categories";
import { hasPermission, Permission } from "@/lib/permissions";
import { InstallSettingsCard } from "@/components/install-settings-card";
import { SettingsSection } from "@/components/settings-section";
import { SettingsNav } from "@/components/settings-nav";
import {
  type SupportedCurrency,
  SUPPORTED_CURRENCIES,
  CURRENCY_META,
  PRICING,
  getPrice,
  getMonthlyEquivalent,
  formatPrice,
  getDefaultCurrency,
  toBillingCurrency,
} from "@/lib/pricing";
import { QuotaMeter } from "@/components/quota-meter";
import { WhatsAppPackPicker } from "@/components/whatsapp-pack-picker";

const CURRENCIES = [
  "NGN", "GHS", "KES", "ZAR", "TZS", "UGX", "RWF", "XAF", "XOF", "EGP", "ETB",
  "CDF", "AOA", "MZN", "ZMW", "USD", "BWP", "NAD", "MWK", "SLE", "LRD", "GMD",
  "GBP", "EUR", "CAD"
];

const COUNTRIES: Record<string, { currency: string }> = {
  "Nigeria": { currency: "NGN" },
  "Ghana": { currency: "GHS" },
  "Kenya": { currency: "KES" },
  "South Africa": { currency: "ZAR" },
  "Tanzania": { currency: "TZS" },
  "Uganda": { currency: "UGX" },
  "Rwanda": { currency: "RWF" },
  "Cameroon": { currency: "XAF" },
  "Senegal": { currency: "XOF" },
  "Ivory Coast": { currency: "XOF" },
  "Egypt": { currency: "EGP" },
  "Ethiopia": { currency: "ETB" },
  "DR Congo": { currency: "CDF" },
  "Angola": { currency: "AOA" },
  "Mozambique": { currency: "MZN" },
  "Zambia": { currency: "ZMW" },
  "Zimbabwe": { currency: "USD" },
  "Botswana": { currency: "BWP" },
  "Namibia": { currency: "NAD" },
  "Malawi": { currency: "MWK" },
  "Benin": { currency: "XOF" },
  "Togo": { currency: "XOF" },
  "Sierra Leone": { currency: "SLE" },
  "Liberia": { currency: "LRD" },
  "Gambia": { currency: "GMD" },
};

const COUNTRY_NAMES = Object.keys(COUNTRIES).sort();

// Twilio sandbox — update if you move to a production WhatsApp sender
const TWILIO_WHATSAPP_NUMBER = "14155238886";
const TWILIO_JOIN_CODE = "join knife-wait";
const TWILIO_WA_LINK = `https://wa.me/${TWILIO_WHATSAPP_NUMBER}?text=${encodeURIComponent(TWILIO_JOIN_CODE)}`;

export default function SettingsPageWrapper() {
  return (
    <Suspense fallback={null}>
      <SettingsPage />
    </Suspense>
  );
}

function SettingsPage() {
  const syncBusiness = useBusiness();
  const syncUser = useUser();
  const { refresh: refreshSync } = useDataSync();
  const { toast } = useToast();
  const [user, setUser] = useState<ReturnType<typeof getStoredUser>>(null);
  const [business, setBusiness] = useState<ReturnType<typeof useBusiness>>(null);
  const [editing, setEditing] = useState(false);
  const [mounted, setMounted] = useState(false);
  const searchParams = useSearchParams();
  const [showSuccess, setShowSuccess] = useState(false);

  useEffect(() => {
    if (syncBusiness) setBusiness(syncBusiness);
  }, [syncBusiness]);
  useEffect(() => {
    if (syncUser) setUser(syncUser);
  }, [syncUser]);

  useEffect(() => {
    const status = searchParams.get("status");
    const txRef = searchParams.get("tx_ref");
    const txId = searchParams.get("transaction_id");

    if (status === "successful" && (txRef || txId)) {
      window.history.replaceState({}, "", "/settings");
      (async () => {
        try {
          await api.post("/subscription/verify-flutterwave", {
            transactionId: txId ?? undefined,
            txRef: txRef ?? undefined,
          });
          setShowSuccess(true);
          setTimeout(() => setShowSuccess(false), 8000);
        } catch {
          setShowSuccess(true);
          setTimeout(() => setShowSuccess(false), 8000);
        }
      })();
    } else if (searchParams.get("subscribed") === "true") {
      setShowSuccess(true);
      window.history.replaceState({}, "", "/settings");
      setTimeout(() => setShowSuccess(false), 8000);
    }
  }, [searchParams]);

  const cs = CURRENCY_META[(business?.currency ?? "NGN") as SupportedCurrency]?.symbol ?? business?.currency ?? "₦";

  if (!mounted && typeof window !== "undefined") {
    setUser(getStoredUser());
    setMounted(true);
  }

  return (
    <div className="space-y-6">
      <PageHeader title="Settings" subtitle="Account and business information" />

      {showSuccess && (
        <div className="rounded-lg border border-green-200 bg-green-50 px-4 py-3 flex items-center justify-between max-w-3xl">
          <p className="text-sm text-green-800 font-medium">Payment successful! Your plan is now active.</p>
          <button onClick={() => setShowSuccess(false)} className="text-green-600 hover:text-green-800">
            <X size={16} />
          </button>
        </div>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-[200px_1fr] lg:gap-10">
        {/* Sticky section nav with active-state highlight (desktop only) */}
        <SettingsNav
          items={[
            { href: "#install", label: "Install on phone" },
            { href: "#business", label: "Business" },
            { href: "#receipts", label: "Receipts" },
            { href: "#plan", label: "Plan & Billing" },
            { href: "#alerts", label: "Alerts" },
            { href: "#account", label: "Account" },
            { href: "#voice-ai", label: "Voice AI" },
            { href: "#whatsapp", label: "WhatsApp" },
            { href: "#channels", label: "Connected Channels" },
            { href: "#team", label: "Team" },
            { href: "#categories", label: "Categories" },
          ]}
        />

        {/* Settings sections */}
        <div className="space-y-6 max-w-2xl">

      <SettingsSection id="install" title="Install on phone" icon={<Phone size={14} />}>
        <InstallSettingsCard />
      </SettingsSection>

      <SettingsSection id="business" title="Business" icon={<Building2 size={14} />}>
      {/* Business */}
      <Card>
        <CardHeader className="pb-3 flex flex-row items-start justify-between">
          <div>
            <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300 flex items-center gap-2">
              <Building2 size={15} className="text-slate-500 dark:text-slate-400" />
              Business
            </CardTitle>
            <p className="text-xs text-slate-500 dark:text-slate-400 mt-1">Your business details, currency, and address</p>
          </div>
          {hasPermission(Permission.ManageSettings) && (
            <button
              onClick={() => setEditing(true)}
              className="p-1.5 rounded-md hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-500 dark:text-slate-400 hover:text-slate-900 dark:hover:text-slate-50 transition-colors"
              title="Edit business"
            >
              <Pencil size={14} />
            </button>
          )}
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="flex justify-between">
            <span className="text-sm text-slate-500 dark:text-slate-400">Business Name</span>
            <span className="text-sm font-medium">{business?.name ?? "—"}</span>
          </div>
          <Separator />
          {business?.accountNumber && (
            <>
              <div className="flex justify-between items-center">
                <span className="text-sm text-slate-500 dark:text-slate-400">Account Number</span>
                <button
                  onClick={() => { navigator.clipboard.writeText(business.accountNumber!); }}
                  className="text-sm font-mono font-medium bg-slate-100 dark:bg-slate-800 px-2.5 py-0.5 rounded hover:bg-slate-200 dark:hover:bg-slate-700 transition-colors"
                  title="Click to copy"
                >
                  {business.accountNumber}
                </button>
              </div>
              <Separator />
            </>
          )}
          <div className="flex justify-between">
            <span className="text-sm text-slate-500 dark:text-slate-400">Type</span>
            <span className="text-sm">{business?.businessType ?? "—"}</span>
          </div>
          <Separator />
          <div className="flex justify-between">
            <span className="text-sm text-slate-500 dark:text-slate-400">Currency</span>
            <span className="text-sm font-mono">{business?.currency ?? "NGN"}</span>
          </div>
          <Separator />
          <div className="flex justify-between">
            <span className="text-sm text-slate-500 dark:text-slate-400">City</span>
            <span className="text-sm">{business?.city ?? "—"}</span>
          </div>
          <Separator />
          <div className="flex justify-between">
            <span className="text-sm text-slate-500 dark:text-slate-400">Town / State</span>
            <span className="text-sm">{business?.state ?? "—"}</span>
          </div>
          <Separator />
          <div className="flex justify-between">
            <span className="text-sm text-slate-500 dark:text-slate-400">Country</span>
            <span className="text-sm">{business?.country ?? "—"}</span>
          </div>
          <Separator />
          <div className="flex justify-between gap-4">
            <span className="text-sm text-slate-500 dark:text-slate-400 flex-shrink-0">Address</span>
            <span className="text-sm text-right">{business?.address ?? <span className="text-slate-300 dark:text-slate-600">—</span>}</span>
          </div>
          <Separator />
          <div className="flex justify-between">
            <span className="text-sm text-slate-500 dark:text-slate-400">Status</span>
            <Badge variant={business?.isActive ? "default" : "secondary"}>
              {business?.isActive ? "Active" : "Inactive"}
            </Badge>
          </div>
        </CardContent>
      </Card>

      {/* Custom dashboard branding — Pro/Business plans only */}
      {hasPermission(Permission.ManageSettings) && (
        <BrandingCard business={business} onUpdate={(updated) => {
          setBusiness(updated);
          if (typeof window !== "undefined") { localStorage.setItem("oj_business", JSON.stringify(updated)); refreshSync(); }
        }} />
      )}
      </SettingsSection>

      <SettingsSection id="receipts" title="Receipts" icon={<FileText size={14} />}>
      {/* Receipts — own section */}
      {hasPermission(Permission.ManageSettings) && (
        <ReceiptsCard
          business={business}
          onUpdate={(updated) => {
            setBusiness(updated);
            if (typeof window !== "undefined") {
              localStorage.setItem("oj_business", JSON.stringify(updated));
              refreshSync();
            }
          }}
        />
      )}
      </SettingsSection>

      <SettingsSection id="plan" title="Plan & Billing" icon={<CreditCard size={14} />}>
      {/* Current-month assistant action usage — sits above the plan picker so a merchant
          who's hit a cap sees the trigger right where they decide whether to upgrade. */}
      <QuotaMeter />
      <div className="h-3" />
      {/* WhatsApp pack — separately purchasable since WhatsApp has per-conversation fees;
          merchants who only use Telegram/Messenger never need this. */}
      <WhatsAppPackPicker />
      <div className="h-3" />
      {/* Plan */}
      <PlanCard business={business} />
      </SettingsSection>

      <SettingsSection id="alerts" title="Alerts" icon={<Bell size={14} />}>
      {hasPermission(Permission.ManageSettings) && <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300 flex items-center gap-2">
            <Bell size={15} className="text-amber-500" />
            Alerts
          </CardTitle>
          <p className="text-xs text-slate-500 dark:text-slate-400 mt-1">Stay on top of business events. Choose which channel they reach you on.</p>
        </CardHeader>
        <CardContent className="space-y-3">

          {/* ── WhatsApp channel ────────────────────────────────────────── */}
          <div className="rounded-lg border border-emerald-200 dark:border-emerald-900 bg-emerald-50/40 dark:bg-emerald-950/20 p-3">
            <div className="flex items-center justify-between mb-1">
              <p className="text-xs font-semibold uppercase tracking-wider text-emerald-700 dark:text-emerald-300 flex items-center gap-1.5">
                <MessageSquare size={12} />
                WhatsApp
              </p>
              <span className="text-[10px] font-medium uppercase tracking-wide px-1.5 py-px rounded-full bg-emerald-100 text-emerald-700 ring-1 ring-inset ring-emerald-200 dark:bg-emerald-950/40 dark:text-emerald-300 dark:ring-emerald-900">
                Owner only
              </span>
            </div>
            <p className="text-[11px] text-slate-500 dark:text-slate-400 mb-3">
              Sent as WhatsApp messages to the owner&apos;s registered number.
            </p>
            <div className="space-y-3">
              {[
                { key: "alertLowStock" as const, label: "Low Stock", desc: "When a product drops below its threshold after a sale" },
                { key: "alertDailySummary" as const, label: "Daily Summary", desc: "Sales, expenses, and net sent at 8 PM daily" },
              ].map(({ key, label, desc }) => (
                <label key={key} className="flex items-start gap-3 cursor-pointer">
                  <input
                    type="checkbox"
                    className="mt-1 h-4 w-4 rounded border-slate-300 dark:border-slate-700 text-cyan-600 focus:ring-cyan-500"
                    checked={business?.[key] ?? true}
                    onChange={async (e) => {
                      try {
                        const { data } = await api.put<{ data: typeof business }>("/business", { [key]: e.target.checked });
                        const updated = data.data!;
                        setBusiness(updated);
                        if (typeof window !== "undefined") { localStorage.setItem("oj_business", JSON.stringify(updated)); refreshSync(); }
                      } catch (err: unknown) {
                        const ax = err as { response?: { data?: { errors?: string[] } } };
                        toast.error("Couldn't save change", ax.response?.data?.errors?.[0] ?? "Please try again.");
                      }
                    }}
                  />
                  <div>
                    <p className="text-sm font-medium text-slate-700 dark:text-slate-300">{label}</p>
                    <p className="text-xs text-slate-400 dark:text-slate-500">{desc}</p>
                  </div>
                </label>
              ))}
              <label className="flex items-start gap-3 cursor-pointer">
                <input
                  type="checkbox"
                  className="mt-1 h-4 w-4 rounded border-slate-300 dark:border-slate-700 text-cyan-600 focus:ring-cyan-500"
                  checked={business?.alertLargeSale ?? true}
                  onChange={async (e) => {
                    try {
                      const { data } = await api.put<{ data: typeof business }>("/business", { alertLargeSale: e.target.checked });
                      const updated = data.data!;
                      setBusiness(updated);
                      if (typeof window !== "undefined") { localStorage.setItem("oj_business", JSON.stringify(updated)); refreshSync(); }
                    } catch (err: unknown) {
                      const ax = err as { response?: { data?: { errors?: string[] } } };
                      toast.error("Couldn't save change", ax.response?.data?.errors?.[0] ?? "Please try again.");
                    }
                  }}
                />
                <div>
                  <p className="text-sm font-medium text-slate-700 dark:text-slate-300">Large Sale Alert</p>
                  <p className="text-xs text-slate-400 dark:text-slate-500">When a sale exceeds the threshold</p>
                </div>
              </label>
              {business?.alertLargeSale && (
                <div className="ml-7 space-y-3">
                  <div>
                    <Label className="text-xs">Alert Threshold</Label>
                    <Input
                      type="number"
                      value={business?.largeSaleThreshold?.toString() ?? "100000"}
                      placeholder="e.g. 100000"
                      onChange={async (e) => {
                        const val = Number(e.target.value);
                        try {
                          const { data } = await api.put<{ data: typeof business }>("/business", { largeSaleThreshold: val || 100000 });
                          const updated = data.data!;
                          setBusiness(updated);
                          if (typeof window !== "undefined") { localStorage.setItem("oj_business", JSON.stringify(updated)); refreshSync(); }
                        } catch (err: unknown) {
                          const ax = err as { response?: { data?: { errors?: string[] } } };
                          toast.error("Couldn't save change", ax.response?.data?.errors?.[0] ?? "Please try again.");
                        }
                      }}
                    />
                    <p className="text-xs text-slate-400 dark:text-slate-500 mt-1">Alert when a sale exceeds {cs}{(business?.largeSaleThreshold ?? 100000).toLocaleString()}</p>
                  </div>

                  {/* Per-source toggles: which sales channels should trigger the alert?
                      Defaults on the API are all true so existing behavior (alert on any channel)
                      is preserved. Owners can untoggle individual channels to mute noisy ones. */}
                  <div className="pt-2 border-t border-slate-100 dark:border-slate-800">
                    <p className="text-xs font-medium text-slate-600 dark:text-slate-400 mb-2">Trigger alerts from</p>
                    <div className="space-y-1.5">
                      {([
                        { key: "largeSaleAlertWhatsApp", label: "WhatsApp" },
                        { key: "largeSaleAlertTelegram", label: "Telegram" },
                        { key: "largeSaleAlertMessenger", label: "Facebook Messenger" },
                        { key: "largeSaleAlertDashboard", label: "Dashboard" },
                      ] as const).map(({ key, label }) => {
                        const current = (business as unknown as Record<string, boolean | undefined> | undefined)?.[key];
                        return (
                          <label key={key} className="flex items-center gap-2 cursor-pointer">
                            <input
                              type="checkbox"
                              className="h-4 w-4 rounded border-slate-300 dark:border-slate-700 text-cyan-600 focus:ring-cyan-500"
                              checked={current ?? true}
                              onChange={async (e) => {
                                try {
                                  const { data } = await api.put<{ data: typeof business }>("/business", { [key]: e.target.checked });
                                  const updated = data.data!;
                                  setBusiness(updated);
                                  if (typeof window !== "undefined") { localStorage.setItem("oj_business", JSON.stringify(updated)); refreshSync(); }
                                } catch (err: unknown) {
                                  const ax = err as { response?: { data?: { errors?: string[] } } };
                                  toast.error("Couldn't save change", ax.response?.data?.errors?.[0] ?? "Please try again.");
                                }
                              }}
                            />
                            <span className="text-xs text-slate-700 dark:text-slate-300">{label}</span>
                          </label>
                        );
                      })}
                    </div>
                  </div>
                </div>
              )}
            </div>
          </div>

          {/* ── Telegram channel (Phase 6) ────────────────────────────────
              Same alert toggles as WhatsApp would have, but they fire on the
              user's Telegram chat instead. AlertChannel preference on the user
              row decides which messaging channel actually receives. */}
          <AlertChannelCard channel="telegram" />

          {/* ── Messenger channel — live as of Phase 3e ─────────────────── */}
          <AlertChannelCard channel="messenger" />

          {/* ── Dashboard channel ─────────────────────────────────────────
              In-app bell. Internally split into Business (toggleable, Owner/Admin
              only) and Personal (always-on security alerts for the user). */}
          <div className="rounded-lg border border-cyan-200 dark:border-cyan-900 bg-cyan-50/40 dark:bg-cyan-950/20 p-3">
            <div className="flex items-center justify-between mb-1">
              <p className="text-xs font-semibold uppercase tracking-wider text-cyan-700 dark:text-cyan-300 flex items-center gap-1.5">
                <Bell size={12} />
                Dashboard
              </p>
            </div>
            <p className="text-[11px] text-slate-500 dark:text-slate-400 mb-3">
              Shown in the bell in the top corner of the dashboard.
            </p>

            {/* Business sub-block */}
            <div className="rounded-lg border border-violet-200 dark:border-violet-900 bg-white dark:bg-slate-950/50 p-3">
              <div className="flex items-center justify-between mb-1">
                <p className="text-xs font-semibold uppercase tracking-wider text-violet-700 dark:text-violet-300">
                  Business
                </p>
                <span className="text-[10px] font-medium uppercase tracking-wide px-1.5 py-px rounded-full bg-violet-100 text-violet-700 ring-1 ring-inset ring-violet-200 dark:bg-violet-950/40 dark:text-violet-300 dark:ring-violet-900">
                  Owner / Admin only
                </span>
              </div>
              <p className="text-[11px] text-slate-500 dark:text-slate-400 mb-3">
                Operational alerts about the whole business. Sales, Bookkeeper, and Viewer roles never see these.
              </p>
              <div className="space-y-3">
                {[
                  { key: "alertDashboardLowStock" as const, label: "Low Stock", desc: "When a product drops below its threshold" },
                  { key: "alertDashboardDailySummary" as const, label: "Daily Summary", desc: "Yesterday's revenue, expenses, and net at the start of each day" },
                  { key: "alertDashboardLargeSale" as const, label: "Large Sale", desc: "When a sale exceeds the threshold above" },
                  { key: "alertDashboardAgedReceivable" as const, label: "Aged Receivables", desc: "When a customer has owed you for 30+ days" },
                  { key: "alertDashboardStaffChanges" as const, label: "Staff Added or Removed", desc: "When team membership changes" },
                ].map(({ key, label, desc }) => (
                  <label key={key} className="flex items-start gap-3 cursor-pointer">
                    <input
                      type="checkbox"
                      className="mt-1 h-4 w-4 rounded border-slate-300 dark:border-slate-700 text-cyan-600 focus:ring-cyan-500"
                      checked={(business as unknown as Record<string, boolean | undefined>)?.[key] ?? true}
                      onChange={async (e) => {
                        try {
                          const { data } = await api.put<{ data: typeof business }>("/business", { [key]: e.target.checked });
                          const updated = data.data!;
                          setBusiness(updated);
                          if (typeof window !== "undefined") { localStorage.setItem("oj_business", JSON.stringify(updated)); refreshSync(); }
                        } catch (err: unknown) {
                          const ax = err as { response?: { data?: { errors?: string[] } } };
                          toast.error("Couldn't save change", ax.response?.data?.errors?.[0] ?? "Please try again.");
                        }
                      }}
                    />
                    <div>
                      <p className="text-sm font-medium text-slate-700 dark:text-slate-300">{label}</p>
                      <p className="text-xs text-slate-400 dark:text-slate-500">{desc}</p>
                    </div>
                  </label>
                ))}

                {/* Daily sales goal — tick to enable, then enter the amount */}
                {(() => {
                  const goalRaw = (business as unknown as { dailySalesGoal?: number | null })?.dailySalesGoal;
                  const goalEnabled = typeof goalRaw === "number" && goalRaw > 0;

                  async function setGoal(val: number) {
                    try {
                      const { data } = await api.put<{ data: typeof business }>("/business", { dailySalesGoal: val });
                      const updated = data.data!;
                      setBusiness(updated);
                      if (typeof window !== "undefined") { localStorage.setItem("oj_business", JSON.stringify(updated)); refreshSync(); }
                    } catch (err: unknown) {
                      const ax = err as { response?: { data?: { errors?: string[] } } };
                      toast.error("Couldn't save change", ax.response?.data?.errors?.[0] ?? "Please try again.");
                    }
                  }

                  return (
                    <>
                      <label className="flex items-start gap-3 cursor-pointer">
                        <input
                          type="checkbox"
                          className="mt-1 h-4 w-4 rounded border-slate-300 dark:border-slate-700 text-cyan-600 focus:ring-cyan-500"
                          checked={goalEnabled}
                          onChange={(e) => setGoal(e.target.checked ? (goalRaw && goalRaw > 0 ? goalRaw : 100000) : 0)}
                        />
                        <div>
                          <p className="text-sm font-medium text-slate-700 dark:text-slate-300">Daily Sales Goal</p>
                          <p className="text-xs text-slate-400 dark:text-slate-500">Get a celebratory bell notification the moment today&apos;s revenue crosses your goal.</p>
                        </div>
                      </label>
                      {goalEnabled && (
                        <div className="ml-7">
                          <Label className="text-xs">Goal amount</Label>
                          <Input
                            type="number"
                            value={goalRaw?.toString() ?? ""}
                            placeholder="e.g. 100000"
                            onChange={(e) => {
                              const val = Number(e.target.value);
                              if (!Number.isNaN(val) && val >= 0) setGoal(val);
                            }}
                          />
                          <p className="text-xs text-slate-400 dark:text-slate-500 mt-1">
                            Notification fires once a day when sales cross {cs}{(goalRaw ?? 0).toLocaleString()}.
                          </p>
                        </div>
                      )}
                    </>
                  );
                })()}
              </div>
            </div>

            {/* Personal sub-block */}
            <div className="mt-3 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-950/50 p-3">
              <div className="flex items-center justify-between mb-1">
                <p className="text-xs font-semibold uppercase tracking-wider text-slate-600 dark:text-slate-300">
                  Personal
                </p>
                <span className="text-[10px] font-medium uppercase tracking-wide px-1.5 py-px rounded-full bg-slate-100 text-slate-600 ring-1 ring-inset ring-slate-200 dark:bg-slate-800 dark:text-slate-300 dark:ring-slate-700">
                  Always on
                </span>
              </div>
              <p className="text-[11px] text-slate-500 dark:text-slate-400 mb-2">
                Security alerts about your own account. These can&apos;t be disabled — they&apos;re your safety net if someone tries to take over the account.
              </p>
              <ul className="text-xs text-slate-600 dark:text-slate-400 space-y-1 ml-1">
                <li>• Password changed</li>
                <li>• Email verified</li>
                <li>• Account recovery used</li>
                <li>• Multiple failed login attempts</li>
                <li>• Trial ending (if you&apos;re Owner)</li>
              </ul>
            </div>
          </div>

        </CardContent>
      </Card>}
      </SettingsSection>

      <SettingsSection id="team" title="Team" icon={<Users size={14} />}>
      {/* Team Members — Owner/Admin only */}
      {hasPermission(Permission.ManageStaff) && <TeamMembersCard />}
      </SettingsSection>

      <SettingsSection id="categories" title="Categories" icon={<Tags size={14} />}>
      {/* Manage Categories — Owner/Admin only */}
      {hasPermission(Permission.ManageSettings) &&
      <ManageCategoriesCard business={business} onUpdate={(updated) => {
        setBusiness(updated);
        if (typeof window !== "undefined") { localStorage.setItem("oj_business", JSON.stringify(updated)); refreshSync(); }
      }} />}
      </SettingsSection>

      <SettingsSection id="account" title="Your Account" icon={<User size={14} />}>
      {/* User */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300 flex items-center gap-2">
            <User size={15} className="text-violet-500" />
            Your Account
          </CardTitle>
          <p className="text-xs text-slate-500 dark:text-slate-400 mt-1">Personal profile and login details</p>
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="flex justify-between">
            <span className="text-sm text-slate-500 dark:text-slate-400">Name</span>
            <span className="text-sm font-medium">{user?.fullName ?? "—"}</span>
          </div>
          <Separator />
          <div className="flex justify-between">
            <span className="text-sm text-slate-500 dark:text-slate-400">Phone</span>
            <span className="text-sm font-mono">{user?.phoneNumber ?? "—"}</span>
          </div>
          <Separator />
          <EmailField />
          <Separator />
          <div className="flex justify-between">
            <span className="text-sm text-slate-500 dark:text-slate-400">Role</span>
            <Badge variant="outline">{user?.role ?? "—"}</Badge>
          </div>
          <Separator />
          <DobField />
        </CardContent>
      </Card>
      </SettingsSection>

      <SettingsSection id="voice-ai" title="Voice AI" icon={<Phone size={14} />}>
      {/* Voice AI */}
      <VoiceAISettingsCard />
      </SettingsSection>

      <SettingsSection id="whatsapp" title="WhatsApp" icon={<MessageSquare size={14} />}>
      {/* WhatsApp */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300 flex items-center gap-2">
            <MessageSquare size={15} className="text-green-500" />
            WhatsApp Integration
          </CardTitle>
          <p className="text-xs text-slate-500 dark:text-slate-400 mt-1">How you and your customers interact with the bot</p>
        </CardHeader>
        <CardContent className="space-y-3">
          {/* Sale Confirmations sub-block — first because it's a behavior toggle the user
              actively manages, vs. the integration info below which is more reference. */}
          {hasPermission(Permission.ManageSettings) && (
            <div className="rounded-lg border border-emerald-200 dark:border-emerald-900 bg-emerald-50/40 dark:bg-emerald-950/20 p-3">
              <div className="flex items-center justify-between mb-1">
                <p className="text-xs font-semibold uppercase tracking-wider text-emerald-700 dark:text-emerald-300 flex items-center gap-1.5">
                  <CheckCircle2 size={12} />
                  Sale Confirmations
                </p>
              </div>
              <p className="text-[11px] text-slate-500 dark:text-slate-400 mb-3">
                When enabled, the bot asks for confirmation before recording sales above the threshold — useful for protecting against fat-fingered orders or staff mistakes.
              </p>
              <label className="flex items-start gap-3 cursor-pointer">
                <input
                  type="checkbox"
                  className="mt-1 h-4 w-4 rounded border-slate-300 dark:border-slate-700 text-cyan-600 focus:ring-cyan-500"
                  checked={business?.confirmLargeSales ?? false}
                  onChange={async (e) => {
                    try {
                      const { data } = await api.put<{ data: typeof business }>("/business", { confirmLargeSales: e.target.checked });
                      const updated = data.data!;
                      setBusiness(updated);
                      if (typeof window !== "undefined") { localStorage.setItem("oj_business", JSON.stringify(updated)); refreshSync(); }
                    } catch (err: unknown) {
                      const ax = err as { response?: { data?: { errors?: string[] } } };
                      toast.error("Couldn't save change", ax.response?.data?.errors?.[0] ?? "Please try again.");
                    }
                  }}
                />
                <div>
                  <p className="text-sm font-medium text-slate-700 dark:text-slate-300">Confirm large sales via WhatsApp</p>
                  <p className="text-xs text-slate-400 dark:text-slate-500">Bot will ask &quot;Confirm?&quot; before recording sales above the threshold below.</p>
                </div>
              </label>
              {business?.confirmLargeSales && (
                <div className="ml-7 mt-3">
                  <Label className="text-xs">Confirmation Threshold</Label>
                  <Input
                    type="number"
                    value={business?.confirmLargeSaleThreshold?.toString() ?? ""}
                    placeholder="e.g. 500000"
                    onChange={async (e) => {
                      const val = Number(e.target.value);
                      try {
                        const { data } = await api.put<{ data: typeof business }>("/business", { confirmLargeSaleThreshold: val || 0 });
                        const updated = data.data!;
                        setBusiness(updated);
                        if (typeof window !== "undefined") { localStorage.setItem("oj_business", JSON.stringify(updated)); refreshSync(); }
                      } catch (err: unknown) {
                        const ax = err as { response?: { data?: { errors?: string[] } } };
                        toast.error("Couldn't save change", ax.response?.data?.errors?.[0] ?? "Please try again.");
                      }
                    }}
                  />
                  <p className="text-xs text-slate-400 dark:text-slate-500 mt-1">Sales above {cs}{(business?.confirmLargeSaleThreshold ?? 0).toLocaleString()} will require WhatsApp confirmation.</p>
                </div>
              )}
            </div>
          )}

          <Separator className="my-2" />

          <p className="text-sm text-slate-600 dark:text-slate-400">
            Ojunai understands natural language. Here are some example commands you can send via WhatsApp:
          </p>
          <div className="grid grid-cols-1 gap-2 mt-2">
            {[
              "sell 5 bags of rice to Ade for 15k each",
              "spent 2k on transport",
              "received 10 bottles of oil from supplier",
              "Emeka paid me 8,500",
              "how much did I make today?",
              "which products are running low?",
              "how much does Kola owe me?",
            ].map((example) => (
              <div key={example} className="bg-slate-50 dark:bg-slate-950 border rounded-lg px-3 py-2 text-sm text-slate-700 dark:text-slate-300 font-mono">
                &ldquo;{example}&rdquo;
              </div>
            ))}
          </div>
          <p className="text-xs text-slate-400 dark:text-slate-500 mt-2">
            Messages are processed by AI and executed automatically when confidence is high.
          </p>

          <a
            href={TWILIO_WA_LINK}
            target="_blank"
            rel="noreferrer"
            className="flex items-center justify-center gap-2 w-full px-4 py-3 bg-green-500 hover:bg-green-600 text-white rounded-lg font-medium transition-colors mt-2"
          >
            <MessageSquare size={16} />
            Chat with Ojunai on WhatsApp
          </a>
        </CardContent>
      </Card>
      </SettingsSection>

      <SettingsSection id="channels" title="Connected Channels" icon={<LinkIcon size={14} />}>
        <ConnectedChannelsCard />
      </SettingsSection>

      <EditBusinessDialog
        business={business}
        open={editing}
        onClose={() => setEditing(false)}
        onSaved={(updated) => {
          setBusiness(updated);
          if (typeof window !== "undefined") {
            localStorage.setItem("oj_business", JSON.stringify(updated));
            refreshSync();
          }
        }}
      />
        </div>
      </div>
    </div>
  );
}

const STAFF_ROLES = ["Admin", "Sales", "Bookkeeper", "Viewer"];

type StaffMember = {
  id: string;
  fullName: string;
  phoneNumber: string;
  email?: string;
  role: string;
  isActive: boolean;
  permissions: string[];
  createdAtUtc: string;
};

function EmailField() {
  const user = useUser();
  const { refresh } = useDataSync();
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState("");
  const [saving, setSaving] = useState(false);
  const [sending, setSending] = useState(false);
  const [sent, setSent] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSave() {
    const trimmed = value.trim();
    if (!trimmed) {
      setError(user?.email
        ? "To remove your email, please contact support — it's your account recovery channel."
        : "Please enter an email address, or click Cancel.");
      return;
    }
    setSaving(true);
    setError(null);
    try {
      await api.put("/auth/email", { email: trimmed });
      await refresh();
      setEditing(false);
      setValue("");
      setSent(false); // reset banner state since email may have changed
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Couldn't update email.");
    } finally {
      setSaving(false);
    }
  }

  async function handleResend() {
    setSending(true);
    setError(null);
    try {
      await api.post("/auth/request-email-verification");
      setSent(true);
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Couldn't send the verification email.");
    } finally {
      setSending(false);
    }
  }

  if (editing) {
    return (
      <div className="space-y-1.5">
        <div className="flex items-center justify-between gap-2">
          <span className="text-sm text-slate-500 dark:text-slate-400 flex-shrink-0">Email</span>
          <div className="flex items-center gap-2 flex-1 max-w-sm">
            <Input
              type="email"
              value={value}
              onChange={(e) => setValue(e.target.value)}
              placeholder="email@example.com"
              autoFocus
              className="h-8 text-sm"
              onKeyDown={(e) => { if (e.key === "Enter") handleSave(); }}
            />
            <Button size="sm" onClick={handleSave} disabled={saving}>{saving ? "..." : "Save"}</Button>
            <Button size="sm" variant="outline" onClick={() => { setEditing(false); setValue(""); setError(null); }} disabled={saving}>Cancel</Button>
          </div>
        </div>
        <p className="text-[11px] text-slate-400 dark:text-slate-500">
          Changing your email will require re-verification. Used for account recovery if you lose your phone.
        </p>
        {error && <p className="text-[11px] text-red-500">{error}</p>}
      </div>
    );
  }

  return (
    <div className="space-y-1.5">
      <div className="flex items-center justify-between">
        <span className="text-sm text-slate-500 dark:text-slate-400">Email</span>
        <div className="flex items-center gap-2">
          <span className="text-sm">{user?.email ?? "—"}</span>
          {user?.email && (user.emailVerified
            ? <Badge className="bg-emerald-50 text-emerald-700 ring-1 ring-inset ring-emerald-200 text-[10px]">Verified</Badge>
            : <Badge className="bg-amber-50 text-amber-700 ring-1 ring-inset ring-amber-200 text-[10px]">Unverified</Badge>
          )}
          <button
            onClick={() => { setValue(user?.email ?? ""); setEditing(true); setError(null); }}
            className="p-1 rounded hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-400 dark:text-slate-500 hover:text-slate-700 dark:hover:text-slate-300"
            title={user?.email ? "Edit email" : "Add email"}
            type="button"
          >
            <Pencil size={12} />
          </button>
        </div>
      </div>
      {user?.email && !user.emailVerified && (
        <div className="flex items-center justify-between gap-2">
          <p className="text-[11px] text-slate-500 dark:text-slate-400">
            Verify so you can recover your account if you lose your phone.
          </p>
          <Button size="sm" variant="outline" onClick={handleResend} disabled={sending || sent}>
            {sending ? "Sending…" : sent ? "Email sent ✓" : "Send verification email"}
          </Button>
        </div>
      )}
      {error && <p className="text-[11px] text-red-500">{error}</p>}
    </div>
  );
}

function DobField() {
  const user = useUser();
  const { refresh } = useDataSync();
  const { toast } = useToast();
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState("");
  const [saving, setSaving] = useState(false);

  const hasDob = !!user?.dateOfBirth;

  async function handleSave() {
    if (!value || isNaN(Number(value))) return;
    setSaving(true);
    try {
      await api.put("/auth/date-of-birth", { dateOfBirth: `${value}-01-01` });
      await refresh();
      setEditing(false);
      setValue("");
    } catch (err: unknown) {
                    const ax = err as { response?: { data?: { errors?: string[] } } };
                    toast.error("Couldn't save change", ax.response?.data?.errors?.[0] ?? "Please try again.");
                  } finally {
      setSaving(false);
    }
  }

  if (editing) {
    return (
      <div className="flex items-center justify-between">
        <span className="text-sm text-slate-500 dark:text-slate-400">Birth Year</span>
        <div className="flex items-center gap-2">
          <input type="number" min="1920" max={new Date().getFullYear() - 13} placeholder="e.g. 1990"
            value={value} onChange={(e) => setValue(e.target.value)}
            className="h-8 w-24 px-2 rounded-md border border-slate-200 dark:border-slate-800 text-sm text-center" />
          <Button size="sm" onClick={handleSave} disabled={saving || !value}>{saving ? "..." : "Save"}</Button>
          <Button size="sm" variant="outline" onClick={() => setEditing(false)}>Cancel</Button>
        </div>
      </div>
    );
  }

  return (
    <div className="flex items-center justify-between">
      <span className="text-sm text-slate-500 dark:text-slate-400">Birth Year</span>
      <div className="flex items-center gap-2">
        <span className="text-sm text-slate-500 dark:text-slate-400">{hasDob ? "Saved" : "Not set"}</span>
        <button onClick={() => setEditing(true)} className="text-xs text-cyan-600 hover:underline">
          {hasDob ? "Change" : "Set"}
        </button>
      </div>
    </div>
  );
}

function VoiceAISettingsCard() {
  const { data: planStatus } = usePlanStatus();
  if (!planStatus?.voiceAIFeatureVisible) return null;

  const status = planStatus.voiceAIPlanStatus;
  const isActive = status === "active" || status === "trial";
  const isTrial = status === "trial";
  const isSuspended = status === "suspended";

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300 flex items-center gap-2">
          <Phone size={15} className="text-violet-500" />
          Voice AI Receptionist
          {isActive && (
            <Badge className={isTrial ? "bg-amber-100 text-amber-700 ml-auto" : "bg-emerald-100 text-emerald-700 ml-auto"}>
              {isTrial ? `Trial — ${planStatus.voiceAITrialDaysLeft}d left` : "Active"}
            </Badge>
          )}
          {isSuspended && (
            <Badge className="bg-red-100 text-red-700 ml-auto">Suspended</Badge>
          )}
        </CardTitle>
      </CardHeader>
      <CardContent>
        {isActive ? (
          <div className="space-y-2">
            <p className="text-sm text-slate-600 dark:text-slate-400">Your AI receptionist is active.</p>
            <a href="/voice-ai" className="text-sm text-cyan-600 hover:underline">Manage Voice AI settings</a>
          </div>
        ) : isSuspended ? (
          <div className="space-y-2">
            <p className="text-sm text-rose-600 dark:text-rose-400">Voice AI is inactive due to billing.</p>
            <a href="/voice-ai" className="text-sm text-cyan-600 hover:underline">Resubscribe</a>
          </div>
        ) : (
          <div className="space-y-2">
            <p className="text-sm text-slate-500 dark:text-slate-400">Add an AI phone receptionist that handles customer calls 24/7.</p>
            <a href="/voice-ai" className="text-sm text-cyan-600 hover:underline font-medium">Learn more and enable</a>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function TeamMembersCard() {
  const qc = useQueryClient();
  const { toast } = useToast();
  const [adding, setAdding] = useState(false);
  const [form, setForm] = useState({ fullName: "", phoneNumber: "", password: "", email: "", role: "Sales" });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [resetting, setResetting] = useState<StaffMember | null>(null);
  const [resetPassword, setResetPasswordValue] = useState("");
  const [resetError, setResetError] = useState<string | null>(null);
  const [resetSaving, setResetSaving] = useState(false);

  async function handleResetPassword() {
    if (!resetting) return;
    const v = validatePassword(resetPassword);
    if (!v.ok) { setResetError(v.reason ?? "Invalid password."); return; }
    setResetSaving(true);
    setResetError(null);
    try {
      await api.post(`/staff/${resetting.id}/reset-password`, { newPassword: resetPassword });
      toast.success(`Password reset for ${resetting.fullName}`, "They'll be required to change it on next login.");
      setResetting(null);
      setResetPasswordValue("");
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setResetError(ax.response?.data?.errors?.[0] ?? "Failed to reset password. Try again.");
    } finally {
      setResetSaving(false);
    }
  }

  const { data: planStatus } = usePlanStatus();
  const maxStaff = planStatus?.maxStaff ?? 1;
  const canAddStaff = maxStaff > 1;

  const { data: staff } = useQuery({
    queryKey: ["staff"],
    queryFn: async () => {
      const { data } = await api.get<{ data: StaffMember[] }>("/staff");
      return data.data!;
    },
  });

  async function handleAdd() {
    setSaving(true);
    setError(null);
    try {
      await api.post("/staff", {
        fullName: form.fullName,
        phoneNumber: form.phoneNumber,
        password: form.password,
        email: form.email || undefined,
        role: form.role,
      });
      qc.invalidateQueries({ queryKey: ["staff"] });
      setForm({ fullName: "", phoneNumber: "", password: "", email: "", role: "Sales" });
      setAdding(false);
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to add staff");
    } finally {
      setSaving(false);
    }
  }

  async function handleRemove(id: string) {
    try {
      await api.delete(`/staff/${id}`);
      qc.invalidateQueries({ queryKey: ["staff"] });
    } catch (err: unknown) {
                    const ax = err as { response?: { data?: { errors?: string[] } } };
                    toast.error("Couldn't save change", ax.response?.data?.errors?.[0] ?? "Please try again.");
                  }
  }

  return (
    <Card>
      <CardHeader className="pb-3 flex flex-row items-start justify-between">
        <div>
          <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300 flex items-center gap-2">
            <Users size={15} className="text-cyan-500" />
            Team Members
          </CardTitle>
          <p className="text-xs text-slate-500 dark:text-slate-400 mt-1">Staff who can access this business via WhatsApp or dashboard</p>
        </div>
        {canAddStaff && (
          <Button size="sm" variant="outline" onClick={() => setAdding(!adding)}>
            <Plus size={14} className="mr-1" /> Add Staff
          </Button>
        )}
      </CardHeader>
      <CardContent className="space-y-3">
        {!canAddStaff && (
          <div className="rounded-lg border border-dashed border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-800">
            Staff accounts aren&apos;t available on the Starter plan. Upgrade to Shop or higher to add team members.
          </div>
        )}
        {adding && canAddStaff && (
          <div className="border rounded-lg p-3 space-y-2 bg-slate-50 dark:bg-slate-950">
            <div className="grid grid-cols-2 gap-2">
              <div>
                <Label className="text-xs">Full Name</Label>
                <Input value={form.fullName} onChange={(e) => setForm({ ...form, fullName: e.target.value })} placeholder="Mary Johnson" />
              </div>
              <div>
                <Label className="text-xs">Phone Number</Label>
                <Input value={form.phoneNumber} onChange={(e) => setForm({ ...form, phoneNumber: e.target.value })} placeholder="+2348012345678" />
              </div>
            </div>
            <div className="grid grid-cols-2 gap-2">
              <div>
                <Label className="text-xs">Password</Label>
                <Input type="password" value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} placeholder="Min 8 chars" />
              </div>
              <div>
                <Label className="text-xs">Role</Label>
                <select
                  className="w-full h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 text-sm bg-white dark:bg-slate-900"
                  value={form.role}
                  onChange={(e) => setForm({ ...form, role: e.target.value })}
                >
                  {STAFF_ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
                </select>
              </div>
            </div>
            {error && <p className="text-xs text-rose-500 dark:text-rose-400">{error}</p>}
            <div className="flex gap-2 justify-end">
              <Button size="sm" variant="outline" onClick={() => { setAdding(false); setError(null); }}>Cancel</Button>
              <Button size="sm" onClick={handleAdd} disabled={saving || !form.fullName || !form.phoneNumber || !form.password}>
                {saving ? "Adding..." : "Add"}
              </Button>
            </div>
          </div>
        )}

        {staff && staff.length > 0 ? (
          <div className="space-y-2">
            {staff.map((s) => (
              <div key={s.id} className="flex items-center justify-between border rounded-lg px-3 py-2">
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2">
                    <p className="text-sm font-medium text-slate-900 dark:text-slate-50 truncate">{s.fullName}</p>
                    <Badge variant={s.role === "Owner" ? "default" : "secondary"} className="text-xs">{s.role}</Badge>
                  </div>
                  <p className="text-xs text-slate-400 dark:text-slate-500 font-mono">{s.phoneNumber}</p>
                </div>
                {s.role !== "Owner" && (
                  <div className="flex items-center gap-1">
                    <button
                      onClick={() => { setResetting(s); setResetPasswordValue(""); setResetError(null); }}
                      className="p-1 rounded hover:bg-cyan-50 dark:hover:bg-cyan-950/30 text-slate-400 dark:text-slate-500 hover:text-cyan-600 dark:hover:text-cyan-400"
                      title="Reset password"
                    >
                      <KeyRound size={14} />
                    </button>
                    <button
                      onClick={() => handleRemove(s.id)}
                      className="p-1 rounded hover:bg-rose-50 dark:bg-rose-950/30 text-slate-400 dark:text-slate-500 hover:text-rose-500 dark:text-rose-400"
                      title="Remove staff"
                    >
                      <Trash2 size={14} />
                    </button>
                  </div>
                )}
              </div>
            ))}
          </div>
        ) : (
          <p className="text-xs text-slate-400 dark:text-slate-500 italic">No team members yet. Add staff to let them record sales via WhatsApp or the dashboard.</p>
        )}
      </CardContent>

      {/* Reset password dialog — replaces the legacy prompt() */}
      <Dialog open={resetting !== null} onOpenChange={(o) => !o && setResetting(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Reset password{resetting ? ` for ${resetting.fullName}` : ""}</DialogTitle>
          </DialogHeader>
          <div className="space-y-3">
            <p className="text-sm text-slate-600 dark:text-slate-400">
              Set a temporary password. They&rsquo;ll be required to change it on next login.
            </p>
            <div>
              <Label className="text-xs text-slate-500 dark:text-slate-400">Temporary password</Label>
              <Input
                type="password"
                value={resetPassword}
                onChange={(e) => { setResetPasswordValue(e.target.value); setResetError(null); }}
                placeholder="Min 10 characters"
                autoFocus
                onKeyDown={(e) => { if (e.key === "Enter" && validatePassword(resetPassword).ok) handleResetPassword(); }}
              />
              <PasswordStrengthHint password={resetPassword} />
            </div>
            {resetError && <p className="text-xs text-rose-500 dark:text-rose-400">{resetError}</p>}
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setResetting(null)} disabled={resetSaving}>Cancel</Button>
            <Button onClick={handleResetPassword} disabled={resetSaving || !validatePassword(resetPassword).ok}>
              {resetSaving ? "Resetting…" : "Reset password"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </Card>
  );
}

type BusinessShape = {
  id: string;
  name: string;
  businessType?: string;
  currency: string;
  state?: string;
  city?: string;
  country?: string;
  address?: string;
  largeSaleThreshold?: number;
  customCategories?: string[];
  alertLowStock?: boolean;
  alertDailySummary?: boolean;
  alertLargeSale?: boolean;
  largeSaleAlertWhatsApp?: boolean;
  largeSaleAlertTelegram?: boolean;
  largeSaleAlertMessenger?: boolean;
  largeSaleAlertDashboard?: boolean;
  confirmLargeSales?: boolean;
  confirmLargeSaleThreshold?: number;
  vatEnabled?: boolean;
  vatRate?: number;
  taxId?: string;
  receiptHeaderText?: string;
  receiptFooterText?: string;
  receiptAccentColor?: string;
  plan?: string;
  trialEndsAt?: string;
  isActive: boolean;
  backgroundImageUrl?: string | null;
  backgroundImageOpacity?: number;
};

// ─── Receipts settings card (own section, inline editable) ──────────────────
/**
 * Custom dashboard background image — Pro and Business plans only.
 * - Free/Shop plans: shows a locked upgrade prompt; file picker disabled.
 * - Plans with the feature: file picker, preview of currently-saved image,
 *   opacity slider (overlay legibility), and remove button.
 *
 * Client-side validation rejects too-large files and non-image MIME types BEFORE
 * upload, but the server re-validates everything (BackgroundImageService has the
 * full security pipeline: magic-byte sniff, dimension preflight, decode-and-re-encode).
 */
// ─── Alert delivery channel card (Phase 6) ───────────────────────────────────
/**
 * One card per messaging channel inside Settings → Alerts. Shows whether this channel is the
 * user's current alert recipient (badge), and a button to switch to it. Telegram and Messenger
 * are both live as of Phase 3e — outbound alerts route through whichever channel the user picks,
 * with WhatsApp as the fallback when the chosen channel isn't bound.
 *
 * Routing logic lives in the API's NotificationDispatcher — this card just toggles the
 * User.AlertChannel preference. Same alert TYPE toggles (low stock, daily summary, etc.) are
 * set in the WhatsApp section above and apply globally — the channel toggle only changes
 * delivery, not content.
 */
function AlertChannelCard({ channel }: { channel: "telegram" | "messenger" }) {
  const { toast } = useToast();
  const [user, setUser] = useState<ReturnType<typeof getStoredUser>>(getStoredUser());
  const [saving, setSaving] = useState(false);

  // Mount-time refresh from the server so the badge reflects truth, not stale cache.
  useEffect(() => {
    let cancelled = false;
    async function refresh() {
      try {
        const { data } = await api.get<{ data: UserDto }>("/auth/me");
        if (!cancelled) setUser(data.data);
      } catch { /* keep cached value */ }
    }
    refresh();
    return () => { cancelled = true; };
  }, []);

  const isTelegram = channel === "telegram";
  const isActive = user?.alertChannel === channel;

  async function makeActive() {
    setSaving(true);
    try {
      await api.put("/auth/alert-channel", { channel });
      // Re-fetch user to pick up new AlertChannel
      const { data } = await api.get<{ data: UserDto }>("/auth/me");
      setUser(data.data);
      toast.success(`Alerts will now go to ${channel === "telegram" ? "Telegram" : "Messenger"}`,
        "Future summaries, low-stock notices, and other alerts will land there.");
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      toast.error("Couldn't change alert channel", ax.response?.data?.errors?.[0] ?? "Please try again.");
    } finally {
      setSaving(false);
    }
  }

  const accent = isTelegram
    ? "border-sky-200 dark:border-sky-900 bg-sky-50/40 dark:bg-sky-950/20 text-sky-700 dark:text-sky-300"
    : "border-blue-200 dark:border-blue-900 bg-blue-50/40 dark:bg-blue-950/20 text-blue-700 dark:text-blue-300";
  const accentText = isTelegram ? "text-sky-700 dark:text-sky-300" : "text-blue-700 dark:text-blue-300";

  return (
    <div className={`rounded-lg border p-3 ${accent}`}>
      <div className="flex items-center justify-between mb-1">
        <p className={`text-xs font-semibold uppercase tracking-wider flex items-center gap-1.5 ${accentText}`}>
          {isTelegram ? <Send size={12} /> : <MessageSquare size={12} />}
          {isTelegram ? "Telegram" : "Facebook Messenger"}
        </p>
        {isActive ? (
          <span className="text-[10px] font-medium uppercase tracking-wide px-1.5 py-px rounded-full bg-emerald-100 text-emerald-700 ring-1 ring-inset ring-emerald-200 dark:bg-emerald-950/40 dark:text-emerald-300 dark:ring-emerald-900">
            ✓ Active
          </span>
        ) : (
          <span className="text-[10px] font-medium uppercase tracking-wide px-1.5 py-px rounded-full bg-slate-100 text-slate-600 ring-1 ring-inset ring-slate-200 dark:bg-slate-800 dark:text-slate-300 dark:ring-slate-700">
            Inactive
          </span>
        )}
      </div>
      <p className="text-[11px] text-slate-500 dark:text-slate-400 mb-3">
        {isTelegram
          ? "Sent as Telegram messages to your linked chat. Make sure Telegram is connected in Settings → Connected Channels first."
          : "Sent through Facebook Messenger to your linked Page chat. Make sure Messenger is connected in Settings → Connected Channels first — otherwise alerts fall back to WhatsApp."}
      </p>
      {isActive ? (
        <Button
          variant="outline"
          size="sm"
          onClick={async () => {
            setSaving(true);
            try {
              await api.put("/auth/alert-channel", { channel: "whatsapp" });
              const { data } = await api.get<{ data: UserDto }>("/auth/me");
              setUser(data.data);
              toast.success("Switched back to WhatsApp",
                "Future alerts will land on the owner's WhatsApp number.");
            } catch (err: unknown) {
              const ax = err as { response?: { data?: { errors?: string[] } } };
              toast.error("Couldn't switch back", ax.response?.data?.errors?.[0] ?? "Please try again.");
            } finally {
              setSaving(false);
            }
          }}
          disabled={saving}
          className="flex-shrink-0"
        >
          {saving ? "Switching…" : "Switch back to WhatsApp"}
        </Button>
      ) : (
        <Button size="sm" onClick={makeActive} disabled={saving} className="flex-shrink-0">
          {saving ? "Switching…" : "Make this my alert channel"}
        </Button>
      )}
    </div>
  );
}

// ─── Connected Channels (Phase 4) ─────────────────────────────────────────────
/**
 * Shows status of each messaging channel (WhatsApp, Telegram, Messenger) and lets users
 * connect or disconnect them. WhatsApp is always "available" (no per-user binding — every
 * user gets the same shared bot phone number). Telegram supports full bind/unbind via a
 * /start deep link. Messenger is "Coming soon" until Phase 3 + Meta Advanced Access ship.
 *
 * Status comes from GET /api/channels/status; connect uses POST /api/channels/telegram/link
 * which mints a 30-minute one-time token and returns the t.me deep link to open.
 */
type ChannelBindingStatus = {
  connected: boolean;
  displayName?: string | null;
  connectedAtUtc?: string | null;
  lastSeenAtUtc?: string | null;
};

type ChannelStatusResponse = {
  whatsapp: ChannelBindingStatus;
  telegram: ChannelBindingStatus;
  messenger: ChannelBindingStatus;
};

function ConnectedChannelsCard() {
  const { toast } = useToast();
  const [status, setStatus] = useState<ChannelStatusResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [linking, setLinking] = useState(false);
  const [disconnecting, setDisconnecting] = useState(false);

  async function fetchStatus() {
    setLoading(true);
    try {
      const { data } = await api.get<{ data: ChannelStatusResponse }>("/channels/status");
      setStatus(data.data);
    } catch {
      // Fall through; UI shows "—" for everything. Don't toast on read failure — could be
      // an old build that doesn't have the endpoint yet.
      setStatus(null);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    fetchStatus();
  }, []);

  async function handleConnectTelegram() {
    setLinking(true);
    try {
      const { data } = await api.post<{ data: { deepLink: string } }>("/channels/telegram/link");
      const link = data.data?.deepLink;
      if (!link) {
        toast.error("Couldn't create link", "Try again in a moment.");
        return;
      }
      // Open the Telegram deep link in a new tab. On desktop the user gets a "Open in Telegram"
      // prompt; on mobile it launches the Telegram app directly. Either way they tap Start and
      // the bot binds their chat.
      //
      // Mobile browsers silently block `window.open(...)` calls made after `await` because the
      // user-gesture context is lost — popup looks like programmatic navigation. Fall back to
      // a current-window navigation when the popup is blocked. Desktop keeps the new-tab UX;
      // mobile reliably reaches t.me which the OS routes into the Telegram app.
      const opened = window.open(link, "_blank", "noopener,noreferrer");
      if (!opened) window.location.href = link;
      toast.success("Telegram link opened",
        "Tap Start in Telegram to connect. Refresh this page after to confirm.");
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      toast.error("Couldn't create link", ax.response?.data?.errors?.[0] ?? "Please try again.");
    } finally {
      setLinking(false);
    }
  }

  async function handleDisconnectTelegram() {
    if (!confirm("Disconnect Telegram from your account?")) return;
    setDisconnecting(true);
    try {
      await api.delete("/channels/telegram");
      toast.success("Telegram disconnected", "The chat will stop responding to your account.");
      await fetchStatus();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      toast.error("Couldn't disconnect", ax.response?.data?.errors?.[0] ?? "Please try again.");
    } finally {
      setDisconnecting(false);
    }
  }

  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300">
          Where Ojunai can reach you
        </CardTitle>
        <p className="text-xs text-slate-500 dark:text-slate-400 mt-1">
          Connect a messaging account so you can record sales, expenses, and payments by chatting
          with the assistant — on whichever platform you already use.
        </p>
      </CardHeader>
      <CardContent className="space-y-3">
        {/* WhatsApp — always available, no per-user binding. */}
        <div className="flex items-center justify-between gap-3 rounded-lg border border-slate-200 dark:border-slate-800 bg-slate-50/40 dark:bg-slate-900/40 p-3">
          <div className="flex items-center gap-3 min-w-0">
            <div className="w-9 h-9 rounded-lg bg-green-50 dark:bg-green-950/40 flex items-center justify-center flex-shrink-0">
              <MessageSquare size={16} className="text-green-600 dark:text-green-400" />
            </div>
            <div className="min-w-0">
              <p className="text-sm font-medium text-slate-900 dark:text-slate-50">WhatsApp</p>
              <p className="text-xs text-slate-500 dark:text-slate-400 truncate">
                Send messages to the shared Ojunai number to record sales and expenses.
              </p>
            </div>
          </div>
          <Badge variant="default" className="flex-shrink-0">Always on</Badge>
        </div>

        {/* Telegram — full bind/unbind via /start deep link */}
        <div className="flex items-center justify-between gap-3 rounded-lg border border-slate-200 dark:border-slate-800 bg-slate-50/40 dark:bg-slate-900/40 p-3">
          <div className="flex items-center gap-3 min-w-0">
            <div className="w-9 h-9 rounded-lg bg-sky-50 dark:bg-sky-950/40 flex items-center justify-center flex-shrink-0">
              <Send size={16} className="text-sky-600 dark:text-sky-400" />
            </div>
            <div className="min-w-0">
              <p className="text-sm font-medium text-slate-900 dark:text-slate-50">Telegram</p>
              {loading ? (
                <p className="text-xs text-slate-400 dark:text-slate-500">Checking status…</p>
              ) : status?.telegram?.connected ? (
                <p className="text-xs text-slate-500 dark:text-slate-400 truncate">
                  Connected{status.telegram.displayName ? ` as ${status.telegram.displayName}` : ""}
                  {status.telegram.connectedAtUtc
                    ? ` · ${new Date(status.telegram.connectedAtUtc).toLocaleDateString()}`
                    : ""}
                </p>
              ) : (
                <p className="text-xs text-slate-500 dark:text-slate-400">
                  Not connected. Link your chat to get the bot in Telegram.
                </p>
              )}
            </div>
          </div>
          {loading ? null : status?.telegram?.connected ? (
            <Button
              variant="outline"
              size="sm"
              onClick={handleDisconnectTelegram}
              disabled={disconnecting}
              className="flex-shrink-0"
            >
              {disconnecting ? "Disconnecting…" : "Disconnect"}
            </Button>
          ) : (
            <Button
              size="sm"
              onClick={handleConnectTelegram}
              disabled={linking}
              className="flex-shrink-0"
            >
              <ExternalLink size={14} className="mr-1.5" />
              {linking ? "Generating link…" : "Connect"}
            </Button>
          )}
        </div>

        {/* Messenger — linking active in Phase 3b; full NL handling lands in Phase 3c */}
        <div className="flex items-center justify-between gap-3 rounded-lg border border-slate-200 dark:border-slate-800 bg-slate-50/40 dark:bg-slate-900/40 p-3">
          <div className="flex items-center gap-3 min-w-0">
            <div className="w-9 h-9 rounded-lg bg-blue-50 dark:bg-blue-950/40 flex items-center justify-center flex-shrink-0">
              <MessageSquare size={16} className="text-blue-600 dark:text-blue-400" />
            </div>
            <div className="min-w-0">
              <p className="text-sm font-medium text-slate-900 dark:text-slate-50">Facebook Messenger</p>
              {loading ? (
                <p className="text-xs text-slate-400 dark:text-slate-500">Checking status…</p>
              ) : status?.messenger?.connected ? (
                <p className="text-xs text-slate-500 dark:text-slate-400 truncate">
                  Connected
                  {status.messenger.connectedAtUtc
                    ? ` · ${new Date(status.messenger.connectedAtUtc).toLocaleDateString()}`
                    : ""}
                </p>
              ) : (
                <p className="text-xs text-slate-500 dark:text-slate-400">
                  Not connected. Link Messenger to log sales, expenses, and payments by chatting with your Page.
                </p>
              )}
            </div>
          </div>
          {loading ? null : status?.messenger?.connected ? (
            <Button
              variant="outline"
              size="sm"
              onClick={async () => {
                if (!confirm("Disconnect Messenger from your account?")) return;
                try {
                  await api.delete("/channels/messenger");
                  toast.success("Messenger disconnected", "The Page will stop responding to your account.");
                  await fetchStatus();
                } catch (err: unknown) {
                  const ax = err as { response?: { data?: { errors?: string[] } } };
                  toast.error("Couldn't disconnect", ax.response?.data?.errors?.[0] ?? "Please try again.");
                }
              }}
              className="flex-shrink-0"
            >
              Disconnect
            </Button>
          ) : (
            <Button
              size="sm"
              onClick={async () => {
                try {
                  const { data } = await api.post<{ data: { deepLink: string } }>("/channels/messenger/link");
                  const link = data.data?.deepLink;
                  if (!link) {
                    toast.error("Couldn't create link", "Try again in a moment.");
                    return;
                  }
                  // Opens Messenger via m.me on mobile, or web Messenger on desktop. After the
                  // user sends a message or taps Get Started, Meta fires the referral webhook
                  // with our token; the orchestrator consumes it and binds the PSID.
                  //
                  // Mobile browsers silently block `window.open(...)` after an `await` (lost
                  // user-gesture context). Fall back to current-window navigation when the
                  // popup is blocked — m.me is a deep link the OS routes into the Messenger
                  // app with `?ref=` preserved.
                  const opened = window.open(link, "_blank", "noopener,noreferrer");
                  if (!opened) window.location.href = link;
                  toast.success("Messenger link opened",
                    "Send a message or tap Get Started to connect. Refresh this page after.");
                } catch (err: unknown) {
                  const ax = err as { response?: { data?: { errors?: string[] } } };
                  toast.error("Couldn't create link", ax.response?.data?.errors?.[0] ?? "Please try again.");
                }
              }}
              className="flex-shrink-0"
            >
              <ExternalLink size={14} className="mr-1.5" />
              Connect
            </Button>
          )}
        </div>

        <p className="text-[11px] text-slate-400 dark:text-slate-500 mt-2">
          Telegram links expire after 30 minutes if not used. You can disconnect a channel at any
          time to stop the bot from responding to that account.
        </p>
      </CardContent>
    </Card>
  );
}

function BrandingCard({
  business,
  onUpdate,
}: {
  business: BusinessShape | null;
  onUpdate: (b: BusinessShape) => void;
}) {
  const { toast } = useToast();
  const { data: planStatus } = usePlanStatus();
  const hasFeature = planStatus?.hasCustomBranding ?? false;
  const [uploading, setUploading] = useState(false);
  const [removing, setRemoving] = useState(false);
  const fileInputRef = useRef<HTMLInputElement | null>(null);

  const currentUrl = absoluteApiUrl(business?.backgroundImageUrl ?? null);
  const opacity = business?.backgroundImageOpacity ?? 0.85;

  async function handleFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    e.target.value = ""; // allow re-selecting the same file later
    if (!file) return;

    // Client-side validation. Server re-validates with magic-byte sniff + decode.
    const allowed = ["image/jpeg", "image/jpg", "image/png", "image/webp"];
    if (!allowed.includes(file.type)) {
      toast.error("Unsupported file type", "Use JPEG, PNG, or WebP.");
      return;
    }
    if (file.size > 5 * 1024 * 1024) {
      toast.error("File too large", "Maximum size is 5MB.");
      return;
    }

    const formData = new FormData();
    formData.append("file", file);
    setUploading(true);
    try {
      const { data } = await api.post<{ data: BusinessShape }>("/business/background-image", formData, {
        headers: { "Content-Type": "multipart/form-data" },
      });
      onUpdate(data.data!);
      toast.success("Background updated", "Your dashboard now uses your custom image.");
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      toast.error("Couldn't upload image", ax.response?.data?.errors?.[0] ?? "Please try a different file.");
    } finally {
      setUploading(false);
    }
  }

  async function handleRemove() {
    if (!confirm("Remove the custom background?")) return;
    setRemoving(true);
    try {
      const { data } = await api.delete<{ data: BusinessShape }>("/business/background-image");
      onUpdate(data.data!);
      toast.success("Background removed", "Reverted to the default dashboard.");
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      toast.error("Couldn't remove image", ax.response?.data?.errors?.[0] ?? "Please try again.");
    } finally {
      setRemoving(false);
    }
  }

  async function handleOpacityChange(val: number) {
    try {
      const { data } = await api.put<{ data: BusinessShape }>("/business", { backgroundImageOpacity: val });
      onUpdate(data.data!);
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      toast.error("Couldn't save change", ax.response?.data?.errors?.[0] ?? "Please try again.");
    }
  }

  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300 flex items-center gap-2">
          <ImageIcon size={15} className="text-pink-500" />
          Branding
        </CardTitle>
        <p className="text-xs text-slate-500 dark:text-slate-400 mt-1">Custom dashboard background image</p>
      </CardHeader>
      <CardContent className="space-y-3">
        {!hasFeature ? (
          <div className="flex items-start gap-3 rounded-lg border border-amber-200 dark:border-amber-900 bg-amber-50/40 dark:bg-amber-950/20 p-3">
            <Lock size={16} className="text-amber-500 mt-0.5 flex-shrink-0" />
            <div className="text-sm">
              <p className="font-medium text-slate-700 dark:text-slate-300">Upgrade to use custom branding</p>
              <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
                Custom dashboard backgrounds are part of the Pro and Business plans. Visit Plan &amp; Billing to upgrade.
              </p>
            </div>
          </div>
        ) : (
          <>
            {/* Live preview */}
            <div className="rounded-lg border border-slate-200 dark:border-slate-800 overflow-hidden">
              <div
                className="h-32 bg-cover bg-center relative"
                style={{
                  backgroundImage: currentUrl ? `url(${currentUrl})` : undefined,
                  backgroundColor: !currentUrl ? "#F1F5F9" : undefined,
                }}
              >
                {currentUrl && (
                  <div
                    className="absolute inset-0 bg-white dark:bg-slate-950 transition-opacity"
                    style={{ opacity }}
                  />
                )}
                <div className="absolute inset-0 flex items-center justify-center">
                  <p className="text-xs text-slate-400 dark:text-slate-500 px-3 py-1 rounded bg-white/80 dark:bg-slate-900/80">
                    {currentUrl ? "Preview" : "No image — upload to set a custom background"}
                  </p>
                </div>
              </div>
            </div>

            <input
              ref={fileInputRef}
              type="file"
              accept="image/jpeg,image/png,image/webp"
              className="hidden"
              onChange={handleFileChange}
            />

            <div className="flex items-center gap-2">
              <Button
                variant="outline"
                size="sm"
                onClick={() => fileInputRef.current?.click()}
                disabled={uploading || removing}
              >
                <Upload size={14} className="mr-1.5" />
                {uploading ? "Uploading…" : currentUrl ? "Replace image" : "Upload image"}
              </Button>
              {currentUrl && (
                <Button
                  variant="outline"
                  size="sm"
                  onClick={handleRemove}
                  disabled={uploading || removing}
                >
                  <Trash2 size={14} className="mr-1.5" />
                  {removing ? "Removing…" : "Remove"}
                </Button>
              )}
            </div>

            {currentUrl && (
              <div>
                <Label className="text-xs">Overlay opacity ({Math.round(opacity * 100)}%)</Label>
                <input
                  type="range"
                  min={0}
                  max={1}
                  step={0.05}
                  value={opacity}
                  onChange={(e) => handleOpacityChange(Number(e.target.value))}
                  className="w-full"
                />
                <p className="text-[11px] text-slate-400 dark:text-slate-500">
                  Higher = content stays legible. Lower = image more visible. Default is 85%.
                </p>
              </div>
            )}

            <p className="text-[11px] text-slate-400 dark:text-slate-500">
              JPEG, PNG, or WebP — up to 5MB. Resized server-side to 1920×1080 and stripped of metadata.
            </p>
          </>
        )}
      </CardContent>
    </Card>
  );
}

function ReceiptsCard({
  business,
  onUpdate,
}: {
  business: BusinessShape | null;
  onUpdate: (b: BusinessShape) => void;
}) {
  const [form, setForm] = useState({
    vatEnabled: false,
    vatRate: 7.5,
    taxId: "",
    receiptHeaderText: "",
    receiptFooterText: "",
    receiptAccentColor: "#06b6d4",
  });
  const [saving, setSaving] = useState(false);
  const [savedAt, setSavedAt] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [initialized, setInitialized] = useState(false);

  // Track dirty state for the Save button enable
  const initialRef = useState({ ref: form })[0];

  if (business && !initialized) {
    const f = {
      vatEnabled: business.vatEnabled ?? false,
      vatRate: business.vatRate ?? 7.5,
      taxId: business.taxId ?? "",
      receiptHeaderText: business.receiptHeaderText ?? "",
      receiptFooterText: business.receiptFooterText ?? "",
      receiptAccentColor: business.receiptAccentColor ?? "#06b6d4",
    };
    setForm(f);
    initialRef.ref = f;
    setInitialized(true);
  }

  const isDirty = initialized && JSON.stringify(form) !== JSON.stringify(initialRef.ref);

  async function handleSave() {
    if (!business || !isDirty) return;
    setSaving(true);
    setError(null);
    try {
      const { data } = await api.put<{ data: BusinessShape }>("/business", {
        vatEnabled: form.vatEnabled,
        vatRate: form.vatRate,
        taxId: form.taxId || null,
        receiptHeaderText: form.receiptHeaderText || null,
        receiptFooterText: form.receiptFooterText || null,
        receiptAccentColor: form.receiptAccentColor || null,
      });
      onUpdate(data.data!);
      initialRef.ref = { ...form };
      setSavedAt(Date.now());
      setTimeout(() => setSavedAt(null), 2500);
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to save");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300 flex items-center gap-2">
          <FileText size={15} className="text-cyan-500" />
          Receipts
        </CardTitle>
        <p className="text-xs text-slate-500 dark:text-slate-400 mt-1">
          Configure VAT, tax info, and receipt PDF appearance
        </p>
      </CardHeader>
      <CardContent className="space-y-5">
        {/* VAT */}
        <div>
          <label className="flex items-center justify-between cursor-pointer">
            <div>
              <p className="text-sm font-medium text-slate-700 dark:text-slate-300">Charge VAT on sales</p>
              <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">When on, new sales default to VAT-included; toggleable per sale</p>
            </div>
            <input
              type="checkbox"
              checked={form.vatEnabled}
              onChange={(e) => setForm({ ...form, vatEnabled: e.target.checked })}
              className="rounded border-slate-300 dark:border-slate-700"
            />
          </label>
          {form.vatEnabled && (
            <div className="mt-3 ml-0 max-w-[180px]">
              <Label className="text-xs text-slate-500 dark:text-slate-400">VAT Rate (%)</Label>
              <Input
                type="number"
                step="0.1"
                min={0}
                max={100}
                value={form.vatRate}
                onChange={(e) => setForm({ ...form, vatRate: Number(e.target.value) })}
              />
              <p className="text-[11px] text-slate-400 dark:text-slate-500 mt-1">Nigeria standard: 7.5%</p>
            </div>
          )}
        </div>

        <Separator />

        {/* Tax ID */}
        <div>
          <Label className="text-xs text-slate-500 dark:text-slate-400">Tax ID / TIN</Label>
          <Input
            value={form.taxId}
            onChange={(e) => setForm({ ...form, taxId: e.target.value })}
            placeholder="e.g. 12345678-0001"
            className="max-w-md"
          />
          <p className="text-[11px] text-slate-400 dark:text-slate-500 mt-1">Printed on receipts under business address. Optional.</p>
        </div>

        {/* Header text override */}
        <div>
          <Label className="text-xs text-slate-500 dark:text-slate-400">Header text override</Label>
          <Input
            value={form.receiptHeaderText}
            onChange={(e) => setForm({ ...form, receiptHeaderText: e.target.value })}
            placeholder={business?.name ?? "Business name"}
            maxLength={80}
            className="max-w-md"
          />
          <p className="text-[11px] text-slate-400 dark:text-slate-500 mt-1">Defaults to your business name. Use only if you want a different name on receipts.</p>
        </div>

        {/* Footer */}
        <div>
          <Label className="text-xs text-slate-500 dark:text-slate-400">Footer message</Label>
          <Input
            value={form.receiptFooterText}
            onChange={(e) => setForm({ ...form, receiptFooterText: e.target.value })}
            placeholder="Thank you for your business"
            maxLength={200}
            className="max-w-md"
          />
        </div>

        {/* Accent color */}
        <div>
          <Label className="text-xs text-slate-500 dark:text-slate-400">Accent color</Label>
          <div className="flex items-center gap-2.5 mt-1">
            <input
              type="color"
              value={form.receiptAccentColor}
              onChange={(e) => setForm({ ...form, receiptAccentColor: e.target.value })}
              className="h-9 w-12 rounded-md border border-slate-200 dark:border-slate-800 cursor-pointer"
            />
            <Input
              value={form.receiptAccentColor}
              onChange={(e) => setForm({ ...form, receiptAccentColor: e.target.value })}
              placeholder="#06b6d4"
              maxLength={7}
              className="font-mono text-xs max-w-[140px]"
            />
            <div
              className="h-9 flex-1 max-w-[180px] rounded-md flex items-center justify-center text-xs font-semibold tracking-wider text-white"
              style={{ backgroundColor: form.receiptAccentColor }}
            >
              PREVIEW
            </div>
          </div>
          <p className="text-[11px] text-slate-400 dark:text-slate-500 mt-1">Used for the header divider, RECEIPT label, item table border, and TOTAL row on the printed receipt.</p>
        </div>

        {/* Save bar */}
        <div className="flex items-center justify-end gap-3 pt-3 border-t border-slate-100 dark:border-slate-800">
          {error && <p className="text-xs text-rose-500 dark:text-rose-400 mr-auto">{error}</p>}
          {savedAt && (
            <p className="text-xs text-emerald-600 inline-flex items-center gap-1 mr-auto">
              <CheckCircle2 size={12} /> Saved
            </p>
          )}
          <Button
            onClick={handleSave}
            disabled={saving || !isDirty}
            className="gap-1.5"
          >
            <Save size={14} />
            {saving ? "Saving…" : "Save changes"}
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

function ManageCategoriesCard({
  business,
  onUpdate,
}: {
  business: BusinessShape | null;
  onUpdate: (b: BusinessShape) => void;
}) {
  const { toast } = useToast();
  const [newCat, setNewCat] = useState("");
  const [saving, setSaving] = useState(false);

  const customCats = business?.customCategories ?? [];

  async function addCategory() {
    if (!newCat.trim() || !business) return;
    const trimmed = newCat.trim();
    if (CATEGORY_NAMES.includes(trimmed) || customCats.includes(trimmed)) {
      setNewCat("");
      return;
    }
    setSaving(true);
    try {
      const updated = [...customCats, trimmed];
      const { data } = await api.put<{ data: BusinessShape }>("/business", { customCategories: updated });
      onUpdate(data.data!);
      setNewCat("");
    } catch (err: unknown) {
                    const ax = err as { response?: { data?: { errors?: string[] } } };
                    toast.error("Couldn't save change", ax.response?.data?.errors?.[0] ?? "Please try again.");
                  } finally { setSaving(false); }
  }

  async function removeCategory(cat: string) {
    if (!business) return;
    setSaving(true);
    try {
      const updated = customCats.filter((c) => c !== cat);
      const { data } = await api.put<{ data: BusinessShape }>("/business", { customCategories: updated });
      onUpdate(data.data!);
    } catch (err: unknown) {
                    const ax = err as { response?: { data?: { errors?: string[] } } };
                    toast.error("Couldn't save change", ax.response?.data?.errors?.[0] ?? "Please try again.");
                  } finally { setSaving(false); }
  }

  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300 flex items-center gap-2">
          <Tags size={15} className="text-rose-500" />
          Product Categories
        </CardTitle>
        <p className="text-xs text-slate-500 dark:text-slate-400 mt-1">Custom categories on top of presets — for inventory organization</p>
      </CardHeader>
      <CardContent className="space-y-3">
        <p className="text-xs text-slate-400 dark:text-slate-500">
          12 preset categories are always available. Add your own custom categories below.
        </p>

        {/* Preset categories (read-only) */}
        <div>
          <Label className="text-xs text-slate-500 dark:text-slate-400">Preset categories</Label>
          <div className="flex flex-wrap gap-1.5 mt-1">
            {CATEGORY_NAMES.map((c) => (
              <span key={c} className="inline-flex items-center px-2 py-0.5 rounded-full text-xs bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400">
                {c}
              </span>
            ))}
          </div>
        </div>

        <Separator />

        {/* Custom categories (editable) */}
        <div>
          <Label className="text-xs text-slate-500 dark:text-slate-400">Your custom categories</Label>
          {customCats.length === 0 ? (
            <p className="text-xs text-slate-300 dark:text-slate-600 mt-1 italic">No custom categories yet</p>
          ) : (
            <div className="flex flex-wrap gap-1.5 mt-1">
              {customCats.map((c) => (
                <span key={c} className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs bg-cyan-50 text-cyan-700 border border-cyan-200">
                  {c}
                  <button
                    onClick={() => removeCategory(c)}
                    className="hover:text-rose-500 dark:text-rose-400"
                    disabled={saving}
                  >
                    <X size={12} />
                  </button>
                </span>
              ))}
            </div>
          )}
        </div>

        {/* Add new */}
        <div className="flex gap-2">
          <Input
            value={newCat}
            onChange={(e) => setNewCat(e.target.value)}
            placeholder="New category name"
            className="flex-1"
            onKeyDown={(e) => e.key === "Enter" && addCategory()}
          />
          <Button size="sm" onClick={addCategory} disabled={saving || !newCat.trim()}>
            <Plus size={14} className="mr-1" /> Add
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

function loadFlutterwaveScript(): Promise<void> {
  return new Promise((resolve, reject) => {
    if (document.querySelector('script[src*="checkout.flutterwave.com"]')) {
      resolve();
      return;
    }
    const script = document.createElement("script");
    script.src = "https://checkout.flutterwave.com/v3.js";
    script.onload = () => resolve();
    script.onerror = () => reject(new Error("Failed to load Flutterwave SDK"));
    document.head.appendChild(script);
  });
}

type PlanFeature = { text: string; included: boolean };

const PLAN_ORDER = ["starter", "shop", "pro", "business"];

const PLAN_DETAILS: Record<string, { label: string; tagline: string; color: string; features: PlanFeature[] }> = {
  starter: {
    label: "Starter",
    tagline: "Best for solo traders just starting out",
    color: "bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-300",
    features: [
      { text: "1-month free trial", included: true },
      { text: "WhatsApp bot access", included: true },
      { text: "Up to 30 products", included: true },
      { text: "150 messages / month", included: true },
      { text: "Daily summaries", included: true },
      { text: "Basic web dashboard", included: true },
      { text: "Staff accounts", included: false },
      { text: "Advanced reports", included: false },
    ],
  },
  shop: {
    label: "Shop",
    tagline: "For growing shops with staff",
    color: "bg-cyan-100 text-cyan-700",
    features: [
      { text: "Everything in Starter", included: true },
      { text: "Unlimited products", included: true },
      { text: "850 messages / month", included: true },
      { text: "Ledger & stock holds", included: true },
      { text: "Up to 4 users", included: true },
      { text: "CSV import", included: false },
      { text: "Advanced reports & charts", included: false },
    ],
  },
  pro: {
    label: "Pro",
    tagline: "Full power for serious businesses",
    color: "bg-violet-100 text-violet-700",
    features: [
      { text: "Everything in Shop", included: true },
      { text: "Unlimited messages", included: true },
      { text: "CSV import", included: true },
      { text: "Advanced reports & charts", included: true },
      { text: "Up to 11 users", included: true },
    ],
  },
  business: {
    label: "Business",
    tagline: "Enterprise-grade for multi-location businesses",
    color: "bg-amber-100 text-amber-700",
    features: [
      { text: "Everything in Pro", included: true },
      { text: "Unlimited staff", included: true },
      { text: "Multi-branch support", included: true },
      { text: "API access & custom exports", included: true },
    ],
  },
};

type PlanStatus = {
  plan: string;
  subscribedPlan: string | null;
  isSubscriber: boolean;
  trialStatus: string;
  trialDaysLeft: number | null;
  trialEndsAt: string | null;
  pricePerMonth: number;
  maxProducts: number;
  maxMessages: number;
  maxStaff: number;
  isBillable: boolean;
  hasActiveSubscription: boolean;
  subscriptionEndsAt: string | null;
  isAutoRenew: boolean;
  paymentMethod: string | null;
  subscriptionStatus: string;
  pendingPlanChange: string | null;
};

function PlanCard({ business }: { business: BusinessShape | null }) {
  const qc = useQueryClient();
  const { toast } = useToast();
  const { data: planStatus } = useQuery({
    queryKey: ["plan-status"],
    queryFn: async () => {
      const { data } = await api.get<{ data: PlanStatus }>("/business/plan-status");
      return data.data!;
    },
  });
  const [subscribing, setSubscribing] = useState<string | null>(null);
  const [cancelling, setCancelling] = useState(false);
  const [subError, setSubError] = useState<string | null>(null);
  const [pendingPayment, setPendingPayment] = useState(false);
  const [payMethodPick, setPayMethodPick] = useState<{ plan: string; result: Record<string, unknown> } | null>(null);
  const [failedVerify, setFailedVerify] = useState<{transactionId?: string; txRef?: string} | null>(null);
  const [billingCycle, setBillingCycle] = useState<"monthly" | "annual">("monthly");
  const [selectedCurrency, setSelectedCurrency] = useState<SupportedCurrency>(getDefaultCurrency());
  // Confirm-price modal state — opened before any plan-change checkout so the user
  // sees exactly what we're about to charge (especially the mid-cycle delta).
  const [confirmTarget, setConfirmTarget] = useState<string | null>(null);

  useEffect(() => {
    if (business?.currency) setSelectedCurrency(toBillingCurrency(business.currency));
  }, [business?.currency]);

  // Poll plan-status every 10s while a mobile money payment is pending
  useEffect(() => {
    if (!pendingPayment) return;
    const interval = setInterval(() => {
      qc.invalidateQueries({ queryKey: ["plan-status"] });
    }, 10000);
    return () => clearInterval(interval);
  }, [pendingPayment, qc]);

  // Auto-clear pending state when subscription activates
  useEffect(() => {
    if (pendingPayment && planStatus?.subscriptionStatus === "active") {
      setPendingPayment(false);
      setSubError(null);
    }
  }, [pendingPayment, planStatus?.subscriptionStatus]);

  const plan = planStatus?.plan ?? business?.plan ?? "starter";
  const details = PLAN_DETAILS[plan] ?? PLAN_DETAILS.starter;
  const trialStatus = planStatus?.trialStatus;
  const daysLeft = planStatus?.trialDaysLeft;
  const isSubscriber = planStatus?.isSubscriber ?? false;
  const isBillable = planStatus?.isBillable ?? true;
  const hasActiveSub = planStatus?.hasActiveSubscription ?? false;
  const isAutoRenew = planStatus?.isAutoRenew ?? true;
  const paymentMethod = planStatus?.paymentMethod;
  const subEndsAt = planStatus?.subscriptionEndsAt ? new Date(planStatus.subscriptionEndsAt) : null;
  const daysUntilExpiry = subEndsAt ? Math.max(0, Math.ceil((subEndsAt.getTime() - Date.now()) / 86400000)) : null;
  const isExpiringSoon = daysUntilExpiry != null && daysUntilExpiry <= 7 && hasActiveSub;
  const monthlyPrice = getPrice(plan, "monthly", selectedCurrency);
  const displayPrice = billingCycle === "annual"
    ? formatPrice(getMonthlyEquivalent(plan, selectedCurrency), selectedCurrency)
    : formatPrice(monthlyPrice, selectedCurrency);
  const annualTotal = formatPrice(getPrice(plan, "annual", selectedCurrency), selectedCurrency);
  const discount = PRICING[plan]?.annualDiscount;

  function btnPrice(planKey: string) {
    if (billingCycle === "annual") {
      return `${formatPrice(getPrice(planKey, "annual", selectedCurrency), selectedCurrency)}/yr`;
    }
    return `${formatPrice(getPrice(planKey, "monthly", selectedCurrency), selectedCurrency)}/mo`;
  }

  async function handleSubscribe(targetPlan: string) {
    setSubscribing(targetPlan);
    setSubError(null);
    try {
      const { data } = await api.post<{ data: Record<string, unknown> }>("/subscription/initialize", {
        plan: targetPlan,
        currency: selectedCurrency,
        billingCycle,
      });
      const result = data.data!;

      if (result.provider === "paystack") {
        window.location.href = result.paymentUrl as string;
        return;
      }

      if (!result.publicKey) {
        setSubError("Payment gateway not configured. Please contact support.");
        setSubscribing(null);
        return;
      }

      // Show payment method picker for Flutterwave
      setPayMethodPick({ plan: targetPlan, result });
      setSubscribing(null);
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setSubError(ax.response?.data?.errors?.[0] ?? "Failed to start subscription");
      setSubscribing(null);
    }
  }

  async function openFlutterwaveCheckout(useCard: boolean) {
    if (!payMethodPick) return;
    const { plan: targetPlan, result } = payMethodPick;
    setPayMethodPick(null);
    setSubscribing(targetPlan);

    try { await loadFlutterwaveScript(); } catch {
      setSubError("Failed to load payment widget. Please refresh and try again.");
      setSubscribing(null);
      return;
    }

    const win = window as unknown as { FlutterwaveCheckout?: (config: Record<string, unknown>) => void };
    if (!win.FlutterwaveCheckout) {
      setSubError("Failed to load payment widget. Please refresh and try again.");
      setSubscribing(null);
      return;
    }

    const timeout = setTimeout(() => {
      setSubError("Payment timed out. Please try again.");
      setSubscribing(null);
    }, useCard ? 30000 : 120000);

    const config: Record<string, unknown> = {
      public_key: result.publicKey,
      tx_ref: result.txRef,
      amount: result.amount,
      currency: result.currency,
      redirect_url: result.callbackUrl,
      customer: { email: result.email },
      meta: {
        businessId: result.businessId,
        plan: result.plan,
        billingCycle: result.billingCycle,
        currency: result.currency,
      },
      customizations: {
        title: "Ojunai",
        description: `${(result.plan as string).charAt(0).toUpperCase() + (result.plan as string).slice(1)} Plan — ${result.billingCycle}`,
        logo: "https://app.ojunai.com/favicon.ico",
      },
      callback: async (response: { transaction_id?: string; tx_ref?: string }) => {
        clearTimeout(timeout);
        try {
          await api.post("/subscription/verify-flutterwave", {
            transactionId: response.transaction_id?.toString(),
            txRef: response.tx_ref,
          });
          qc.invalidateQueries({ queryKey: ["plan-status"] });
        } catch (err: unknown) {
          const ax = err as { response?: { data?: { errors?: string[] } } };
          const msg = ax.response?.data?.errors?.[0] ?? "";
          if (msg.startsWith("PENDING:")) {
            setPendingPayment(true);
          } else {
            setFailedVerify({ transactionId: response.transaction_id?.toString(), txRef: response.tx_ref });
            setSubError("Payment received but verification failed.");
          }
        } finally {
          setSubscribing(null);
        }
      },
      onclose: () => { clearTimeout(timeout); setSubscribing(null); },
    };

    if (useCard && result.paymentPlanId) {
      config.payment_plan = result.paymentPlanId;
      config.payment_options = "card";
    } else if (useCard && !result.paymentPlanId) {
      // Card selected but auto-renew plan creation failed — proceed without auto-renew
      config.payment_options = "card";
    } else {
      config.payment_options = "mobilemoney,banktransfer,ussd";
    }

    win.FlutterwaveCheckout(config);
  }

  async function handleCancel() {
    if (!confirm("Cancel auto-renewal? You'll keep access until the end of your billing period.")) return;
    setCancelling(true);
    setSubError(null);
    try {
      await api.post("/subscription/cancel");
      qc.invalidateQueries({ queryKey: ["plan-status"] });
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setSubError(ax.response?.data?.errors?.[0] ?? "Failed to cancel");
    } finally {
      setCancelling(false);
    }
  }

  async function handleDowngrade(targetPlan: string) {
    const targetLabel = targetPlan.charAt(0).toUpperCase() + targetPlan.slice(1);
    if (!confirm(`Downgrade to ${targetLabel}? You'll keep your current features until the end of your billing period.`)) return;
    setSubscribing(targetPlan);
    setSubError(null);
    try {
      await api.post("/subscription/change-plan", { plan: targetPlan });
      qc.invalidateQueries({ queryKey: ["plan-status"] });
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setSubError(ax.response?.data?.errors?.[0] ?? "Failed to change plan");
    } finally {
      setSubscribing(null);
    }
  }

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300 flex items-center gap-2">
          <CreditCard size={15} />
          Your Plan
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="flex items-center gap-3">
          <span className={`inline-flex items-center px-3 py-1 rounded-full text-sm font-semibold ${details.color}`}>
            {details.label}
          </span>
          {hasActiveSub && (
            <span className="text-xs text-green-600 font-medium">Active subscription</span>
          )}
          {!hasActiveSub && isSubscriber && trialStatus === "None" && (
            <span className="text-xs text-green-600 font-medium">Paid</span>
          )}
          {trialStatus === "Active" && daysLeft != null && (
            <span className="text-xs text-amber-600 font-medium">
              Free trial — {daysLeft} day{daysLeft !== 1 ? "s" : ""} left
            </span>
          )}
          {trialStatus === "GracePeriod" && (
            <span className="text-xs text-rose-600 dark:text-rose-400 font-medium">
              Trial ended — grace period active
            </span>
          )}
          {trialStatus === "Expired" && (
            <span className="text-xs text-rose-600 dark:text-rose-400 font-medium">
              Trial expired — subscribe to keep access
            </span>
          )}
        </div>

        {/* Billing cycle toggle */}
        <div className="flex items-center bg-slate-100 dark:bg-slate-800 rounded-lg p-1">
          <button
            onClick={() => setBillingCycle("monthly")}
            className={`flex-1 px-3 py-1.5 rounded-md text-sm font-medium transition-colors ${
              billingCycle === "monthly" ? "bg-white dark:bg-slate-900 shadow-sm text-slate-900 dark:text-slate-50" : "text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-300"
            }`}
          >
            Monthly
          </button>
          <button
            onClick={() => setBillingCycle("annual")}
            className={`flex-1 px-3 py-1.5 rounded-md text-sm font-medium transition-colors ${
              billingCycle === "annual" ? "bg-white dark:bg-slate-900 shadow-sm text-slate-900 dark:text-slate-50" : "text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-300"
            }`}
          >
            Annual
            {discount != null && <span className="ml-1 text-xs text-green-600 font-semibold">-{discount}%</span>}
          </button>
        </div>

        {/* Currency selector */}
        <div className="flex gap-1.5 overflow-x-auto pb-1">
          {SUPPORTED_CURRENCIES.map((c) => (
            <button
              key={c}
              onClick={() => setSelectedCurrency(c)}
              className={`px-2.5 py-1 rounded-md text-xs font-medium whitespace-nowrap transition-colors ${
                selectedCurrency === c
                  ? "bg-cyan-100 text-cyan-700 border border-cyan-200"
                  : "bg-slate-50 dark:bg-slate-950 text-slate-500 dark:text-slate-400 border border-slate-200 dark:border-slate-800 hover:bg-slate-100 dark:hover:bg-slate-800"
              }`}
            >
              {CURRENCY_META[c].symbol} {c}
            </button>
          ))}
        </div>

        <div className="flex items-baseline gap-1">
          {billingCycle === "annual" && (
            <span className="text-sm text-slate-400 dark:text-slate-500 line-through mr-1">
              {formatPrice(monthlyPrice, selectedCurrency)}
            </span>
          )}
          <span className="text-2xl font-bold text-slate-900 dark:text-slate-50">{displayPrice}</span>
          <span className="text-sm text-slate-400 dark:text-slate-500">/month</span>
        </div>
        {billingCycle === "annual" && (
          <p className="text-xs text-green-600 font-medium">
            {annualTotal}/year — save {discount}%
          </p>
        )}
        <p className="text-xs text-slate-500 dark:text-slate-400">{details.tagline}</p>

        {trialStatus === "GracePeriod" && (
          <div className="rounded-lg border border-amber-200 bg-amber-50 p-3">
            <p className="text-sm text-amber-800">
              Your {details.label} free trial has ended. Subscribe now to keep access.
            </p>
          </div>
        )}

        {trialStatus === "Expired" && (
          <div className="rounded-lg border border-rose-200 dark:border-rose-900 bg-rose-50 dark:bg-rose-950/30 p-3">
            <p className="text-sm text-red-800">
              Your {details.label} free trial has expired. Subscribe at {displayPrice}/month to keep your {details.label} features.
            </p>
          </div>
        )}

        <ul className="space-y-1.5">
          {details.features.map((f) => (
            <li key={f.text} className={`text-sm flex items-center gap-2 ${f.included ? "text-slate-600 dark:text-slate-400" : "text-slate-400 dark:text-slate-500 line-through"}`}>
              <span className={`text-xs ${f.included ? "text-green-500" : "text-slate-300 dark:text-slate-600"}`}>
                {f.included ? "\u2713" : "\u2717"}
              </span>
              {f.text}
            </li>
          ))}
        </ul>

        {subError && <p className="text-xs text-rose-500 dark:text-rose-400">{subError}</p>}

        {pendingPayment && (
          <div className="rounded-lg border border-amber-200 bg-amber-50 p-3">
            <p className="text-sm text-amber-800">
              Your payment is being processed. This usually takes 2-5 minutes for mobile money.
              Your plan will activate automatically — you&apos;ll receive a WhatsApp confirmation.
            </p>
          </div>
        )}

        {/* Payment method picker for Flutterwave */}
        {payMethodPick && (
          <div className="rounded-lg border border-cyan-200 bg-cyan-50 p-4 space-y-3">
            <p className="text-sm font-medium text-slate-700 dark:text-slate-300">How would you like to pay?</p>
            <div className="grid grid-cols-1 gap-2">
              <button
                onClick={() => openFlutterwaveCheckout(true)}
                className="flex items-center justify-between px-4 py-3 rounded-lg border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 hover:border-cyan-300 hover:bg-cyan-50 transition-colors text-left"
              >
                <div>
                  <p className="text-sm font-medium text-slate-900 dark:text-slate-50">Card payment</p>
                  <p className="text-xs text-green-600">Auto-renews — no action needed at renewal</p>
                </div>
                <span className="text-xs text-slate-400 dark:text-slate-500">Visa, Mastercard</span>
              </button>
              <button
                onClick={() => openFlutterwaveCheckout(false)}
                className="flex items-center justify-between px-4 py-3 rounded-lg border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 hover:border-cyan-300 hover:bg-cyan-50 transition-colors text-left"
              >
                <div>
                  <p className="text-sm font-medium text-slate-900 dark:text-slate-50">Mobile money / Bank transfer</p>
                  <p className="text-xs text-amber-600">Manual renewal — you renew before expiry</p>
                </div>
                <span className="text-xs text-slate-400 dark:text-slate-500">M-Pesa, MoMo, USSD</span>
              </button>
            </div>
            <button onClick={() => setPayMethodPick(null)} className="text-xs text-slate-400 dark:text-slate-500 hover:text-slate-600">
              Cancel
            </button>
          </div>
        )}

        {failedVerify && (
          <Button
            size="sm"
            variant="outline"
            className="w-full mt-1"
            onClick={async () => {
              try {
                await api.post("/subscription/verify-flutterwave", failedVerify);
                setFailedVerify(null);
                setSubError(null);
                qc.invalidateQueries({ queryKey: ["plan-status"] });
              } catch {
                setSubError("Verification still failing. Please contact support.");
              }
            }}
          >
            Retry verification
          </Button>
        )}

        {isBillable && !isSubscriber && (
          <div className="pt-3 space-y-3">
            <Button
              className="w-full h-11 text-base font-semibold bg-cyan-600 hover:bg-cyan-700 text-white shadow-sm"
              onClick={() => handleSubscribe(plan)}
              disabled={subscribing !== null}
            >
              {subscribing === plan ? "Redirecting..." : `Subscribe to ${details.label} — ${btnPrice(plan)}`}
            </Button>
            {plan !== "business" && (
              <div className="space-y-2">
                <p className="text-xs text-slate-400 dark:text-slate-500">Or choose a different plan:</p>
                {PLAN_ORDER
                  .filter((key) => key !== plan && key !== "business")
                  .map((key) => {
                    const d = PLAN_DETAILS[key];
                    if (!d) return null;
                    return (
                      <Button
                        key={key}
                        variant="outline"
                        className="w-full h-9 text-sm font-medium"
                        onClick={() => handleSubscribe(key)}
                        disabled={subscribing !== null}
                      >
                        {subscribing === key ? "Redirecting..." : `${d.label} — ${btnPrice(key)}`}
                      </Button>
                    );
                  })}
                <a
                  href={`mailto:contact@ojunai.com?subject=${encodeURIComponent("Business Plan Enquiry")}&body=${encodeURIComponent(
                    `Hi Ojunai Team,\n\nI'm interested in the Business plan.\n\nI'd like to learn more about:\n- Multi-branch support\n- Custom report exports\n- Bulk CSV import\n- API access\n- Unlimited staff accounts\n\nPlease get in touch to discuss my requirements.\n\nBusiness name: ${business?.name ?? "[Your business name]"}\n\nThank you.`
                  )}`}
                  className="flex items-center justify-center w-full h-9 text-sm font-medium rounded-lg border-2 border-amber-400 bg-amber-50 text-amber-800 hover:bg-amber-100 hover:border-amber-500 transition-colors"
                >
                  Business — Contact Us
                </a>
              </div>
            )}
            <p className="text-xs text-slate-400 dark:text-slate-500 text-center mt-2">
              Card payments auto-renew. Mobile money requires manual renewal.{" "}
              <a href="/terms" className="underline hover:text-slate-600">Terms</a> &{" "}
              <a href="/privacy" className="underline hover:text-slate-600">Privacy</a>.
            </p>
          </div>
        )}

        {isBillable && isSubscriber && (
          <div className="pt-3 space-y-3">
            {/* Upgrade buttons — plans above current */}
            {PLAN_ORDER.filter((key) => PLAN_ORDER.indexOf(key) > PLAN_ORDER.indexOf(plan) && key !== "business").length > 0 && (
              <>
                <p className="text-xs text-slate-500 dark:text-slate-400">Upgrade:</p>
                {PLAN_ORDER
                  .filter((key) => PLAN_ORDER.indexOf(key) > PLAN_ORDER.indexOf(plan) && key !== "business")
                  .map((key) => {
                    const d = PLAN_DETAILS[key];
                    if (!d) return null;
                    return (
                      <Button
                        key={key}
                        className="w-full h-11 text-base font-semibold bg-cyan-600 hover:bg-cyan-700 text-white shadow-sm"
                        onClick={() => setConfirmTarget(key)}
                        disabled={subscribing !== null}
                      >
                        {subscribing === key ? "Redirecting..." : `Upgrade to ${d.label} — ${btnPrice(key)}`}
                      </Button>
                    );
                  })}
              </>
            )}

            {/* Business — contact us */}
            {PLAN_ORDER.indexOf(plan) < PLAN_ORDER.indexOf("business") && (
              <a
                href={`mailto:contact@ojunai.com?subject=${encodeURIComponent("Business Plan Enquiry")}&body=${encodeURIComponent(
                  `Hi Ojunai Team,\n\nI'm currently on the ${details.label} plan and I'm interested in upgrading to the Business plan.\n\nI'd like to learn more about:\n- Multi-branch support\n- Custom report exports\n- Bulk CSV import\n- API access\n- Unlimited staff accounts\n\nPlease get in touch to discuss my requirements.\n\nBusiness name: ${business?.name ?? "[Your business name]"}\n\nThank you.`
                )}`}
                className="flex items-center justify-center w-full h-11 text-base font-semibold rounded-lg border-2 border-amber-400 bg-amber-50 text-amber-800 hover:bg-amber-100 hover:border-amber-500 transition-colors shadow-sm"
              >
                Upgrade to Business — Contact Us
              </a>
            )}

            {/* Downgrade buttons — plans below current (not shown on Starter since there's nothing below) */}
            {PLAN_ORDER.indexOf(plan) > 0 && (
              <>
                <div className="border-t border-slate-100 dark:border-slate-800 pt-3">
                  <p className="text-xs text-slate-400 dark:text-slate-500 mb-2">Downgrade:</p>
                  <div className="flex flex-wrap gap-2">
                    {PLAN_ORDER
                      .filter((key) => PLAN_ORDER.indexOf(key) < PLAN_ORDER.indexOf(plan))
                      .map((key) => {
                        const d = PLAN_DETAILS[key];
                        if (!d) return null;
                        return (
                          <button
                            key={key}
                            onClick={() => handleDowngrade(key)}
                            disabled={subscribing !== null}
                            className="text-xs px-3 py-1.5 rounded-md border border-slate-200 dark:border-slate-800 text-slate-500 dark:text-slate-400 hover:bg-slate-50 dark:hover:bg-slate-800 hover:text-slate-700 dark:hover:text-slate-300 hover:border-slate-300 dark:hover:border-slate-700 transition-colors disabled:opacity-50"
                          >
                            {subscribing === key ? "..." : `Downgrade to ${d.label} — ${btnPrice(key)}`}
                          </button>
                        );
                      })}
                  </div>
                </div>
              </>
            )}
            <p className="text-xs text-slate-400 dark:text-slate-500 text-center mt-2">
              Card payments auto-renew. Mobile money requires manual renewal.{" "}
              <a href="/terms" className="underline hover:text-slate-600">Terms</a> &{" "}
              <a href="/privacy" className="underline hover:text-slate-600">Privacy</a>.
            </p>
          </div>
        )}

        {/* Expiry banner for manual (non-auto-renew) subscribers */}
        {isBillable && hasActiveSub && !isAutoRenew && isExpiringSoon && (
          <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 space-y-2">
            <p className="text-sm text-amber-800">
              Your plan {daysUntilExpiry === 0 ? "expires today" : `expires in ${daysUntilExpiry} day${daysUntilExpiry !== 1 ? "s" : ""}`}.
              Renew now to keep your {details.label} features.
            </p>
            <Button
              className="w-full h-10 text-sm font-semibold bg-amber-600 hover:bg-amber-700 text-white"
              onClick={() => handleSubscribe(plan)}
              disabled={subscribing !== null}
            >
              {subscribing === plan ? "Redirecting..." : `Renew ${details.label} — ${btnPrice(plan)}`}
            </Button>
            {billingCycle === "monthly" && (
              <p className="text-xs text-slate-500 dark:text-slate-400 mt-1">
                Switch to annual and save {discount}% — renew once a year instead of every month.
              </p>
            )}
          </div>
        )}

        {planStatus?.pendingPlanChange && planStatus.subscriptionEndsAt && (
          <div className="rounded-lg border border-blue-200 bg-blue-50 p-3 flex items-center justify-between">
            <p className="text-sm text-blue-800">
              Scheduled to switch to {planStatus.pendingPlanChange.charAt(0).toUpperCase() + planStatus.pendingPlanChange.slice(1)} on {new Date(planStatus.subscriptionEndsAt).toLocaleDateString()}.
            </p>
            <button
              onClick={async () => {
                try {
                  await api.post("/subscription/cancel-pending-change");
                  qc.invalidateQueries({ queryKey: ["plan-status"] });
                } catch (err: unknown) {
                    const ax = err as { response?: { data?: { errors?: string[] } } };
                    toast.error("Couldn't save change", ax.response?.data?.errors?.[0] ?? "Please try again.");
                  }
              }}
              className="text-xs font-medium text-blue-700 hover:text-blue-900 whitespace-nowrap ml-3 underline"
            >
              Cancel downgrade
            </button>
          </div>
        )}

        {isBillable && (hasActiveSub || isSubscriber) && (
          <div className="pt-2">
            <button
              onClick={handleCancel}
              disabled={cancelling}
              className="text-xs text-rose-500 dark:text-rose-400 hover:underline"
            >
              {cancelling ? "Cancelling..." : "Cancel renewal"}
            </button>
            {planStatus?.subscriptionEndsAt && (
              <p className="text-xs text-slate-400 dark:text-slate-500 mt-1">
                {isAutoRenew
                  ? `Auto-renews ${new Date(planStatus.subscriptionEndsAt).toLocaleDateString()}. Your card will be charged automatically.`
                  : `Expires ${new Date(planStatus.subscriptionEndsAt).toLocaleDateString()}.`}
                {!isAutoRenew && paymentMethod && paymentMethod !== "card" && (
                  <span> Renew via {paymentMethod === "mobilemoney" ? "mobile money" : paymentMethod.replace("_", " ")} before this date.</span>
                )}
              </p>
            )}
          </div>
        )}

        {!isBillable && (
          <p className="text-xs text-green-600 pt-1">Complimentary account — no billing required.</p>
        )}

        {/* Auto-renew informational banner — shows when user is within 7 days of renewal AND auto-renew is on.
            Different from the action-required "expiring soon" warning above, which shows only when auto-renew is off. */}
        {isBillable && hasActiveSub && isAutoRenew && isExpiringSoon && (
          <div className="rounded-lg border border-cyan-200 dark:border-cyan-900 bg-cyan-50 dark:bg-cyan-950/30 p-3 mt-2">
            <p className="text-sm text-cyan-800 dark:text-cyan-300">
              Your <span className="font-semibold">{details.label}</span> plan auto-renews in {daysUntilExpiry === 0 ? "less than 24 hours" : `${daysUntilExpiry} day${daysUntilExpiry !== 1 ? "s" : ""}`} for <span className="font-semibold">{btnPrice(plan)}</span>.
            </p>
            <p className="text-xs text-cyan-700/80 dark:text-cyan-400/70 mt-1">
              You don&rsquo;t need to do anything — your card will be charged automatically. To cancel, use the link above.
            </p>
          </div>
        )}
      </CardContent>

      {/* Confirm-price modal — shown before any plan-change checkout so the user sees the actual charge.
          Computes mid-cycle delta client-side using the same rule the backend enforces (≥10 days remaining,
          new plan price > current). Display only — backend recomputes & enforces independently. */}
      {confirmTarget && (() => {
        const targetDetails = PLAN_DETAILS[confirmTarget];
        if (!targetDetails) return null;
        const targetPrice = getPrice(confirmTarget, billingCycle, selectedCurrency);
        const currentPrice = isSubscriber ? getPrice(plan, billingCycle, selectedCurrency) : 0;

        // Mid-cycle delta eligibility (mirrors PaystackService.InitializeSubscriptionAsync)
        const daysRemaining = subEndsAt ? (subEndsAt.getTime() - Date.now()) / 86400000 : 0;
        const isMidCycleUpgrade =
          billingCycle === "monthly" &&
          hasActiveSub &&
          subEndsAt && subEndsAt > new Date() &&
          targetPrice > currentPrice &&
          daysRemaining >= 10;

        const deltaAmount = Math.max(0, targetPrice - currentPrice);
        const chargedNow = isMidCycleUpgrade ? deltaAmount : targetPrice;

        return (
          <Dialog open onOpenChange={(o) => !o && setConfirmTarget(null)}>
            <DialogContent>
              <DialogHeader>
                <DialogTitle>Upgrade to {targetDetails.label}</DialogTitle>
              </DialogHeader>
              <div className="space-y-3 text-sm">
                <div className="rounded-lg border border-slate-200 dark:border-slate-800 bg-slate-50/50 dark:bg-slate-950/50 p-3 space-y-1.5">
                  <div className="flex items-center justify-between">
                    <span className="text-slate-600 dark:text-slate-400">You pay today</span>
                    <span className="text-2xl font-bold text-slate-900 dark:text-slate-50 tabular-nums">
                      {formatPrice(chargedNow, selectedCurrency)}
                    </span>
                  </div>
                  {isMidCycleUpgrade && (
                    <p className="text-[11px] text-slate-500 dark:text-slate-400">
                      Mid-cycle upgrade — you&rsquo;re only charged the difference between your current {details.label} ({formatPrice(currentPrice, selectedCurrency)}) and {targetDetails.label} ({formatPrice(targetPrice, selectedCurrency)}).
                    </p>
                  )}
                </div>

                <div className="rounded-lg border border-slate-200 dark:border-slate-800 p-3 space-y-1">
                  <p className="text-[10px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">What happens next</p>
                  <ul className="text-xs text-slate-600 dark:text-slate-400 space-y-1 mt-1.5">
                    <li>• Your plan switches to <span className="font-semibold text-slate-900 dark:text-slate-50">{targetDetails.label}</span> immediately after payment</li>
                    {isMidCycleUpgrade && subEndsAt ? (
                      <>
                        <li>• Your current cycle continues until <span className="font-semibold text-slate-900 dark:text-slate-50">{subEndsAt.toLocaleDateString()}</span></li>
                        <li>• On that date, auto-renew kicks in at <span className="font-semibold text-slate-900 dark:text-slate-50">{formatPrice(targetPrice, selectedCurrency)}/mo</span></li>
                      </>
                    ) : (
                      <>
                        <li>• A new {billingCycle} cycle begins today, ending <span className="font-semibold text-slate-900 dark:text-slate-50">{new Date(Date.now() + (billingCycle === "annual" ? 365 : 30) * 86400000).toLocaleDateString()}</span></li>
                        <li>• Auto-renews at <span className="font-semibold text-slate-900 dark:text-slate-50">{formatPrice(targetPrice, selectedCurrency)}/{billingCycle === "annual" ? "yr" : "mo"}</span> unless you cancel</li>
                      </>
                    )}
                    <li>• You can cancel anytime — you keep access until the end of the current cycle</li>
                  </ul>
                </div>
              </div>
              <DialogFooter>
                <Button variant="outline" onClick={() => setConfirmTarget(null)} disabled={subscribing !== null}>Cancel</Button>
                <Button
                  onClick={() => { const t = confirmTarget; setConfirmTarget(null); handleSubscribe(t); }}
                  disabled={subscribing !== null}
                  className="bg-cyan-600 hover:bg-cyan-700 text-white"
                >
                  {subscribing === confirmTarget ? "Redirecting..." : `Pay ${formatPrice(chargedNow, selectedCurrency)} & upgrade`}
                </Button>
              </DialogFooter>
            </DialogContent>
          </Dialog>
        );
      })()}
    </Card>
  );
}

function EditBusinessDialog({
  business,
  open,
  onClose,
  onSaved,
}: {
  business: BusinessShape | null;
  open: boolean;
  onClose: () => void;
  onSaved: (b: BusinessShape) => void;
}) {
  const [form, setForm] = useState({
    businessType: "",
    currency: "NGN",
    city: "",
    state: "",
    country: "",
    address: "",
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [initialized, setInitialized] = useState(false);

  // Initialize form when business loads or dialog opens
  if (business && open && !initialized) {
    setForm({
      businessType: business.businessType ?? "",
      currency: business.currency ?? "NGN",
      city: business.city ?? "",
      state: business.state ?? "",
      country: business.country ?? "",
      address: business.address ?? "",
    });
    setInitialized(true);
  }

  async function handleSave() {
    if (!business) return;
    setSaving(true);
    setError(null);
    try {
      const { data } = await api.put<{ data: BusinessShape }>("/business", {
        businessType: form.businessType || null,
        currency: form.currency,
        city: form.city,
        state: form.state,
        country: form.country,
        address: form.address,
      });
      onSaved(data.data!);
      handleClose();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to save");
    } finally {
      setSaving(false);
    }
  }

  function handleClose() {
    setForm({ businessType: "", currency: "NGN", city: "", state: "", country: "", address: "" });
    setError(null);
    setInitialized(false);
    onClose();
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && handleClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Edit Business</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label>Business Name</Label>
            <Input value={business?.name ?? ""} disabled className="bg-slate-50 dark:bg-slate-950 text-slate-500 dark:text-slate-400" />
            <p className="text-xs text-slate-400 dark:text-slate-500 mt-1">Business name cannot be changed.</p>
          </div>
          <div>
            <Label>Type</Label>
            <Input
              value={form.businessType}
              onChange={(e) => setForm({ ...form, businessType: e.target.value })}
              placeholder="e.g. Retail, Food, Services"
            />
          </div>
          <div>
            <Label>Currency</Label>
            <select
              className="w-full h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 text-sm"
              value={form.currency}
              onChange={(e) => setForm({ ...form, currency: e.target.value })}
            >
              {CURRENCIES.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <Label>City</Label>
              <Input
                value={form.city}
                onChange={(e) => setForm({ ...form, city: e.target.value })}
                placeholder="e.g. Lagos"
              />
            </div>
            <div>
              <Label>Town / State</Label>
              <Input
                value={form.state}
                onChange={(e) => setForm({ ...form, state: e.target.value })}
                placeholder="e.g. Ikeja"
              />
            </div>
          </div>
          <div>
            <Label>Address (for receipts)</Label>
            <Input
              value={form.address}
              onChange={(e) => setForm({ ...form, address: e.target.value })}
              placeholder="e.g. 12 Awolowo Way, Ikeja"
            />
            <p className="text-[11px] text-slate-400 dark:text-slate-500 mt-1">Shown on PDF receipts. Optional.</p>
          </div>
          <div>
            <Label>Country</Label>
            <select
              className="w-full h-9 px-2 rounded-md border border-slate-200 dark:border-slate-800 text-sm bg-white dark:bg-slate-900"
              value={form.country}
              onChange={(e) => {
                const country = e.target.value;
                const info = COUNTRIES[country];
                setForm({
                  ...form,
                  country,
                  currency: info?.currency ?? form.currency,
                });
              }}
            >
              <option value="">Select country</option>
              {COUNTRY_NAMES.map((c) => (
                <option key={c} value={c}>{c}</option>
              ))}
            </select>
          </div>
          {error && <p className="text-xs text-rose-500 dark:text-rose-400">{error}</p>}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={handleClose} disabled={saving}>Cancel</Button>
          <Button onClick={handleSave} disabled={saving}>{saving ? "Saving…" : "Save Changes"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

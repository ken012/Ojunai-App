"use client";

import { useState, useEffect, Suspense } from "react";
import { useSearchParams } from "next/navigation";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { getStoredUser } from "@/lib/auth";
import { api } from "@/lib/api";
import { useBusiness, useUser, useDataSync } from "@/lib/data-sync";
import { usePlanStatus } from "@/lib/use-plan-status";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Separator } from "@/components/ui/separator";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import { MessageSquare, Building2, User, Pencil, Bell, Tags, X, Plus, Users, Trash2, KeyRound, CreditCard } from "lucide-react";
import { CATEGORY_NAMES } from "@/lib/categories";
import { hasPermission, Permission } from "@/lib/permissions";
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
    <div className="space-y-6 max-w-2xl">
      <div>
        <h2 className="text-2xl font-bold text-slate-900">Settings</h2>
        <p className="text-slate-500 text-sm mt-0.5">Account and business information</p>
      </div>

      {showSuccess && (
        <div className="rounded-lg border border-green-200 bg-green-50 px-4 py-3 flex items-center justify-between">
          <p className="text-sm text-green-800 font-medium">Payment successful! Your plan is now active.</p>
          <button onClick={() => setShowSuccess(false)} className="text-green-600 hover:text-green-800">
            <X size={16} />
          </button>
        </div>
      )}

      {/* Business */}
      <Card>
        <CardHeader className="pb-2 flex flex-row items-center justify-between">
          <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
            <Building2 size={15} />
            Business
          </CardTitle>
          {hasPermission(Permission.ManageSettings) && (
            <button
              onClick={() => setEditing(true)}
              className="p-1 rounded hover:bg-slate-100 text-slate-500 hover:text-slate-900"
              title="Edit business"
            >
              <Pencil size={14} />
            </button>
          )}
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="flex justify-between">
            <span className="text-sm text-slate-500">Business Name</span>
            <span className="text-sm font-medium">{business?.name ?? "—"}</span>
          </div>
          <Separator />
          <div className="flex justify-between">
            <span className="text-sm text-slate-500">Type</span>
            <span className="text-sm">{business?.businessType ?? "—"}</span>
          </div>
          <Separator />
          <div className="flex justify-between">
            <span className="text-sm text-slate-500">Currency</span>
            <span className="text-sm font-mono">{business?.currency ?? "NGN"}</span>
          </div>
          <Separator />
          <div className="flex justify-between">
            <span className="text-sm text-slate-500">City</span>
            <span className="text-sm">{business?.city ?? "—"}</span>
          </div>
          <Separator />
          <div className="flex justify-between">
            <span className="text-sm text-slate-500">Town / State</span>
            <span className="text-sm">{business?.state ?? "—"}</span>
          </div>
          <Separator />
          <div className="flex justify-between">
            <span className="text-sm text-slate-500">Country</span>
            <span className="text-sm">{business?.country ?? "—"}</span>
          </div>
          <Separator />
          <div className="flex justify-between">
            <span className="text-sm text-slate-500">Large Sale Alert</span>
            <span className="text-sm font-mono">{cs}{(business?.largeSaleThreshold && business.largeSaleThreshold > 0 ? business.largeSaleThreshold : 100000).toLocaleString()}</span>
          </div>
          <Separator />
          <div className="flex justify-between">
            <span className="text-sm text-slate-500">Status</span>
            <Badge variant={business?.isActive ? "default" : "secondary"}>
              {business?.isActive ? "Active" : "Inactive"}
            </Badge>
          </div>
        </CardContent>
      </Card>

      {/* Plan */}
      <PlanCard business={business} />

      {/* Alerts — Owner/Admin only */}
      {hasPermission(Permission.ManageSettings) && <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
            <Bell size={15} />
            WhatsApp Alerts
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          <p className="text-xs text-slate-400">These alerts are sent to your WhatsApp automatically.</p>
          {[
            { key: "alertLowStock" as const, label: "Low Stock Alerts", desc: "When a product drops below its threshold after a sale" },
            { key: "alertDailySummary" as const, label: "Daily Summary", desc: "Sales, expenses, and net sent at 8 PM daily" },
            { key: "alertLargeSale" as const, label: "Large Sale Alert", desc: `When a sale exceeds ${cs}${(business?.largeSaleThreshold && business.largeSaleThreshold > 0 ? business.largeSaleThreshold : 100000).toLocaleString()}` },
          ].map(({ key, label, desc }) => (
            <label key={key} className="flex items-start gap-3 cursor-pointer">
              <input
                type="checkbox"
                className="mt-1 h-4 w-4 rounded border-slate-300 text-sky-600 focus:ring-sky-500"
                checked={business?.[key] ?? true}
                onChange={async (e) => {
                  try {
                    const { data } = await api.put<{ data: typeof business }>("/business", { [key]: e.target.checked });
                    const updated = data.data!;
                    setBusiness(updated);
                    if (typeof window !== "undefined") { localStorage.setItem("bp_business", JSON.stringify(updated)); refreshSync(); }
                  } catch { /* silent */ }
                }}
              />
              <div>
                <p className="text-sm font-medium text-slate-700">{label}</p>
                <p className="text-xs text-slate-400">{desc}</p>
              </div>
            </label>
          ))}
        </CardContent>
      </Card>}

      {/* Team Members — Owner/Admin only */}
      {hasPermission(Permission.ManageStaff) && <TeamMembersCard />}

      {/* Manage Categories — Owner/Admin only */}
      {hasPermission(Permission.ManageSettings) &&
      <ManageCategoriesCard business={business} onUpdate={(updated) => {
        setBusiness(updated);
        if (typeof window !== "undefined") { localStorage.setItem("bp_business", JSON.stringify(updated)); refreshSync(); }
      }} />}

      {/* User */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
            <User size={15} />
            Your Account
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="flex justify-between">
            <span className="text-sm text-slate-500">Name</span>
            <span className="text-sm font-medium">{user?.fullName ?? "—"}</span>
          </div>
          <Separator />
          <div className="flex justify-between">
            <span className="text-sm text-slate-500">Phone</span>
            <span className="text-sm font-mono">{user?.phoneNumber ?? "—"}</span>
          </div>
          <Separator />
          <div className="flex justify-between">
            <span className="text-sm text-slate-500">Email</span>
            <span className="text-sm">{user?.email ?? "—"}</span>
          </div>
          <Separator />
          <div className="flex justify-between">
            <span className="text-sm text-slate-500">Role</span>
            <Badge variant="outline">{user?.role ?? "—"}</Badge>
          </div>
        </CardContent>
      </Card>

      {/* WhatsApp */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
            <MessageSquare size={15} className="text-green-500" />
            WhatsApp Integration
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          <p className="text-sm text-slate-600">
            BizPilot understands natural language. Here are some example commands you can send via WhatsApp:
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
              <div key={example} className="bg-slate-50 border rounded-lg px-3 py-2 text-sm text-slate-700 font-mono">
                &ldquo;{example}&rdquo;
              </div>
            ))}
          </div>
          <p className="text-xs text-slate-400 mt-2">
            Messages are processed by AI and executed automatically when confidence is high.
          </p>

          <Separator className="my-3" />

          <a
            href={TWILIO_WA_LINK}
            target="_blank"
            rel="noreferrer"
            className="flex items-center justify-center gap-2 w-full px-4 py-3 bg-green-500 hover:bg-green-600 text-white rounded-lg font-medium transition-colors"
          >
            <MessageSquare size={16} />
            Chat with BizPilot on WhatsApp
          </a>
        </CardContent>
      </Card>

      <EditBusinessDialog
        business={business}
        open={editing}
        onClose={() => setEditing(false)}
        onSaved={(updated) => {
          setBusiness(updated);
          if (typeof window !== "undefined") {
            localStorage.setItem("bp_business", JSON.stringify(updated));
            refreshSync();
          }
        }}
      />
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

function TeamMembersCard() {
  const qc = useQueryClient();
  const [adding, setAdding] = useState(false);
  const [form, setForm] = useState({ fullName: "", phoneNumber: "", password: "", email: "", role: "Sales" });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

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
    } catch { /* silent */ }
  }

  return (
    <Card>
      <CardHeader className="pb-2 flex flex-row items-center justify-between">
        <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
          <Users size={15} />
          Team Members
        </CardTitle>
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
          <div className="border rounded-lg p-3 space-y-2 bg-slate-50">
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
                  className="w-full h-9 px-2 rounded-md border border-slate-200 text-sm bg-white"
                  value={form.role}
                  onChange={(e) => setForm({ ...form, role: e.target.value })}
                >
                  {STAFF_ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
                </select>
              </div>
            </div>
            {error && <p className="text-xs text-red-500">{error}</p>}
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
                    <p className="text-sm font-medium text-slate-900 truncate">{s.fullName}</p>
                    <Badge variant={s.role === "Owner" ? "default" : "secondary"} className="text-xs">{s.role}</Badge>
                  </div>
                  <p className="text-xs text-slate-400 font-mono">{s.phoneNumber}</p>
                </div>
                {s.role !== "Owner" && (
                  <div className="flex items-center gap-1">
                    <button
                      onClick={async () => {
                        const pw = prompt(`Set temporary password for ${s.fullName}:`);
                        if (!pw || pw.length < 8) { if (pw) alert("Password must be at least 8 characters."); return; }
                        try {
                          await api.post(`/staff/${s.id}/reset-password`, { newPassword: pw });
                          alert(`Password reset for ${s.fullName}. They must change it on next login.`);
                        } catch { alert("Failed to reset password."); }
                      }}
                      className="p-1 rounded hover:bg-sky-50 text-slate-400 hover:text-sky-600"
                      title="Reset password"
                    >
                      <KeyRound size={14} />
                    </button>
                    <button
                      onClick={() => handleRemove(s.id)}
                      className="p-1 rounded hover:bg-red-50 text-slate-400 hover:text-red-500"
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
          <p className="text-xs text-slate-400 italic">No team members yet. Add staff to let them record sales via WhatsApp or the dashboard.</p>
        )}
      </CardContent>
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
  largeSaleThreshold?: number;
  customCategories?: string[];
  alertLowStock?: boolean;
  alertDailySummary?: boolean;
  alertLargeSale?: boolean;
  plan?: string;
  trialEndsAt?: string;
  isActive: boolean;
};

function ManageCategoriesCard({
  business,
  onUpdate,
}: {
  business: BusinessShape | null;
  onUpdate: (b: BusinessShape) => void;
}) {
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
    } catch { /* silent */ } finally { setSaving(false); }
  }

  async function removeCategory(cat: string) {
    if (!business) return;
    setSaving(true);
    try {
      const updated = customCats.filter((c) => c !== cat);
      const { data } = await api.put<{ data: BusinessShape }>("/business", { customCategories: updated });
      onUpdate(data.data!);
    } catch { /* silent */ } finally { setSaving(false); }
  }

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
          <Tags size={15} />
          Manage Categories
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <p className="text-xs text-slate-400">
          12 preset categories are always available. Add your own custom categories below.
        </p>

        {/* Preset categories (read-only) */}
        <div>
          <Label className="text-xs text-slate-500">Preset categories</Label>
          <div className="flex flex-wrap gap-1.5 mt-1">
            {CATEGORY_NAMES.map((c) => (
              <span key={c} className="inline-flex items-center px-2 py-0.5 rounded-full text-xs bg-slate-100 text-slate-600">
                {c}
              </span>
            ))}
          </div>
        </div>

        <Separator />

        {/* Custom categories (editable) */}
        <div>
          <Label className="text-xs text-slate-500">Your custom categories</Label>
          {customCats.length === 0 ? (
            <p className="text-xs text-slate-300 mt-1 italic">No custom categories yet</p>
          ) : (
            <div className="flex flex-wrap gap-1.5 mt-1">
              {customCats.map((c) => (
                <span key={c} className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs bg-sky-50 text-sky-700 border border-sky-200">
                  {c}
                  <button
                    onClick={() => removeCategory(c)}
                    className="hover:text-red-500"
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
    color: "bg-slate-100 text-slate-700",
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
    color: "bg-sky-100 text-sky-700",
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
  const [payMethodPick, setPayMethodPick] = useState<{ plan: string; result: Record<string, unknown> } | null>(null);
  const [failedVerify, setFailedVerify] = useState<{transactionId?: string; txRef?: string} | null>(null);
  const [billingCycle, setBillingCycle] = useState<"monthly" | "annual">("monthly");
  const [selectedCurrency, setSelectedCurrency] = useState<SupportedCurrency>(getDefaultCurrency());

  useEffect(() => {
    if (business?.currency) setSelectedCurrency(toBillingCurrency(business.currency));
  }, [business?.currency]);

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
        title: "BizPilot AI",
        description: `${(result.plan as string).charAt(0).toUpperCase() + (result.plan as string).slice(1)} Plan — ${result.billingCycle}`,
        logo: "https://app.bizpilot-ai.com/favicon.ico",
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
            setSubError(msg.replace("PENDING:", ""));
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
        <CardTitle className="text-sm font-semibold text-slate-700 flex items-center gap-2">
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
            <span className="text-xs text-red-600 font-medium">
              Trial ended — grace period active
            </span>
          )}
          {trialStatus === "Expired" && (
            <span className="text-xs text-red-600 font-medium">
              Trial expired — subscribe to keep access
            </span>
          )}
        </div>

        {/* Billing cycle toggle */}
        <div className="flex items-center bg-slate-100 rounded-lg p-1">
          <button
            onClick={() => setBillingCycle("monthly")}
            className={`flex-1 px-3 py-1.5 rounded-md text-sm font-medium transition-colors ${
              billingCycle === "monthly" ? "bg-white shadow-sm text-slate-900" : "text-slate-500 hover:text-slate-700"
            }`}
          >
            Monthly
          </button>
          <button
            onClick={() => setBillingCycle("annual")}
            className={`flex-1 px-3 py-1.5 rounded-md text-sm font-medium transition-colors ${
              billingCycle === "annual" ? "bg-white shadow-sm text-slate-900" : "text-slate-500 hover:text-slate-700"
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
                  ? "bg-sky-100 text-sky-700 border border-sky-200"
                  : "bg-slate-50 text-slate-500 border border-slate-200 hover:bg-slate-100"
              }`}
            >
              {CURRENCY_META[c].symbol} {c}
            </button>
          ))}
        </div>

        <div className="flex items-baseline gap-1">
          {billingCycle === "annual" && (
            <span className="text-sm text-slate-400 line-through mr-1">
              {formatPrice(monthlyPrice, selectedCurrency)}
            </span>
          )}
          <span className="text-2xl font-bold text-slate-900">{displayPrice}</span>
          <span className="text-sm text-slate-400">/month</span>
        </div>
        {billingCycle === "annual" && (
          <p className="text-xs text-green-600 font-medium">
            {annualTotal}/year — save {discount}%
          </p>
        )}
        <p className="text-xs text-slate-500">{details.tagline}</p>

        {trialStatus === "GracePeriod" && (
          <div className="rounded-lg border border-amber-200 bg-amber-50 p-3">
            <p className="text-sm text-amber-800">
              Your {details.label} free trial has ended. Subscribe now to keep access.
            </p>
          </div>
        )}

        {trialStatus === "Expired" && (
          <div className="rounded-lg border border-red-200 bg-red-50 p-3">
            <p className="text-sm text-red-800">
              Your {details.label} free trial has expired. Subscribe at {displayPrice}/month to keep your {details.label} features.
            </p>
          </div>
        )}

        <ul className="space-y-1.5">
          {details.features.map((f) => (
            <li key={f.text} className={`text-sm flex items-center gap-2 ${f.included ? "text-slate-600" : "text-slate-400 line-through"}`}>
              <span className={`text-xs ${f.included ? "text-green-500" : "text-slate-300"}`}>
                {f.included ? "\u2713" : "\u2717"}
              </span>
              {f.text}
            </li>
          ))}
        </ul>

        {subError && <p className="text-xs text-red-500">{subError}</p>}

        {/* Payment method picker for Flutterwave */}
        {payMethodPick && (
          <div className="rounded-lg border border-sky-200 bg-sky-50 p-4 space-y-3">
            <p className="text-sm font-medium text-slate-700">How would you like to pay?</p>
            <div className="grid grid-cols-1 gap-2">
              <button
                onClick={() => openFlutterwaveCheckout(true)}
                className="flex items-center justify-between px-4 py-3 rounded-lg border border-slate-200 bg-white hover:border-sky-300 hover:bg-sky-50 transition-colors text-left"
              >
                <div>
                  <p className="text-sm font-medium text-slate-900">Card payment</p>
                  <p className="text-xs text-green-600">Auto-renews — no action needed at renewal</p>
                </div>
                <span className="text-xs text-slate-400">Visa, Mastercard</span>
              </button>
              <button
                onClick={() => openFlutterwaveCheckout(false)}
                className="flex items-center justify-between px-4 py-3 rounded-lg border border-slate-200 bg-white hover:border-sky-300 hover:bg-sky-50 transition-colors text-left"
              >
                <div>
                  <p className="text-sm font-medium text-slate-900">Mobile money / Bank transfer</p>
                  <p className="text-xs text-amber-600">Manual renewal — you renew before expiry</p>
                </div>
                <span className="text-xs text-slate-400">M-Pesa, MoMo, USSD</span>
              </button>
            </div>
            <button onClick={() => setPayMethodPick(null)} className="text-xs text-slate-400 hover:text-slate-600">
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
              className="w-full h-11 text-base font-semibold bg-sky-600 hover:bg-sky-700 text-white shadow-sm"
              onClick={() => handleSubscribe(plan)}
              disabled={subscribing !== null}
            >
              {subscribing === plan ? "Redirecting..." : `Subscribe to ${details.label} — ${btnPrice(plan)}`}
            </Button>
            {plan !== "business" && (
              <div className="space-y-2">
                <p className="text-xs text-slate-400">Or choose a different plan:</p>
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
                  href={`mailto:contact@bizpilot-ai.com?subject=${encodeURIComponent("Business Plan Enquiry")}&body=${encodeURIComponent(
                    `Hi BizPilot Team,\n\nI'm interested in the Business plan.\n\nI'd like to learn more about:\n- Multi-branch support\n- Custom report exports\n- Bulk CSV import\n- API access\n- Unlimited staff accounts\n\nPlease get in touch to discuss my requirements.\n\nBusiness name: ${business?.name ?? "[Your business name]"}\n\nThank you.`
                  )}`}
                  className="flex items-center justify-center w-full h-9 text-sm font-medium rounded-lg border-2 border-amber-400 bg-amber-50 text-amber-800 hover:bg-amber-100 hover:border-amber-500 transition-colors"
                >
                  Business — Contact Us
                </a>
              </div>
            )}
            <p className="text-xs text-slate-400 text-center mt-2">
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
                <p className="text-xs text-slate-500">Upgrade:</p>
                {PLAN_ORDER
                  .filter((key) => PLAN_ORDER.indexOf(key) > PLAN_ORDER.indexOf(plan) && key !== "business")
                  .map((key) => {
                    const d = PLAN_DETAILS[key];
                    if (!d) return null;
                    return (
                      <Button
                        key={key}
                        className="w-full h-11 text-base font-semibold bg-sky-600 hover:bg-sky-700 text-white shadow-sm"
                        onClick={() => handleSubscribe(key)}
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
                href={`mailto:contact@bizpilot-ai.com?subject=${encodeURIComponent("Business Plan Enquiry")}&body=${encodeURIComponent(
                  `Hi BizPilot Team,\n\nI'm currently on the ${details.label} plan and I'm interested in upgrading to the Business plan.\n\nI'd like to learn more about:\n- Multi-branch support\n- Custom report exports\n- Bulk CSV import\n- API access\n- Unlimited staff accounts\n\nPlease get in touch to discuss my requirements.\n\nBusiness name: ${business?.name ?? "[Your business name]"}\n\nThank you.`
                )}`}
                className="flex items-center justify-center w-full h-11 text-base font-semibold rounded-lg border-2 border-amber-400 bg-amber-50 text-amber-800 hover:bg-amber-100 hover:border-amber-500 transition-colors shadow-sm"
              >
                Upgrade to Business — Contact Us
              </a>
            )}

            {/* Downgrade buttons — plans below current (not shown on Starter since there's nothing below) */}
            {PLAN_ORDER.indexOf(plan) > 0 && (
              <>
                <div className="border-t border-slate-100 pt-3">
                  <p className="text-xs text-slate-400 mb-2">Downgrade:</p>
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
                            className="text-xs px-3 py-1.5 rounded-md border border-slate-200 text-slate-500 hover:bg-slate-50 hover:text-slate-700 hover:border-slate-300 transition-colors disabled:opacity-50"
                          >
                            {subscribing === key ? "..." : `Downgrade to ${d.label} — ${btnPrice(key)}`}
                          </button>
                        );
                      })}
                  </div>
                </div>
              </>
            )}
            <p className="text-xs text-slate-400 text-center mt-2">
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
              <p className="text-xs text-slate-500 mt-1">
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
                } catch { /* silent */ }
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
              className="text-xs text-red-500 hover:underline"
            >
              {cancelling ? "Cancelling..." : "Cancel renewal"}
            </button>
            {planStatus?.subscriptionEndsAt && (
              <p className="text-xs text-slate-400 mt-1">
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
      </CardContent>
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
    largeSaleThreshold: "",
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
      largeSaleThreshold: business.largeSaleThreshold?.toString() ?? "100000",
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
        largeSaleThreshold: form.largeSaleThreshold ? Number(form.largeSaleThreshold) : null,
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
    setForm({ businessType: "", currency: "NGN", city: "", state: "", country: "", largeSaleThreshold: "" });
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
            <Input value={business?.name ?? ""} disabled className="bg-slate-50 text-slate-500" />
            <p className="text-xs text-slate-400 mt-1">Business name cannot be changed.</p>
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
              className="w-full h-9 px-2 rounded-md border border-slate-200 text-sm"
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
            <Label>Country</Label>
            <select
              className="w-full h-9 px-2 rounded-md border border-slate-200 text-sm bg-white"
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
          <div>
            <Label>Large Sale Alert Threshold</Label>
            <Input
              type="number"
              value={form.largeSaleThreshold}
              onChange={(e) => setForm({ ...form, largeSaleThreshold: e.target.value })}
              placeholder="100000"
            />
            <p className="text-xs text-slate-400 mt-1">Get a WhatsApp alert when a sale exceeds this amount.</p>
          </div>
          {error && <p className="text-xs text-red-500">{error}</p>}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={handleClose} disabled={saving}>Cancel</Button>
          <Button onClick={handleSave} disabled={saving}>{saving ? "Saving…" : "Save Changes"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

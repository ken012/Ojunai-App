"use client";

import { useEffect, useState, Suspense } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import Link from "next/link";
import { LogoMark } from "@/components/logo-mark";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { PasswordStrengthHint } from "@/components/password-strength-hint";
import { validatePassword } from "@/lib/password-policy";
import {
  inspectRecoveryToken,
  recoverAccountResetPassword,
  recoverAccountRequestPhoneOtp,
  recoverAccountChangePhone,
  type RecoveryTokenInfo,
} from "@/lib/auth";
import { KeyRound, Smartphone, AlertCircle } from "lucide-react";

type Stage =
  | { kind: "validating" }
  | { kind: "invalid"; message: string }
  | { kind: "choosing"; info: RecoveryTokenInfo }
  | { kind: "password"; info: RecoveryTokenInfo }
  | { kind: "phone-enter"; info: RecoveryTokenInfo }
  | { kind: "phone-verify"; info: RecoveryTokenInfo; newPhone: string };

function RecoverInner() {
  const router = useRouter();
  const params = useSearchParams();
  const token = params.get("token");
  const [stage, setStage] = useState<Stage>({ kind: "validating" });
  const [error, setError] = useState<string | null>(null);

  // Validate the token on mount — UI shows redacted account info so the user knows what they're recovering.
  useEffect(() => {
    if (!token) {
      setStage({ kind: "invalid", message: "This link is missing the recovery token." });
      return;
    }
    let cancelled = false;
    inspectRecoveryToken(token)
      .then((info) => { if (!cancelled) setStage({ kind: "choosing", info }); })
      .catch((err: unknown) => {
        if (cancelled) return;
        const ax = err as { response?: { data?: { errors?: string[] } } };
        setStage({
          kind: "invalid",
          message: ax.response?.data?.errors?.[0] ?? "This recovery link is invalid or has expired.",
        });
      });
    return () => { cancelled = true; };
  }, [token]);

  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-50 dark:bg-slate-950 px-4 py-8">
      <div className="w-full max-w-sm">
        <div className="text-center mb-6">
          <div className="flex justify-center">
            <LogoMark size="lg" className="text-cyan-700 dark:text-cyan-300" />
          </div>
        </div>

        <div className="bg-white dark:bg-slate-900 rounded-xl shadow-sm border border-slate-200 dark:border-slate-800 p-6">
          {stage.kind === "validating" && (
            <p className="text-sm text-slate-500 dark:text-slate-400 text-center">Validating link…</p>
          )}

          {stage.kind === "invalid" && <InvalidView message={stage.message} />}

          {stage.kind === "choosing" && (
            <ChoosingView
              info={stage.info}
              onPickPassword={() => { setError(null); setStage({ kind: "password", info: stage.info }); }}
              onPickPhone={() => { setError(null); setStage({ kind: "phone-enter", info: stage.info }); }}
            />
          )}

          {stage.kind === "password" && token && (
            <PasswordResetView
              token={token}
              info={stage.info}
              error={error}
              setError={setError}
              onCancel={() => { setError(null); setStage({ kind: "choosing", info: stage.info }); }}
              onSuccess={() => router.push("/")}
            />
          )}

          {stage.kind === "phone-enter" && token && (
            <PhoneEnterView
              token={token}
              info={stage.info}
              error={error}
              setError={setError}
              onCancel={() => { setError(null); setStage({ kind: "choosing", info: stage.info }); }}
              onSent={(newPhone) => { setError(null); setStage({ kind: "phone-verify", info: stage.info, newPhone }); }}
            />
          )}

          {stage.kind === "phone-verify" && token && (
            <PhoneVerifyView
              token={token}
              newPhone={stage.newPhone}
              error={error}
              setError={setError}
              onBack={() => { setError(null); setStage({ kind: "phone-enter", info: stage.info }); }}
              onSuccess={() => router.push("/")}
            />
          )}
        </div>
      </div>
    </div>
  );
}

export default function RecoverPage() {
  return <Suspense><RecoverInner /></Suspense>;
}

function InvalidView({ message }: { message: string }) {
  return (
    <div className="space-y-4 text-center">
      <div className="flex justify-center">
        <div className="rounded-full bg-red-100 dark:bg-red-900/40 p-3">
          <AlertCircle size={28} className="text-red-600 dark:text-red-400" />
        </div>
      </div>
      <h2 className="text-lg font-semibold text-slate-900 dark:text-slate-50">Recovery link invalid</h2>
      <p className="text-sm text-slate-500 dark:text-slate-400">{message}</p>
      <Link href="/recover-account" className="block">
        <Button variant="outline" className="w-full">Request a new link</Button>
      </Link>
    </div>
  );
}

function ChoosingView({ info, onPickPassword, onPickPhone }: {
  info: RecoveryTokenInfo;
  onPickPassword: () => void;
  onPickPhone: () => void;
}) {
  return (
    <div className="space-y-4">
      <div>
        <p className="text-xs uppercase tracking-wider text-slate-400 dark:text-slate-500">Recovering account for</p>
        <p className="text-lg font-semibold text-slate-900 dark:text-slate-50">{info.fullName}</p>
        <p className="text-sm text-slate-500 dark:text-slate-400">
          {info.businessName} · phone {info.maskedPhone}
        </p>
      </div>
      <p className="text-sm text-slate-600 dark:text-slate-400">What do you want to do?</p>
      <button
        onClick={onPickPassword}
        className="w-full text-left p-4 rounded-lg border border-slate-200 dark:border-slate-800 hover:border-cyan-400 hover:bg-cyan-50/50 dark:hover:bg-cyan-950/20 transition-colors"
      >
        <div className="flex items-start gap-3">
          <KeyRound size={20} className="text-cyan-600 mt-0.5 flex-shrink-0" />
          <div>
            <p className="font-medium text-sm text-slate-900 dark:text-slate-50">Reset password</p>
            <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
              I still have my phone — I just forgot the password.
            </p>
          </div>
        </div>
      </button>
      <button
        onClick={onPickPhone}
        className="w-full text-left p-4 rounded-lg border border-slate-200 dark:border-slate-800 hover:border-violet-400 hover:bg-violet-50/50 dark:hover:bg-violet-950/20 transition-colors"
      >
        <div className="flex items-start gap-3">
          <Smartphone size={20} className="text-violet-600 mt-0.5 flex-shrink-0" />
          <div>
            <p className="font-medium text-sm text-slate-900 dark:text-slate-50">Change phone number</p>
            <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
              I lost access to {info.maskedPhone} and need to add a new number.
            </p>
          </div>
        </div>
      </button>
    </div>
  );
}

function PasswordResetView({ token, info, error, setError, onCancel, onSuccess }: {
  token: string;
  info: RecoveryTokenInfo;
  error: string | null;
  setError: (e: string | null) => void;
  onCancel: () => void;
  onSuccess: () => void;
}) {
  const [pw, setPw] = useState("");
  const [confirm, setConfirm] = useState("");
  const [saving, setSaving] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (pw !== confirm) { setError("Passwords don't match."); return; }
    const v = validatePassword(pw);
    if (!v.ok) { setError(v.reason ?? "Password does not meet requirements."); return; }
    setSaving(true);
    setError(null);
    try {
      await recoverAccountResetPassword(token, pw);
      onSuccess();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Couldn't reset password. Please try again.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <p className="text-sm text-slate-600 dark:text-slate-400">
        Resetting password for <strong>{info.fullName}</strong>.
      </p>
      <div>
        <Label>New password</Label>
        <Input type="password" value={pw} onChange={(e) => setPw(e.target.value)} placeholder="Min 10 characters" autoFocus />
        <PasswordStrengthHint password={pw} />
      </div>
      <div>
        <Label>Confirm password</Label>
        <Input type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} />
      </div>
      {error && <p className="text-sm text-red-500">{error}</p>}
      <Button type="submit" className="w-full" disabled={saving || !pw || !confirm}>
        {saving ? "Resetting…" : "Reset password"}
      </Button>
      <button type="button" onClick={onCancel} className="text-xs text-slate-500 hover:text-slate-700 dark:hover:text-slate-300 w-full text-center">
        ← Back
      </button>
    </form>
  );
}

function PhoneEnterView({ token, info, error, setError, onCancel, onSent }: {
  token: string;
  info: RecoveryTokenInfo;
  error: string | null;
  setError: (e: string | null) => void;
  onCancel: () => void;
  onSent: (newPhone: string) => void;
}) {
  const [newPhone, setNewPhone] = useState("");
  const [sending, setSending] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!newPhone.trim()) return;
    setSending(true);
    setError(null);
    try {
      await recoverAccountRequestPhoneOtp(token, newPhone.trim());
      onSent(newPhone.trim());
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Couldn't send the verification code.");
    } finally {
      setSending(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <p className="text-sm text-slate-600 dark:text-slate-400">
        Replacing phone for <strong>{info.fullName}</strong>. Old number: <span className="font-mono">{info.maskedPhone}</span>.
      </p>
      <div>
        <Label>New phone number</Label>
        <Input
          value={newPhone}
          onChange={(e) => setNewPhone(e.target.value)}
          placeholder="+2348012345678"
          autoFocus
        />
        <p className="text-[11px] text-slate-400 dark:text-slate-500 mt-1">
          We&apos;ll send a 6-digit code via WhatsApp to this number to verify you have access to it.
        </p>
      </div>
      {error && <p className="text-sm text-red-500">{error}</p>}
      <Button type="submit" className="w-full" disabled={sending || !newPhone.trim()}>
        {sending ? "Sending code…" : "Send verification code"}
      </Button>
      <button type="button" onClick={onCancel} className="text-xs text-slate-500 hover:text-slate-700 dark:hover:text-slate-300 w-full text-center">
        ← Back
      </button>
    </form>
  );
}

function PhoneVerifyView({ token, newPhone, error, setError, onBack, onSuccess }: {
  token: string;
  newPhone: string;
  error: string | null;
  setError: (e: string | null) => void;
  onBack: () => void;
  onSuccess: () => void;
}) {
  const [code, setCode] = useState("");
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (code.length !== 6) return;
    setSubmitting(true);
    setError(null);
    try {
      await recoverAccountChangePhone(token, newPhone, code);
      onSuccess();
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Couldn't change the phone. Try again.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <p className="text-sm text-slate-600 dark:text-slate-400">
        We sent a 6-digit code on WhatsApp to <strong>{newPhone}</strong>. Enter it below to finish the recovery.
      </p>
      <div>
        <Label>Verification code</Label>
        <Input
          value={code}
          onChange={(e) => setCode(e.target.value.replace(/\D/g, "").slice(0, 6))}
          placeholder="123456"
          maxLength={6}
          inputMode="numeric"
          autoComplete="one-time-code"
          className="text-center text-2xl tracking-widest font-mono"
          autoFocus
        />
      </div>
      {error && <p className="text-sm text-red-500">{error}</p>}
      <Button type="submit" className="w-full" disabled={submitting || code.length !== 6}>
        {submitting ? "Confirming…" : "Confirm and change phone"}
      </Button>
      <button type="button" onClick={onBack} className="text-xs text-slate-500 hover:text-slate-700 dark:hover:text-slate-300 w-full text-center">
        ← Use a different number
      </button>
    </form>
  );
}

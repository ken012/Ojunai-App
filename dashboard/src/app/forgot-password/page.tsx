"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { LogoMark } from "@/components/logo-mark";
import { api } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { PasswordStrengthHint } from "@/components/password-strength-hint";
import { validatePassword } from "@/lib/password-policy";
import { normalizePhone } from "@/lib/phone";

type Step = "phone" | "code" | "done";

export default function ForgotPasswordPage() {
  const router = useRouter();
  const [step, setStep] = useState<Step>("phone");
  const [phone, setPhone] = useState("");
  const [code, setCode] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  async function handleRequestCode(e: React.FormEvent) {
    e.preventDefault();
    if (!phone.trim()) return;
    const normalized = normalizePhone(phone);
    if (!normalized) {
      setError("Enter a valid phone number (e.g. 08012345678 or +2348012345678).");
      return;
    }
    setPhone(normalized); // reflect normalized value back in the input
    setLoading(true);
    setError(null);
    try {
      await api.post("/auth/request-reset", { phoneNumber: normalized });
      setSuccess("Reset code sent to your WhatsApp. Check your messages.");
      setStep("code");
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to send reset code.");
    } finally {
      setLoading(false);
    }
  }

  async function handleVerifyAndReset(e: React.FormEvent) {
    e.preventDefault();
    if (newPassword !== confirm) {
      setError("Passwords don't match.");
      return;
    }
    const pwCheck = validatePassword(newPassword);
    if (!pwCheck.ok) {
      setError(pwCheck.reason ?? "Password does not meet requirements.");
      return;
    }
    setLoading(true);
    setError(null);
    try {
      await api.post("/auth/verify-reset", {
        phoneNumber: normalizePhone(phone) ?? phone,
        code,
        newPassword,
      });
      setStep("done");
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Invalid code or failed to reset.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-50 dark:bg-slate-950 px-4">
      <div className="w-full max-w-md">
        <div className="text-center mb-8">
          <div className="flex justify-center">
            <LogoMark size="lg" className="text-cyan-700 dark:text-cyan-300" />
          </div>
          <p className="text-slate-500 dark:text-slate-400 mt-4 text-sm">Reset your password</p>
        </div>

        <div className="bg-white dark:bg-slate-900 rounded-xl shadow-sm border border-slate-200 dark:border-slate-800 p-6">
          {step === "phone" && (
            <form onSubmit={handleRequestCode} className="space-y-4">
              <p className="text-sm text-slate-600 dark:text-slate-400">
                Enter your phone number and we will send a reset code to your WhatsApp.
              </p>
              <div>
                <Label>Phone Number</Label>
                <Input
                  value={phone}
                  onChange={(e) => setPhone(e.target.value)}
                  placeholder="+2348012345678"
                />
              </div>
              {error && <p className="text-sm text-red-500">{error}</p>}
              <Button type="submit" className="w-full" disabled={loading || !phone.trim()}>
                {loading ? "Sending..." : "Send Reset Code via WhatsApp"}
              </Button>
            </form>
          )}

          {step === "code" && (
            <form onSubmit={handleVerifyAndReset} className="space-y-4">
              {success && <p className="text-sm text-emerald-600 bg-emerald-50 p-3 rounded-lg">{success}</p>}
              <p className="text-sm text-slate-600 dark:text-slate-400">
                Enter the 6-digit code sent to your WhatsApp and choose a new password.
              </p>
              <div>
                <Label>Reset Code</Label>
                <Input
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                  placeholder="123456"
                  maxLength={6}
                  className="text-center text-2xl tracking-widest font-mono"
                />
              </div>
              <div>
                <Label>New Password</Label>
                <Input
                  type="password"
                  value={newPassword}
                  onChange={(e) => setNewPassword(e.target.value)}
                  placeholder="Min 10 characters"
                />
                <PasswordStrengthHint password={newPassword} />
              </div>
              <div>
                <Label>Confirm Password</Label>
                <Input
                  type="password"
                  value={confirm}
                  onChange={(e) => setConfirm(e.target.value)}
                />
              </div>
              {error && <p className="text-sm text-red-500">{error}</p>}
              <Button type="submit" className="w-full" disabled={loading || !code || !newPassword}>
                {loading ? "Resetting..." : "Reset Password"}
              </Button>
              <button
                type="button"
                onClick={() => { setStep("phone"); setError(null); setSuccess(null); }}
                className="text-xs text-cyan-600 hover:underline w-full text-center"
              >
                Use a different number
              </button>
            </form>
          )}

          {step === "done" && (
            <div className="text-center space-y-4">
              <p className="text-lg font-semibold text-emerald-600">Password reset successfully!</p>
              <p className="text-sm text-slate-500 dark:text-slate-400">You can now log in with your new password.</p>
              <Button onClick={() => router.push("/login")} className="w-full">
                Go to Login
              </Button>
            </div>
          )}
        </div>

        <p className="text-center text-sm text-slate-500 dark:text-slate-400 mt-4">
          Remember your password?{" "}
          <Link href="/login" className="text-cyan-600 font-medium hover:underline">
            Sign in
          </Link>
        </p>

        <p className="text-center text-xs text-slate-400 dark:text-slate-500 mt-2">
          Lost access to your phone?{" "}
          <Link href="/recover-account" className="text-cyan-600 font-medium hover:underline">
            Recover via email
          </Link>
        </p>
      </div>
    </div>
  );
}

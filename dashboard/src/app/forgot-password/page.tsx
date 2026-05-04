"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import Image from "next/image";
import { api } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

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
    setLoading(true);
    setError(null);
    try {
      await api.post("/auth/request-reset", { phoneNumber: phone });
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
    if (newPassword.length < 8) {
      setError("Password must be at least 8 characters.");
      return;
    }
    setLoading(true);
    setError(null);
    try {
      await api.post("/auth/verify-reset", {
        phoneNumber: phone,
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
    <div className="min-h-screen flex items-center justify-center bg-slate-50 px-4">
      <div className="w-full max-w-md">
        <div className="text-center mb-8">
          <Image
            src="/logo.jpg"
            alt="Ojunai"
            width={1540}
            height={540}
            priority
            className="h-12 w-auto mx-auto"
          />
          <p className="text-slate-500 mt-3 text-sm">Reset your password</p>
        </div>

        <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-6">
          {step === "phone" && (
            <form onSubmit={handleRequestCode} className="space-y-4">
              <p className="text-sm text-slate-600">
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
              <p className="text-sm text-slate-600">
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
                  placeholder="Min 8 characters"
                />
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
              <p className="text-sm text-slate-500">You can now log in with your new password.</p>
              <Button onClick={() => router.push("/login")} className="w-full">
                Go to Login
              </Button>
            </div>
          )}
        </div>

        <p className="text-center text-sm text-slate-500 mt-4">
          Remember your password?{" "}
          <Link href="/login" className="text-cyan-600 font-medium hover:underline">
            Sign in
          </Link>
        </p>
      </div>
    </div>
  );
}

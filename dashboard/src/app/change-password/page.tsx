"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { LogoMark } from "@/components/logo-mark";
import { api } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { PasswordInput } from "@/components/ui/password-input";
import { Label } from "@/components/ui/label";
import { PasswordStrengthHint } from "@/components/password-strength-hint";
import { validatePassword } from "@/lib/password-policy";

export default function ChangePasswordPage() {
  const router = useRouter();
  const [form, setForm] = useState({ currentPassword: "", newPassword: "", confirm: "" });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (form.newPassword !== form.confirm) {
      setError("Passwords don't match.");
      return;
    }
    const pwCheck = validatePassword(form.newPassword);
    if (!pwCheck.ok) {
      setError(pwCheck.reason ?? "Password does not meet requirements.");
      return;
    }
    setSaving(true);
    setError(null);
    try {
      await api.post("/auth/change-password", {
        currentPassword: form.currentPassword,
        newPassword: form.newPassword,
      });
      // Update localStorage to clear mustChangePassword
      if (typeof window !== "undefined") {
        const stored = localStorage.getItem("oj_auth");
        if (stored) {
          const parsed = JSON.parse(stored);
          parsed.mustChangePassword = false;
          localStorage.setItem("oj_auth", JSON.stringify(parsed));
        }
      }
      router.push("/");
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Failed to change password.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-50 dark:bg-slate-950 px-4">
      <div className="w-full max-w-md">
        <div className="text-center mb-8">
          <div className="flex justify-center">
            <LogoMark size="lg" className="text-cyan-700 dark:text-cyan-300" />
          </div>
          <p className="text-slate-500 dark:text-slate-400 mt-4 text-sm">Set your new password</p>
          <p className="text-xs text-slate-400 dark:text-slate-500 mt-1">You must change your password before continuing.</p>
        </div>

        <div className="bg-white dark:bg-slate-900 rounded-xl shadow-sm border border-slate-200 dark:border-slate-800 p-6">
          <form onSubmit={handleSubmit} className="space-y-4">
            <div>
              <Label>Current Password (temporary)</Label>
              <PasswordInput
                value={form.currentPassword}
                onChange={(e) => setForm({ ...form, currentPassword: e.target.value })}
                placeholder="Enter the password you were given"
              />
            </div>
            <div>
              <Label>New Password</Label>
              <PasswordInput
                value={form.newPassword}
                onChange={(e) => setForm({ ...form, newPassword: e.target.value })}
                placeholder="Min 10 characters"
              />
              <PasswordStrengthHint password={form.newPassword} />
            </div>
            <div>
              <Label>Confirm New Password</Label>
              <PasswordInput
                value={form.confirm}
                onChange={(e) => setForm({ ...form, confirm: e.target.value })}
              />
            </div>
            {error && (
              <div className="bg-red-50 border border-red-200 rounded-lg p-3 text-sm text-red-600">
                {error}
              </div>
            )}
            <Button type="submit" className="w-full" disabled={saving}>
              {saving ? "Saving..." : "Set New Password"}
            </Button>
          </form>
        </div>
      </div>
    </div>
  );
}

"use client";

import { useState } from "react";
import Link from "next/link";
import { LogoMark } from "@/components/logo-mark";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { requestAccountRecovery } from "@/lib/auth";

/**
 * Phone-loss recovery — entry page. User types their email, server emails a recovery link.
 * The response is intentionally always success — we never reveal whether the email matched.
 */
export default function RecoverAccountPage() {
  const [email, setEmail] = useState("");
  const [submitted, setSubmitted] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!email.trim()) return;
    setLoading(true);
    setError(null);
    try {
      await requestAccountRecovery(email.trim());
      setSubmitted(true);
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Couldn't process the request. Try again in a moment.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-50 dark:bg-slate-950 px-4">
      <div className="w-full max-w-sm">
        <div className="text-center mb-6">
          <div className="flex justify-center">
            <LogoMark size="lg" className="text-cyan-700 dark:text-cyan-300" />
          </div>
          <p className="text-slate-500 dark:text-slate-400 mt-4 text-sm">Recover your account</p>
        </div>

        <div className="bg-white dark:bg-slate-900 rounded-xl shadow-sm border border-slate-200 dark:border-slate-800 p-6">
          {!submitted ? (
            <form onSubmit={handleSubmit} className="space-y-4">
              <p className="text-sm text-slate-600 dark:text-slate-400">
                Lost access to your phone? Enter the email on your Ojunai account and we&apos;ll send a recovery link.
              </p>
              <p className="text-xs text-slate-400 dark:text-slate-500">
                Recovery only works for emails you&apos;ve previously verified.
              </p>
              <div>
                <Label>Email</Label>
                <Input
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="email@example.com"
                  autoFocus
                />
              </div>
              {error && <p className="text-sm text-red-500">{error}</p>}
              <Button type="submit" className="w-full" disabled={loading || !email.trim()}>
                {loading ? "Sending…" : "Send recovery email"}
              </Button>
            </form>
          ) : (
            <div className="space-y-3 text-center">
              <p className="text-sm font-medium text-emerald-700 dark:text-emerald-300">Check your inbox</p>
              <p className="text-sm text-slate-500 dark:text-slate-400">
                If an account is registered to <strong>{email}</strong> with a verified email, we&apos;ve sent a recovery link. It expires in 30 minutes.
              </p>
              <p className="text-xs text-slate-400 dark:text-slate-500">
                Don&apos;t see it? Check spam, or wait a few minutes and request again.
              </p>
            </div>
          )}
        </div>

        <p className="text-center text-sm text-slate-500 dark:text-slate-400 mt-4">
          Remembered your phone?{" "}
          <Link href="/login" className="text-cyan-600 font-medium hover:underline">Sign in</Link>
        </p>
      </div>
    </div>
  );
}

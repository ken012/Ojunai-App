"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { AlertCircle, X } from "lucide-react";
import { requestEmailVerification } from "@/lib/auth";
import { useUser } from "@/lib/data-sync";

/**
 * Persistent banner on every dashboard page when the current user has an email on file
 * but hasn't verified it. Email is the *recovery channel* for phone-loss scenarios, so
 * unverified-email users would have no recovery path — we nag them until it's done.
 */
export function EmailVerificationBanner() {
  const user = useUser();
  const router = useRouter();
  const [sending, setSending] = useState(false);
  const [sentAt, setSentAt] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [dismissed, setDismissed] = useState(false);

  if (!user || !user.email || user.emailVerified || dismissed) return null;

  async function handleResend() {
    setSending(true);
    setError(null);
    try {
      await requestEmailVerification();
      setSentAt(Date.now());
    } catch (err: unknown) {
      const ax = err as { response?: { data?: { errors?: string[] } } };
      setError(ax.response?.data?.errors?.[0] ?? "Couldn't send the verification email.");
    } finally {
      setSending(false);
    }
  }

  return (
    <div className="bg-amber-50 dark:bg-amber-950/30 border-b border-amber-200 dark:border-amber-900 px-4 py-2.5">
      <div className="max-w-screen-2xl mx-auto flex items-center justify-between gap-3 flex-wrap">
        <div className="flex items-start gap-2 text-sm text-amber-800 dark:text-amber-200">
          <AlertCircle size={16} className="flex-shrink-0 mt-0.5" />
          <div>
            <strong>Verify your email</strong> ({user.email}) — required for account recovery if you lose access to your phone.
            {sentAt && Date.now() - sentAt < 60_000 && (
              <span className="ml-2 text-emerald-700 dark:text-emerald-300">Email sent, check your inbox.</span>
            )}
            {error && <span className="ml-2 text-red-600 dark:text-red-400">{error}</span>}
          </div>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={handleResend}
            disabled={sending}
            className="text-xs font-medium px-3 py-1 rounded-md bg-amber-600 text-white hover:bg-amber-700 disabled:opacity-60"
          >
            {sending ? "Sending…" : sentAt ? "Resend" : "Send verification email"}
          </button>
          <button
            onClick={() => router.push("/settings")}
            className="text-xs font-medium text-amber-700 dark:text-amber-300 hover:underline"
          >
            Manage in settings
          </button>
          <button
            onClick={() => setDismissed(true)}
            aria-label="Dismiss for this session"
            className="text-amber-700 dark:text-amber-300 hover:text-amber-900 dark:hover:text-amber-100 p-1"
          >
            <X size={14} />
          </button>
        </div>
      </div>
    </div>
  );
}

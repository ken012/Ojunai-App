"use client";

import { useEffect, useState, Suspense } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import Link from "next/link";
import { LogoMark } from "@/components/logo-mark";
import { Button } from "@/components/ui/button";
import { Check, AlertCircle } from "lucide-react";
import { verifyEmail } from "@/lib/auth";
import { isAuthenticated } from "@/lib/auth";

function VerifyEmailInner() {
  const router = useRouter();
  const params = useSearchParams();
  const token = params.get("token");
  const [status, setStatus] = useState<"working" | "ok" | "error">("working");
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    if (!token) {
      setStatus("error");
      setMessage("This link is missing the verification token.");
      return;
    }
    let cancelled = false;
    verifyEmail(token)
      .then(() => {
        if (cancelled) return;
        setStatus("ok");
        setMessage("Your email is verified. You can now use it to recover your account if you ever lose your phone.");
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const ax = err as { response?: { data?: { errors?: string[] } } };
        setStatus("error");
        setMessage(ax.response?.data?.errors?.[0] ?? "This link is invalid or has expired.");
      });
    return () => { cancelled = true; };
  }, [token]);

  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-50 dark:bg-slate-950 px-4">
      <div className="w-full max-w-sm">
        <div className="text-center mb-6">
          <div className="flex justify-center">
            <LogoMark size="lg" className="text-cyan-700 dark:text-cyan-300" />
          </div>
        </div>

        <div className="bg-white dark:bg-slate-900 rounded-xl shadow-sm border border-slate-200 dark:border-slate-800 p-6 text-center space-y-4">
          {status === "working" && (
            <p className="text-sm text-slate-500 dark:text-slate-400">Verifying your email…</p>
          )}

          {status === "ok" && (
            <>
              <div className="flex justify-center">
                <div className="rounded-full bg-emerald-100 dark:bg-emerald-900/40 p-3">
                  <Check size={28} className="text-emerald-600 dark:text-emerald-400" />
                </div>
              </div>
              <h2 className="text-lg font-semibold text-slate-900 dark:text-slate-50">Email verified</h2>
              <p className="text-sm text-slate-500 dark:text-slate-400">{message}</p>
              <Button
                onClick={() => router.push(isAuthenticated() ? "/" : "/login")}
                className="w-full"
              >
                {isAuthenticated() ? "Go to dashboard" : "Go to login"}
              </Button>
            </>
          )}

          {status === "error" && (
            <>
              <div className="flex justify-center">
                <div className="rounded-full bg-red-100 dark:bg-red-900/40 p-3">
                  <AlertCircle size={28} className="text-red-600 dark:text-red-400" />
                </div>
              </div>
              <h2 className="text-lg font-semibold text-slate-900 dark:text-slate-50">Verification failed</h2>
              <p className="text-sm text-slate-500 dark:text-slate-400">{message}</p>
              <p className="text-xs text-slate-400 dark:text-slate-500">
                If you have an account, sign in and resend the verification from the dashboard banner.
              </p>
              <Link href="/login" className="block">
                <Button variant="outline" className="w-full">Go to login</Button>
              </Link>
            </>
          )}
        </div>
      </div>
    </div>
  );
}

export default function VerifyEmailPage() {
  return (
    <Suspense>
      <VerifyEmailInner />
    </Suspense>
  );
}

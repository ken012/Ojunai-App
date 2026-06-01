"use client";

import { Suspense, useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { LogoMark } from "@/components/logo-mark";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { PasswordStrengthHint } from "@/components/password-strength-hint";
import { validatePassword } from "@/lib/password-policy";
import { api } from "@/lib/api";

/**
 * /post-signup — landing page for the magic link issued at the end of the Telegram signup
 * flow. Reads the post-signup JWT from the URL, asks the user to set a password, calls
 * /api/auth/post-signup to finalize, then redirects to the dashboard.
 *
 * Distinct from /register and /change-password. Doesn't replace either — it's a new route
 * that only exists for channel-native signups.
 */
function PostSignupContent() {
  const router = useRouter();
  const params = useSearchParams();
  const token = params.get("token") ?? "";

  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!token) setError("Missing signup token. Open the link from the Telegram message again.");
  }, [token]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (password !== confirm) {
      setError("Passwords don't match.");
      return;
    }
    const check = validatePassword(password);
    if (!check.ok) {
      setError(check.reason ?? "Password does not meet requirements.");
      return;
    }

    setSubmitting(true);
    try {
      const { data } = await api.post<{ data: { token: string; expiresAt: string; user: unknown; business: unknown } }>(
        "/auth/post-signup",
        { postSignupToken: token, password },
      );
      const auth = data.data;
      if (auth?.user && auth?.business) {
        // Mirror the localStorage shape login() writes so the dashboard treats the user as
        // logged in. The auth cookie is set server-side; this only persists the cached
        // user/business so the UI can render before the next fetch.
        localStorage.setItem("oj_user", JSON.stringify(auth.user));
        localStorage.setItem("oj_business", JSON.stringify(auth.business));
        localStorage.setItem("oj_auth_time", Date.now().toString());
      }
      router.push("/");
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { errors?: string[] } } };
      setError(axiosErr.response?.data?.errors?.[0] ?? "Couldn't finish signup. Try again.");
      setSubmitting(false);
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-50 dark:bg-slate-950 px-4">
      <div className="w-full max-w-md">
        <div className="text-center mb-8">
          <div className="flex justify-center">
            <LogoMark size="lg" className="text-cyan-700 dark:text-cyan-300" />
          </div>
          <p className="text-slate-500 dark:text-slate-400 mt-4 text-sm">Set your password</p>
        </div>

        <div className="bg-white dark:bg-slate-900 rounded-xl shadow-sm border border-slate-200 dark:border-slate-800 p-6">
          <form onSubmit={handleSubmit} className="space-y-4">
            <p className="text-sm text-slate-600 dark:text-slate-400">
              Your phone is verified via Telegram. Choose a password to finish signing up.
            </p>
            <div className="space-y-1">
              <Label>Password</Label>
              <Input
                type="password"
                placeholder="Min 10 characters"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoFocus
              />
              <PasswordStrengthHint password={password} />
            </div>
            <div className="space-y-1">
              <Label>Confirm password</Label>
              <Input
                type="password"
                placeholder="Re-enter password"
                value={confirm}
                onChange={(e) => setConfirm(e.target.value)}
              />
            </div>
            {error && (
              <div className="bg-red-50 border border-red-200 rounded-lg p-3 text-sm text-red-600">
                {error}
              </div>
            )}
            <Button type="submit" className="w-full" disabled={submitting || !token}>
              {submitting ? "Finishing…" : "Set password & go to dashboard"}
            </Button>
          </form>
        </div>
      </div>
    </div>
  );
}

export default function PostSignupPage() {
  return (
    <Suspense fallback={null}>
      <PostSignupContent />
    </Suspense>
  );
}

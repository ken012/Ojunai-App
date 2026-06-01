"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { LogoMark } from "@/components/logo-mark";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { requestPhoneVerification, verifyPhoneAndRegister } from "@/lib/auth";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { PasswordStrengthHint } from "@/components/password-strength-hint";
import { validatePassword } from "@/lib/password-policy";
import { api } from "@/lib/api";

const schema = z.object({
  fullName: z.string().min(2, "Full name required"),
  phoneNumber: z.string().min(10, "Valid phone number required"),
  email: z.string().email().optional().or(z.literal("")),
  password: z.string().superRefine((v, ctx) => {
    const r = validatePassword(v);
    if (!r.ok) ctx.addIssue({ code: z.ZodIssueCode.custom, message: r.reason ?? "Invalid password" });
  }),
  businessName: z.string().min(2, "Business name required"),
  businessType: z.string().optional(),
  state: z.string().optional(),
  city: z.string().optional(),
  dateOfBirth: z.string().optional(),
});
type FormData = z.infer<typeof schema>;

type Step = "details" | "verify";

export default function RegisterPage() {
  const router = useRouter();
  const [error, setError] = useState<string | null>(null);
  const [step, setStep] = useState<Step>("details");
  const [code, setCode] = useState("");
  const [verifying, setVerifying] = useState(false);
  const [resendCooldown, setResendCooldown] = useState(0);
  const [telegramStarting, setTelegramStarting] = useState(false);
  const [messengerStarting, setMessengerStarting] = useState(false);

  const {
    register,
    handleSubmit,
    watch,
    getValues,
    formState: { errors, isSubmitting },
  } = useForm<FormData>({
    resolver: zodResolver(schema),
    defaultValues: {
      fullName: "",
      phoneNumber: "",
      email: "",
      password: "",
      businessName: "",
      businessType: "",
      state: "",
      city: "",
      dateOfBirth: "",
    },
  });
  const passwordValue = watch("password");

  // Resend countdown — ticks every second once a verification code has been issued.
  useEffect(() => {
    if (resendCooldown <= 0) return;
    const t = setTimeout(() => setResendCooldown((s) => s - 1), 1000);
    return () => clearTimeout(t);
  }, [resendCooldown]);

  async function onSubmitDetails(data: FormData) {
    setError(null);
    try {
      const r = await requestPhoneVerification(data.phoneNumber);
      setResendCooldown(r.resendCooldownSeconds);
      setStep("verify");
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { errors?: string[] } } };
      setError(axiosErr.response?.data?.errors?.[0] ?? "Couldn't send verification code. Please try again.");
    }
  }

  async function handleResend() {
    if (resendCooldown > 0) return;
    setError(null);
    try {
      const r = await requestPhoneVerification(getValues("phoneNumber"));
      setResendCooldown(r.resendCooldownSeconds);
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { errors?: string[] } } };
      setError(axiosErr.response?.data?.errors?.[0] ?? "Couldn't resend code.");
    }
  }

  async function handleSignupViaTelegram() {
    setError(null);
    setTelegramStarting(true);
    try {
      const { data } = await api.post<{ data: { deepLink: string; botUsername: string } }>(
        "/auth/signup-via-telegram/start",
        {},
      );
      const deepLink = data.data?.deepLink;
      if (!deepLink) {
        setError("Couldn't start Telegram signup. Try the phone-OTP flow above.");
        setTelegramStarting(false);
        return;
      }
      // Open in a new tab so the user can come back here if Telegram isn't installed.
      window.open(deepLink, "_blank", "noopener,noreferrer");
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { errors?: string[] }; status?: number } };
      if (axiosErr.response?.status === 503) {
        setError("Telegram signup isn't enabled on this server yet. Use the phone-OTP flow above for now.");
      } else {
        setError(axiosErr.response?.data?.errors?.[0] ?? "Couldn't start Telegram signup.");
      }
    } finally {
      setTelegramStarting(false);
    }
  }

  async function handleSignupViaMessenger() {
    setError(null);
    setMessengerStarting(true);
    try {
      const { data } = await api.post<{ data: { deepLink: string; pageUsername: string } }>(
        "/auth/signup-via-messenger/start",
        {},
      );
      const deepLink = data.data?.deepLink;
      if (!deepLink) {
        setError("Couldn't start Messenger signup. Try the phone-OTP flow above.");
        setMessengerStarting(false);
        return;
      }
      window.open(deepLink, "_blank", "noopener,noreferrer");
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { errors?: string[] }; status?: number } };
      if (axiosErr.response?.status === 503) {
        setError("Messenger signup isn't enabled on this server yet. Use the phone-OTP flow above for now.");
      } else {
        setError(axiosErr.response?.data?.errors?.[0] ?? "Couldn't start Messenger signup.");
      }
    } finally {
      setMessengerStarting(false);
    }
  }

  async function handleVerify(e: React.FormEvent) {
    e.preventDefault();
    if (code.length !== 6) return;
    setVerifying(true);
    setError(null);
    try {
      const data = getValues();
      await verifyPhoneAndRegister({
        ...data,
        email: data.email || undefined,
        dateOfBirth: data.dateOfBirth ? `${data.dateOfBirth}-01-01` : undefined,
        code,
      });
      router.push("/");
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { errors?: string[] } } };
      setError(axiosErr.response?.data?.errors?.[0] ?? "Verification failed.");
    } finally {
      setVerifying(false);
    }
  }

  const currentPhone = getValues("phoneNumber");

  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-50 dark:bg-slate-950 px-4 py-8">
      <div className="w-full max-w-md">
        <div className="text-center mb-8">
          <div className="flex justify-center">
            <LogoMark size="lg" className="text-cyan-700 dark:text-cyan-300" />
          </div>
          <p className="text-slate-500 dark:text-slate-400 mt-4 text-sm">
            {step === "details" ? "Create your business account" : "Verify your phone number"}
          </p>
        </div>

        <div className="bg-white dark:bg-slate-900 rounded-xl shadow-sm border border-slate-200 dark:border-slate-800 p-6">
          {step === "details" && (
            <form onSubmit={handleSubmit(onSubmitDetails)} className="space-y-4">
              <div className="grid grid-cols-2 gap-3">
                <div className="col-span-2 space-y-1">
                  <Label>Full Name</Label>
                  <Input placeholder="Your name" {...register("fullName")} />
                  {errors.fullName && <p className="text-xs text-red-500">{errors.fullName.message}</p>}
                </div>

                <div className="space-y-1">
                  <Label>Phone Number</Label>
                  <Input placeholder="+2348012345678" {...register("phoneNumber")} />
                  {errors.phoneNumber && <p className="text-xs text-red-500">{errors.phoneNumber.message}</p>}
                </div>

                <div className="space-y-1">
                  <Label>Email (optional)</Label>
                  <Input type="email" placeholder="email@example.com" {...register("email")} />
                  <p className="text-[11px] text-slate-400 dark:text-slate-500">
                    Used to recover your account if you lose access to your phone. We&apos;ll send a verification link.
                  </p>
                </div>

                <div className="col-span-2 space-y-1">
                  <Label>Password</Label>
                  <Input type="password" placeholder="Min 10 characters" {...register("password")} />
                  {errors.password && <p className="text-xs text-red-500">{errors.password.message}</p>}
                  <PasswordStrengthHint password={passwordValue ?? ""} />
                </div>

                <div className="col-span-2 space-y-1">
                  <Label>Business Name</Label>
                  <Input placeholder="e.g. Mama Titi Store" {...register("businessName")} />
                  {errors.businessName && <p className="text-xs text-red-500">{errors.businessName.message}</p>}
                </div>

                <div className="space-y-1">
                  <Label>Business Type</Label>
                  <Input placeholder="e.g. Retail, Food" {...register("businessType")} />
                </div>

                <div className="space-y-1">
                  <Label>State</Label>
                  <Input placeholder="e.g. Lagos" {...register("state")} />
                </div>

                <div className="col-span-2 space-y-1">
                  <Label>Birth Year</Label>
                  <Input type="number" min={1920} max={new Date().getFullYear() - 13} placeholder="e.g. 1990" {...register("dateOfBirth")} />
                  <p className="text-[11px] text-slate-400 dark:text-slate-500">Used to secure your report downloads</p>
                </div>
              </div>

              {error && (
                <div className="bg-red-50 border border-red-200 rounded-lg p-3 text-sm text-red-600">
                  {error}
                </div>
              )}

              <Button type="submit" className="w-full" disabled={isSubmitting}>
                {isSubmitting ? "Sending code…" : "Send verification code"}
              </Button>

              <p className="text-xs text-slate-400 dark:text-slate-500 text-center mt-3">
                By creating an account, you agree to our{" "}
                <Link href="/terms" className="underline hover:text-slate-600">Terms of Service</Link> and{" "}
                <Link href="/privacy" className="underline hover:text-slate-600">Privacy Policy</Link>.
              </p>
            </form>
          )}

          {step === "details" && (
            <div className="mt-6 pt-6 border-t border-slate-200 dark:border-slate-800 space-y-2">
              <p className="text-xs text-center text-slate-500 dark:text-slate-400 mb-1">Or</p>
              <Button
                type="button"
                variant="outline"
                className="w-full"
                onClick={handleSignupViaTelegram}
                disabled={telegramStarting || messengerStarting}
              >
                {telegramStarting ? "Opening Telegram…" : "Sign up via Telegram"}
              </Button>
              <Button
                type="button"
                variant="outline"
                className="w-full"
                onClick={handleSignupViaMessenger}
                disabled={telegramStarting || messengerStarting}
              >
                {messengerStarting ? "Opening Messenger…" : "Sign up via Messenger"}
              </Button>
              <p className="text-[11px] text-slate-400 dark:text-slate-500 text-center mt-2">
                Verifies your phone through the chat app. You&apos;ll set a password after.
              </p>
            </div>
          )}

          {step === "verify" && (
            <form onSubmit={handleVerify} className="space-y-4">
              <p className="text-sm text-slate-600 dark:text-slate-400">
                We sent a 6-digit code to <strong>{currentPhone}</strong> on WhatsApp. Enter it below to finish creating your account.
              </p>
              <div>
                <Label>Verification Code</Label>
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

              {error && (
                <div className="bg-red-50 border border-red-200 rounded-lg p-3 text-sm text-red-600">
                  {error}
                </div>
              )}

              <Button type="submit" className="w-full" disabled={verifying || code.length !== 6}>
                {verifying ? "Verifying…" : "Verify and Create Account"}
              </Button>

              <div className="flex items-center justify-between text-xs">
                <button
                  type="button"
                  onClick={() => { setStep("details"); setCode(""); setError(null); }}
                  className="text-slate-500 hover:text-slate-700 dark:hover:text-slate-300"
                >
                  ← Back to edit details
                </button>
                <button
                  type="button"
                  onClick={handleResend}
                  disabled={resendCooldown > 0}
                  className="text-cyan-600 font-medium hover:underline disabled:text-slate-400 disabled:no-underline"
                >
                  {resendCooldown > 0 ? `Resend in ${resendCooldown}s` : "Resend code"}
                </button>
              </div>
            </form>
          )}
        </div>

        <p className="text-center text-sm text-slate-500 dark:text-slate-400 mt-4">
          Already have an account?{" "}
          <Link href="/login" className="text-cyan-600 font-medium hover:underline">
            Sign in
          </Link>
        </p>
      </div>
    </div>
  );
}

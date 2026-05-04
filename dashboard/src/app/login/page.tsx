"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import Image from "next/image";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { login } from "@/lib/auth";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

const schema = z.object({
  phoneOrEmail: z.string().min(1, "Phone or email is required"),
  password: z.string().min(1, "Password is required"),
});
type FormData = z.infer<typeof schema>;

export default function LoginPage() {
  const router = useRouter();
  const [error, setError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormData>({
    resolver: zodResolver(schema),
    defaultValues: { phoneOrEmail: "", password: "" },
  });

  async function onSubmit(data: FormData) {
    setError(null);
    try {
      const result = await login(data.phoneOrEmail, data.password);
      if (result.mustChangePassword) {
        router.push("/change-password");
      } else {
        router.push("/");
      }
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { errors?: string[] } } };
      setError(axiosErr.response?.data?.errors?.[0] ?? "Login failed. Check your credentials.");
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-50 px-4">
      <div className="w-full max-w-sm">
        <div className="text-center mb-8">
          <Image
            src="/logo.jpg"
            alt="Ojunai"
            width={1540}
            height={540}
            priority
            className="h-12 w-auto mx-auto"
          />
          <p className="text-slate-500 mt-3 text-sm">The eye that never blinks.</p>
        </div>

        <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-6">
          <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
            <div className="space-y-1">
              <Label htmlFor="phoneOrEmail">Phone or Email</Label>
              <Input
                id="phoneOrEmail"
                placeholder="+2348012345678 or email"
                {...register("phoneOrEmail")}
              />
              {errors.phoneOrEmail && (
                <p className="text-xs text-red-500">{errors.phoneOrEmail.message}</p>
              )}
            </div>

            <div className="space-y-1">
              <Label htmlFor="password">Password</Label>
              <Input
                id="password"
                type="password"
                placeholder="••••••••"
                {...register("password")}
              />
              {errors.password && (
                <p className="text-xs text-red-500">{errors.password.message}</p>
              )}
            </div>

            {error && (
              <div className="bg-red-50 border border-red-200 rounded-lg p-3 text-sm text-red-600">
                {error}
              </div>
            )}

            <Button type="submit" className="w-full" disabled={isSubmitting}>
              {isSubmitting ? "Signing in…" : "Sign In"}
            </Button>

            <p className="text-xs text-slate-400 text-center mt-3">
              By signing in, you agree to our{" "}
              <Link href="/terms" className="underline hover:text-slate-600">Terms of Service</Link> and{" "}
              <Link href="/privacy" className="underline hover:text-slate-600">Privacy Policy</Link>.
            </p>
          </form>
        </div>

        <div className="text-center mt-4 space-y-2">
          <p className="text-sm text-slate-500">
            <Link href="/forgot-password" className="text-cyan-600 font-medium hover:underline">
              Forgot password?
            </Link>
          </p>
          <p className="text-sm text-slate-500">
            New business?{" "}
            <Link href="/register" className="text-cyan-600 font-medium hover:underline">
              Register here
            </Link>
          </p>
        </div>
      </div>
    </div>
  );
}

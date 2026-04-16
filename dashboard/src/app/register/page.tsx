"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { register as registerUser } from "@/lib/auth";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

const schema = z.object({
  fullName: z.string().min(2, "Full name required"),
  phoneNumber: z.string().min(10, "Valid phone number required"),
  email: z.string().email().optional().or(z.literal("")),
  password: z.string().min(8, "Password must be at least 8 characters"),
  businessName: z.string().min(2, "Business name required"),
  businessType: z.string().optional(),
  state: z.string().optional(),
  city: z.string().optional(),
});
type FormData = z.infer<typeof schema>;

export default function RegisterPage() {
  const router = useRouter();
  const [error, setError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
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
    },
  });

  async function onSubmit(data: FormData) {
    setError(null);
    try {
      await registerUser({
        ...data,
        email: data.email || undefined,
      });
      router.push("/");
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { errors?: string[] } } };
      setError(axiosErr.response?.data?.errors?.[0] ?? "Registration failed. Please try again.");
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-50 px-4 py-8">
      <div className="w-full max-w-md">
        <div className="text-center mb-8">
          <h1 className="text-3xl font-black text-slate-900">
            Biz<span className="text-sky-500">Pilot</span>
          </h1>
          <p className="text-slate-500 mt-1 text-sm">Create your business account</p>
        </div>

        <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-6">
          <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
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
              </div>

              <div className="col-span-2 space-y-1">
                <Label>Password</Label>
                <Input type="password" placeholder="Min 8 characters" {...register("password")} />
                {errors.password && <p className="text-xs text-red-500">{errors.password.message}</p>}
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
            </div>

            {error && (
              <div className="bg-red-50 border border-red-200 rounded-lg p-3 text-sm text-red-600">
                {error}
              </div>
            )}

            <Button type="submit" className="w-full" disabled={isSubmitting}>
              {isSubmitting ? "Creating account…" : "Create Account"}
            </Button>
          </form>
        </div>

        <p className="text-center text-sm text-slate-500 mt-4">
          Already have an account?{" "}
          <Link href="/login" className="text-sky-600 font-medium hover:underline">
            Sign in
          </Link>
        </p>
      </div>
    </div>
  );
}

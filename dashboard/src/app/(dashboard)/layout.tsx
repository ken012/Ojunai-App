"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { isAuthenticated } from "@/lib/auth";
import { DataSyncProvider } from "@/lib/data-sync";
import { Sidebar } from "@/components/sidebar";
import { TrialBanner } from "@/components/trial-banner";

export default function DashboardLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const router = useRouter();

  useEffect(() => {
    if (!isAuthenticated()) {
      router.push("/login");
    }
  }, [router]);

  return (
    <DataSyncProvider>
      <div className="flex min-h-screen bg-slate-50">
        <Sidebar />
        <main className="flex-1 overflow-auto w-full relative">
          {/* Subtle ambient brand gradient — not visible, just adds warmth */}
          <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(ellipse_900px_500px_at_top,rgba(6,182,212,0.04),transparent),radial-gradient(ellipse_600px_400px_at_bottom_right,rgba(139,92,246,0.04),transparent)]" />
          <TrialBanner />
          <div className="h-12 lg:hidden" />
          <div className="relative p-4 sm:p-6 lg:p-8 max-w-7xl mx-auto">{children}</div>
        </main>
      </div>
    </DataSyncProvider>
  );
}

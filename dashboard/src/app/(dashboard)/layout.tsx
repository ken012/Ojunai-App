"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { isAuthenticated } from "@/lib/auth";
import { DataSyncProvider } from "@/lib/data-sync";
import { Sidebar } from "@/components/sidebar";
import { TrialBanner } from "@/components/trial-banner";
import { InstallBanner } from "@/components/install-banner";
import { EmailVerificationBanner } from "@/components/email-verification-banner";
import { DashboardBackground } from "@/components/dashboard-background";
import { CapHitDialog } from "@/components/cap-hit-dialog";

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
      <div className="flex min-h-screen bg-slate-50 dark:bg-slate-950">
        <Sidebar />
        {/* `relative isolate` creates a stacking context so the negative-z-index
            background layers stay scoped to <main> and don't escape behind the sidebar. */}
        <main className="flex-1 overflow-auto w-full relative isolate">
          <DashboardBackground />
          <EmailVerificationBanner />
          <TrialBanner />
          <div
            className="lg:hidden"
            style={{ height: "calc(env(safe-area-inset-top) + 3rem)" }}
          />
          <div
            className="p-4 sm:p-6 lg:p-8 max-w-7xl mx-auto"
            style={{
              // In landscape on iPhone the front camera / dynamic island sits on
              // the left or right edge of the viewport. Add safe-area insets so
              // dashboard content doesn't slide under it.
              paddingLeft: "max(1rem, env(safe-area-inset-left))",
              paddingRight: "max(1rem, env(safe-area-inset-right))",
            }}
          >{children}</div>
        </main>
      </div>
      <InstallBanner />
      <CapHitDialog />
    </DataSyncProvider>
  );
}

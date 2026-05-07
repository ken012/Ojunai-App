"use client";

import { useEffect, useState } from "react";
import { WifiOff, RefreshCcw } from "lucide-react";
import { LogoMark } from "@/components/logo-mark";

export default function OfflinePage() {
  const [retrying, setRetrying] = useState(false);

  // If the network comes back, reload automatically.
  useEffect(() => {
    if (typeof window === "undefined") return;
    const onOnline = () => window.location.reload();
    window.addEventListener("online", onOnline);
    return () => window.removeEventListener("online", onOnline);
  }, []);

  const tryAgain = () => {
    setRetrying(true);
    window.location.reload();
  };

  return (
    <div className="min-h-screen bg-slate-50 dark:bg-slate-950">
      <div className="mx-auto flex min-h-screen max-w-md flex-col items-center justify-center px-6 py-10 text-center">
        <LogoMark size="lg" />

        <div className="mt-10 flex h-16 w-16 items-center justify-center rounded-2xl bg-gradient-to-br from-cyan-500 to-violet-500 text-white shadow-lg">
          <WifiOff className="h-7 w-7" aria-hidden />
        </div>

        <h1 className="mt-6 text-2xl font-semibold">You&rsquo;re offline</h1>
        <p className="mt-3 text-sm text-muted-foreground">
          Ojunai needs an internet connection to load your business data. The app will reconnect automatically as soon as you&rsquo;re back online.
        </p>

        <button
          onClick={tryAgain}
          disabled={retrying}
          className="mt-8 inline-flex items-center gap-2 rounded-lg bg-gradient-to-r from-cyan-500 to-violet-500 px-5 py-3 text-sm font-medium text-white shadow-sm hover:opacity-95 disabled:opacity-60"
        >
          <RefreshCcw className={`h-4 w-4 ${retrying ? "animate-spin" : ""}`} aria-hidden />
          {retrying ? "Reconnecting…" : "Try again"}
        </button>

        <p className="mt-10 text-xs text-muted-foreground">
          Tip: pages you&rsquo;ve already opened today will still work — try the back button or your home screen.
        </p>
      </div>
    </div>
  );
}

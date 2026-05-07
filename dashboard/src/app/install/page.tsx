"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { Download, Share, Plus, Smartphone, Zap, Wifi, Lock } from "lucide-react";
import {
  getPlatform,
  isInAppBrowser,
  isStandalone,
  trackPwaEvent,
  useInstallPrompt,
  type Platform,
} from "@/lib/pwa";
import { LogoMark } from "@/components/logo-mark";

export default function InstallPage() {
  const { available, trigger } = useInstallPrompt();
  const [mounted, setMounted] = useState(false);
  const [platform, setPlatform] = useState<Platform>("desktop");
  const [inApp, setInApp] = useState(false);
  const [installed, setInstalled] = useState(false);

  useEffect(() => {
    setMounted(true);
    setPlatform(getPlatform());
    setInApp(isInAppBrowser());
    setInstalled(isStandalone());
  }, []);

  const handleAndroidInstall = async () => {
    trackPwaEvent("pwa_banner_clicked", { surface: "install-page" });
    const outcome = await trigger();
    if (outcome === "accepted") trackPwaEvent("pwa_install_accepted", { surface: "install-page" });
    else if (outcome === "dismissed") trackPwaEvent("pwa_install_dismissed", { surface: "install-page" });
  };

  return (
    <div className="min-h-screen bg-slate-50 dark:bg-slate-950">
      <div className="mx-auto max-w-2xl px-4 py-10 sm:py-16">
        <div className="mb-8 flex items-center justify-between">
          <LogoMark size="md" />
          <Link href="/login" className="text-sm text-cyan-600 hover:underline">
            Sign in &rarr;
          </Link>
        </div>

        <div className="rounded-2xl bg-gradient-to-br from-cyan-500 to-violet-500 p-8 text-white shadow-lg sm:p-10">
          <h1 className="text-3xl font-bold leading-tight sm:text-4xl">
            Install Ojunai on your phone
          </h1>
          <p className="mt-3 text-white/90 sm:text-lg">
            One-tap access to your dashboard. No download. No app store.
          </p>
        </div>

        {/* Platform-aware instructions */}
        <div className="mt-8 rounded-2xl border border-slate-200 bg-white p-6 shadow-sm dark:border-slate-800 dark:bg-slate-900 sm:p-8">
          {!mounted ? (
            <p className="text-sm text-muted-foreground">Detecting your device&hellip;</p>
          ) : installed ? (
            <InstalledNotice />
          ) : inApp ? (
            <InAppBrowserNotice />
          ) : platform === "ios" ? (
            <IosInstructions />
          ) : platform === "android" ? (
            <AndroidInstructions promptAvailable={available} onInstall={handleAndroidInstall} />
          ) : (
            <DesktopInstructions />
          )}
        </div>

        {/* Why install */}
        <div className="mt-8 grid grid-cols-1 gap-4 sm:grid-cols-3">
          <Benefit
            icon={<Zap className="h-5 w-5" />}
            title="One-tap access"
            body="Open Ojunai from your home screen — no Safari, no typing the URL."
          />
          <Benefit
            icon={<Wifi className="h-5 w-5" />}
            title="Works on slow networks"
            body="Faster loads on 3G and patchy connections. Cached for speed."
          />
          <Benefit
            icon={<Lock className="h-5 w-5" />}
            title="No app store needed"
            body="Installs straight from your browser. No reviews, no waiting."
          />
        </div>

        <p className="mt-10 text-center text-xs text-muted-foreground">
          Already a customer? <Link href="/login" className="underline">Sign in to your dashboard</Link>.
        </p>
      </div>
    </div>
  );
}

function InstalledNotice() {
  return (
    <div className="text-center">
      <Smartphone className="mx-auto h-10 w-10 text-emerald-500" aria-hidden />
      <h2 className="mt-3 text-lg font-semibold">You&rsquo;re using the installed app</h2>
      <p className="mt-2 text-sm text-muted-foreground">
        Ojunai is already installed on this device. Open it from your home screen anytime.
      </p>
    </div>
  );
}

function InAppBrowserNotice() {
  return (
    <div>
      <h2 className="text-lg font-semibold">Open this page in your browser first</h2>
      <p className="mt-2 text-sm text-muted-foreground">
        You&rsquo;re viewing Ojunai inside another app (like Instagram or WhatsApp). Installing isn&rsquo;t
        supported here.
      </p>
      <ol className="mt-4 list-decimal space-y-2 pl-5 text-sm">
        <li>Tap the menu (usually three dots in the top right)</li>
        <li>Choose <strong>Open in browser</strong> &mdash; on iPhone pick Safari, on Android pick Chrome</li>
        <li>Then come back to this page and follow the install steps</li>
      </ol>
    </div>
  );
}

function IosInstructions() {
  return (
    <div>
      <div className="mb-5 inline-flex items-center gap-2 rounded-full bg-cyan-50 px-3 py-1 text-xs font-medium text-cyan-700 dark:bg-cyan-950 dark:text-cyan-300">
        iPhone or iPad detected
      </div>
      <h2 className="text-xl font-semibold">Three steps in Safari</h2>
      <ol className="mt-5 space-y-5">
        <Step n={1} icon={<Share className="h-5 w-5" />} title="Tap the Share button"
          body="At the bottom of Safari — a square with an upward arrow." />
        <Step n={2} icon={<Plus className="h-5 w-5" />} title="Choose Add to Home Screen"
          body="Scroll down in the share sheet if needed." />
        <Step n={3} icon={<Smartphone className="h-5 w-5" />} title="Tap Add"
          body="The Ojunai icon appears on your home screen. Open it from there for the full app experience." />
      </ol>
      <p className="mt-6 text-xs text-muted-foreground">
        Only works in Safari. If you opened this in Chrome or another browser, copy the link and open it in Safari.
      </p>
    </div>
  );
}

function AndroidInstructions({
  promptAvailable,
  onInstall,
}: {
  promptAvailable: boolean;
  onInstall: () => void;
}) {
  return (
    <div>
      <div className="mb-5 inline-flex items-center gap-2 rounded-full bg-violet-50 px-3 py-1 text-xs font-medium text-violet-700 dark:bg-violet-950 dark:text-violet-300">
        Android detected
      </div>
      <h2 className="text-xl font-semibold">Install in one tap</h2>
      {promptAvailable ? (
        <>
          <p className="mt-3 text-sm text-muted-foreground">
            Tap the button below — Chrome will ask you to confirm. The icon appears on your home screen.
          </p>
          <button
            onClick={onInstall}
            className="mt-5 inline-flex items-center gap-2 rounded-lg bg-gradient-to-r from-cyan-500 to-violet-500 px-5 py-3 text-sm font-medium text-white shadow-sm hover:opacity-95"
          >
            <Download size={16} aria-hidden />
            Install Ojunai
          </button>
        </>
      ) : (
        <>
          <p className="mt-3 text-sm text-muted-foreground">
            If your browser didn&rsquo;t offer to install automatically, follow these steps:
          </p>
          <ol className="mt-4 space-y-3 text-sm">
            <li>1. Tap the <strong>three-dot menu</strong> in the top right of Chrome</li>
            <li>2. Choose <strong>Install app</strong> or <strong>Add to Home screen</strong></li>
            <li>3. Tap <strong>Install</strong> to confirm</li>
          </ol>
        </>
      )}
    </div>
  );
}

function DesktopInstructions() {
  return (
    <div>
      <h2 className="text-xl font-semibold">Open this page on your phone</h2>
      <p className="mt-3 text-sm text-muted-foreground">
        The Ojunai install works best on a phone. Visit this URL on your iPhone or Android:
      </p>
      <div className="mt-4 rounded-lg border bg-slate-50 px-4 py-3 font-mono text-sm dark:bg-slate-950">
        ojunai.com/install
      </div>
      <p className="mt-4 text-xs text-muted-foreground">
        On desktop Chrome you can also install Ojunai as a windowed app — look for the install icon in the address bar.
      </p>
    </div>
  );
}

function Step({ n, icon, title, body }: { n: number; icon: React.ReactNode; title: string; body: string }) {
  return (
    <li className="flex gap-4">
      <div className="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-cyan-500 to-violet-500 text-sm font-bold text-white">
        {n}
      </div>
      <div className="flex-1 pt-1">
        <div className="flex items-center gap-2 font-medium">
          <span className="text-foreground/70">{icon}</span>
          {title}
        </div>
        <p className="mt-1 text-sm text-muted-foreground">{body}</p>
      </div>
    </li>
  );
}

function Benefit({ icon, title, body }: { icon: React.ReactNode; title: string; body: string }) {
  return (
    <div className="rounded-xl border border-slate-200 bg-white p-4 dark:border-slate-800 dark:bg-slate-900">
      <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-gradient-to-br from-cyan-500 to-violet-500 text-white">
        {icon}
      </div>
      <h3 className="mt-3 font-semibold">{title}</h3>
      <p className="mt-1 text-sm text-muted-foreground">{body}</p>
    </div>
  );
}

"use client";

/**
 * PWA install detection, prompt capture, dismiss state, and lightweight analytics.
 * No external deps — uses localStorage and the standard beforeinstallprompt event.
 */

import { useCallback, useEffect, useState } from "react";

// ---------- platform & install-state detection ----------

export type Platform = "ios" | "android" | "desktop";

export function getPlatform(): Platform {
  if (typeof window === "undefined") return "desktop";
  const ua = window.navigator.userAgent;
  if (/iPad|iPhone|iPod/.test(ua)) return "ios";
  if (/Android/.test(ua)) return "android";
  return "desktop";
}

/** True when running as an installed PWA (not a browser tab). */
export function isStandalone(): boolean {
  if (typeof window === "undefined") return false;
  if (window.matchMedia?.("(display-mode: standalone)").matches) return true;
  // iOS Safari uses a non-standard property
  const nav = window.navigator as Navigator & { standalone?: boolean };
  return nav.standalone === true;
}

/** True for in-app browsers (Instagram, Facebook, Twitter, etc) where install won't work. */
export function isInAppBrowser(): boolean {
  if (typeof window === "undefined") return false;
  const ua = window.navigator.userAgent;
  return /FBAN|FBAV|Instagram|Twitter|Line|MicroMessenger|WhatsApp|Snapchat/i.test(ua);
}

// ---------- beforeinstallprompt capture (Android / desktop Chrome) ----------

type BeforeInstallPromptEvent = Event & {
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: "accepted" | "dismissed"; platform: string }>;
};

let cachedPrompt: BeforeInstallPromptEvent | null = null;
const promptListeners = new Set<(e: BeforeInstallPromptEvent | null) => void>();

if (typeof window !== "undefined") {
  window.addEventListener("beforeinstallprompt", (e) => {
    e.preventDefault();
    cachedPrompt = e as BeforeInstallPromptEvent;
    promptListeners.forEach((cb) => cb(cachedPrompt));
  });
  window.addEventListener("appinstalled", () => {
    cachedPrompt = null;
    promptListeners.forEach((cb) => cb(null));
  });
}

export function useInstallPrompt() {
  const [prompt, setPrompt] = useState<BeforeInstallPromptEvent | null>(cachedPrompt);

  useEffect(() => {
    promptListeners.add(setPrompt);
    return () => {
      promptListeners.delete(setPrompt);
    };
  }, []);

  const trigger = useCallback(async (): Promise<"accepted" | "dismissed" | "unavailable"> => {
    if (!prompt) return "unavailable";
    await prompt.prompt();
    const { outcome } = await prompt.userChoice;
    cachedPrompt = null;
    setPrompt(null);
    return outcome;
  }, [prompt]);

  return { available: prompt !== null, trigger };
}

// ---------- dismiss state ----------

const STORAGE_PREFIX = "ojunai-pwa:";

function readNumber(key: string): number | null {
  if (typeof window === "undefined") return null;
  const v = window.localStorage.getItem(STORAGE_PREFIX + key);
  if (!v) return null;
  const n = Number(v);
  return Number.isFinite(n) ? n : null;
}

function writeNumber(key: string, value: number): void {
  if (typeof window === "undefined") return;
  window.localStorage.setItem(STORAGE_PREFIX + key, String(value));
}

const DISMISS_KEY = "banner-dismissed-at";
const VISITS_KEY = "visits";
const DISMISS_DAYS = 14;
const VISIT_THRESHOLD = 2;

export function isBannerDismissed(): boolean {
  const ts = readNumber(DISMISS_KEY);
  if (ts == null) return false;
  const ageDays = (Date.now() - ts) / (1000 * 60 * 60 * 24);
  return ageDays < DISMISS_DAYS;
}

export function dismissBanner(): void {
  writeNumber(DISMISS_KEY, Date.now());
}

/** Increment the visit count once per session and return the lifetime total. */
export function bumpVisitCount(): number {
  if (typeof window === "undefined") return 0;
  const sessionKey = STORAGE_PREFIX + "session-counted";
  if (window.sessionStorage.getItem(sessionKey)) {
    return readNumber(VISITS_KEY) ?? 0;
  }
  const next = (readNumber(VISITS_KEY) ?? 0) + 1;
  writeNumber(VISITS_KEY, next);
  window.sessionStorage.setItem(sessionKey, "1");
  return next;
}

// ---------- composed visibility decision ----------

export type ShowDecision =
  | { show: false; reason: string }
  | { show: true; platform: Platform };

export function decideShowBanner(opts: { visits: number; promptAvailable: boolean }): ShowDecision {
  if (typeof window === "undefined") return { show: false, reason: "ssr" };
  if (isStandalone()) return { show: false, reason: "already-installed" };
  if (isInAppBrowser()) return { show: false, reason: "in-app-browser" };
  if (isBannerDismissed()) return { show: false, reason: "dismissed" };
  if (opts.visits < VISIT_THRESHOLD) return { show: false, reason: "first-visit" };

  const platform = getPlatform();
  if (platform === "desktop") return { show: false, reason: "desktop" };
  if (platform === "android" && !opts.promptAvailable) {
    return { show: false, reason: "android-no-prompt" };
  }
  return { show: true, platform };
}

// ---------- analytics shim ----------

export type PwaEvent =
  | "pwa_banner_shown"
  | "pwa_banner_clicked"
  | "pwa_install_accepted"
  | "pwa_install_dismissed"
  | "pwa_launch_standalone";

export function trackPwaEvent(name: PwaEvent, payload?: Record<string, unknown>): void {
  const data = { name, platform: getPlatform(), standalone: isStandalone(), ts: Date.now(), ...payload };
  if (typeof window === "undefined") return;
  // Console for now — when /api/events ships server-side, swap to api.post here.
  // Kept silent in production to avoid log noise on customer phones.
  if (process.env.NODE_ENV !== "production") {
    // eslint-disable-next-line no-console
    console.debug("[pwa]", data);
  }
  // Best-effort beacon — fails silently if endpoint doesn't exist.
  try {
    if (typeof navigator.sendBeacon === "function") {
      const url = (process.env.NEXT_PUBLIC_API_URL || "") + "/events";
      navigator.sendBeacon(url, new Blob([JSON.stringify(data)], { type: "application/json" }));
    }
  } catch {
    // analytics must never throw
  }
}

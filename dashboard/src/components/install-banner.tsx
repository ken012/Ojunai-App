"use client";

import { useEffect, useState } from "react";
import { Download, X } from "lucide-react";
import {
  bumpVisitCount,
  decideShowBanner,
  dismissBanner,
  isStandalone,
  trackPwaEvent,
  useInstallPrompt,
} from "@/lib/pwa";
import { InstallModalIos } from "@/components/install-modal-ios";

const STANDALONE_SESSION_KEY = "ojunai-pwa:standalone-tracked";

/**
 * Bottom-sheet banner that nudges qualifying mobile users to install Ojunai.
 *
 * Shows only when: mobile, authenticated context (mounted inside the dashboard
 * layout), not already standalone, not an in-app browser, not dismissed in
 * the last 14 days, and the user has visited at least twice.
 */
export function InstallBanner() {
  const { available, trigger } = useInstallPrompt();
  const [decision, setDecision] = useState<ReturnType<typeof decideShowBanner>>({
    show: false,
    reason: "ssr",
  });
  const [iosOpen, setIosOpen] = useState(false);
  const [hidden, setHidden] = useState(false);
  const [shownTracked, setShownTracked] = useState(false);

  // Track standalone launch once per session — most important PWA metric.
  useEffect(() => {
    if (typeof window === "undefined" || !isStandalone()) return;
    if (window.sessionStorage.getItem(STANDALONE_SESSION_KEY)) return;
    window.sessionStorage.setItem(STANDALONE_SESSION_KEY, "1");
    trackPwaEvent("pwa_launch_standalone");
  }, []);

  // Compute visibility on mount and whenever prompt-availability flips.
  useEffect(() => {
    const visits = bumpVisitCount();
    setDecision(decideShowBanner({ visits, promptAvailable: available }));
  }, [available]);

  // Fire shown event once per session when the banner first becomes visible.
  useEffect(() => {
    if (decision.show && !shownTracked && !hidden) {
      trackPwaEvent("pwa_banner_shown", { surface: "dashboard-bottom" });
      setShownTracked(true);
    }
  }, [decision.show, shownTracked, hidden]);

  if (!decision.show || hidden) return null;

  const isIos = decision.platform === "ios";

  const handleInstall = async () => {
    trackPwaEvent("pwa_banner_clicked", { platform: decision.platform });
    if (isIos) {
      setIosOpen(true);
      return;
    }
    const outcome = await trigger();
    if (outcome === "accepted") {
      trackPwaEvent("pwa_install_accepted");
      setHidden(true);
    } else if (outcome === "dismissed") {
      trackPwaEvent("pwa_install_dismissed", { source: "native-prompt" });
    }
  };

  const handleLater = () => {
    dismissBanner();
    trackPwaEvent("pwa_install_dismissed", { source: "later-button" });
    setHidden(true);
  };

  return (
    <>
      <div
        role="region"
        aria-label="Install Ojunai"
        className="lg:hidden fixed inset-x-0 bottom-0 z-40 border-t border-slate-200 bg-white py-3 shadow-lg dark:border-slate-800 dark:bg-slate-900"
        style={{
          paddingBottom: "max(0.75rem, env(safe-area-inset-bottom))",
          // In landscape the camera/notch can sit on the left or right edge
          // of this banner — push content inward so the install button and
          // copy never disappear under it.
          paddingLeft: "max(1rem, env(safe-area-inset-left))",
          paddingRight: "max(1rem, env(safe-area-inset-right))",
        }}
      >
        <div className="mx-auto flex max-w-xl items-center gap-3">
          <div className="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-xl bg-gradient-to-br from-cyan-500 to-violet-500 text-white">
            <Download size={18} aria-hidden />
          </div>
          <div className="min-w-0 flex-1">
            <p className="text-sm font-semibold text-foreground">Install Ojunai on your phone</p>
            <p className="truncate text-xs text-muted-foreground">
              One-tap access. No app store needed.
            </p>
          </div>
          <button
            onClick={handleInstall}
            className="flex-shrink-0 rounded-lg bg-gradient-to-r from-cyan-500 to-violet-500 px-3 py-2 text-sm font-medium text-white shadow-sm hover:opacity-95"
          >
            Install
          </button>
          <button
            onClick={handleLater}
            aria-label="Dismiss install prompt"
            className="flex-shrink-0 rounded-lg p-2 text-slate-400 hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-slate-800 dark:hover:text-slate-200"
          >
            <X size={16} aria-hidden />
          </button>
        </div>
      </div>

      <InstallModalIos open={iosOpen} onOpenChange={setIosOpen} />
    </>
  );
}

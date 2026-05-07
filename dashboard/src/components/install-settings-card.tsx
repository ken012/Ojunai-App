"use client";

import { useEffect, useState } from "react";
import { Smartphone, CheckCircle2, Download } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import {
  getPlatform,
  isStandalone,
  trackPwaEvent,
  useInstallPrompt,
  type Platform,
} from "@/lib/pwa";
import { InstallModalIos } from "@/components/install-modal-ios";

/**
 * Permanent settings entry so users who dismissed the banner can still install.
 * Hides itself when already running as an installed PWA.
 */
export function InstallSettingsCard() {
  const { available, trigger } = useInstallPrompt();
  const [mounted, setMounted] = useState(false);
  const [installed, setInstalled] = useState(false);
  const [platform, setPlatform] = useState<Platform>("desktop");
  const [iosOpen, setIosOpen] = useState(false);

  useEffect(() => {
    setMounted(true);
    setInstalled(isStandalone());
    setPlatform(getPlatform());
  }, []);

  if (!mounted) return null;

  const handleClick = async () => {
    trackPwaEvent("pwa_banner_clicked", { surface: "settings" });
    if (platform === "ios") {
      setIosOpen(true);
      return;
    }
    if (platform === "android" && available) {
      const outcome = await trigger();
      if (outcome === "accepted") trackPwaEvent("pwa_install_accepted", { surface: "settings" });
      else trackPwaEvent("pwa_install_dismissed", { surface: "settings", source: "native-prompt" });
      return;
    }
    // Desktop or Android-without-prompt: open the public install instructions page in a new tab.
    window.open("/install", "_blank");
  };

  const buttonLabel = (() => {
    if (installed) return "Installed";
    if (platform === "ios") return "Show me how";
    if (platform === "android") return available ? "Install" : "View instructions";
    return "Open on your phone";
  })();

  const subtitle = (() => {
    if (installed) return "You're using the installed app right now.";
    if (platform === "ios") return "Add Ojunai to your home screen via Safari's Share menu.";
    if (platform === "android") return "Install Ojunai as an app — no Play Store needed.";
    return "Open ojunai.com on your phone to install it as an app.";
  })();

  return (
    <>
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-sm font-semibold text-slate-700 dark:text-slate-300 flex items-center gap-2">
            <Smartphone size={15} className="text-cyan-500" />
            Install Ojunai on this device
          </CardTitle>
          <p className="text-xs text-slate-500 dark:text-slate-400 mt-1">{subtitle}</p>
        </CardHeader>
        <CardContent>
          {installed ? (
            <div className="flex items-center gap-2 text-sm text-emerald-600 dark:text-emerald-400">
              <CheckCircle2 size={16} aria-hidden />
              <span>Already installed on this device</span>
            </div>
          ) : (
            <Button onClick={handleClick} size="sm" className="gap-2">
              <Download size={14} aria-hidden />
              {buttonLabel}
            </Button>
          )}
        </CardContent>
      </Card>
      <InstallModalIos open={iosOpen} onOpenChange={setIosOpen} />
    </>
  );
}

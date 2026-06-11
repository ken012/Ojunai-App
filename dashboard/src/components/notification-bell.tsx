"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { useRouter } from "next/navigation";
import { Bell, Check, X, AlertCircle, AlertTriangle, Info, ArrowRight } from "lucide-react";
import { cn } from "@/lib/utils";
import {
  type AlertDto,
  listAlerts,
  unreadAlertCount,
  markAlertRead,
  dismissAlert,
  markAllAlertsRead,
} from "@/lib/alerts";

const POLL_MS = 40_000;

/**
 * Notification bell for the dashboard navbar. Polls /alerts/unread-count every 40s
 * (paused when the tab is hidden), shows a badge with the count, and opens a dropdown
 * with the most recent alerts on click. Each row has dismiss + click-to-navigate.
 */
export function NotificationBell() {
  const router = useRouter();
  const [count, setCount] = useState(0);
  const [open, setOpen] = useState(false);
  const [alerts, setAlerts] = useState<AlertDto[] | null>(null);
  const [loading, setLoading] = useState(false);
  // Which alert row is currently expanded (showing the full title + body and
  // any "View source" CTA). Only one row expands at a time to keep the panel tidy.
  const [expandedId, setExpandedId] = useState<string | null>(null);
  // Computed fixed-position coordinates for the popup. Using `position: fixed`
  // lets the popup escape the sidebar's `overflow-y-auto` clip — without this,
  // the dropdown gets cropped at the sidebar's right edge on desktop.
  const [popupPos, setPopupPos] = useState<{ top: number; left: number } | null>(null);
  const popupRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);

  // Poll for unread count. Pauses while the tab is hidden — saves battery and API noise.
  useEffect(() => {
    let timer: ReturnType<typeof setInterval> | null = null;

    async function refresh() {
      try { setCount(await unreadAlertCount()); } catch { /* silent — keep last value */ }
    }

    function start() {
      refresh();
      timer = setInterval(refresh, POLL_MS);
    }
    function stop() {
      if (timer) { clearInterval(timer); timer = null; }
    }

    function onVisibility() {
      if (document.visibilityState === "visible") start();
      else stop();
    }

    if (document.visibilityState === "visible") start();
    document.addEventListener("visibilitychange", onVisibility);
    return () => {
      stop();
      document.removeEventListener("visibilitychange", onVisibility);
    };
  }, []);

  // Click-outside to close
  useEffect(() => {
    if (!open) return;
    function onClick(e: MouseEvent) {
      if (
        popupRef.current && !popupRef.current.contains(e.target as Node) &&
        buttonRef.current && !buttonRef.current.contains(e.target as Node)
      ) setOpen(false);
    }
    document.addEventListener("mousedown", onClick);
    return () => document.removeEventListener("mousedown", onClick);
  }, [open]);

  /**
   * Width of the popup. Desktop stays at the comfortable 320px (room to sit
   * next to the bell, plenty of dashboard visible alongside). Mobile gets a
   * bigger panel (up to 420px) so alert text isn't cramped — it overlays the
   * dashboard underneath, which is fine since the popup is a modal-style
   * overlay anyway.
   */
  function popupWidthFor(viewportWidth: number): number {
    const isMobile = viewportWidth < 640;
    if (isMobile) return Math.min(420, viewportWidth - 24);
    return 320;
  }

  /**
   * Anchors the popup to the bell's current viewport position. Called on open
   * and on resize/scroll while open so the panel tracks if the page reflows.
   *
   *  - On MOBILE the bell sits at top-right of the screen, so right-aligning
   *    the popup to the bell (popup extends leftward) keeps it on-screen.
   *  - On DESKTOP the bell lives inside the LEFT sidebar; right-aligning would
   *    keep the popup entirely inside the sidebar. Anchor 8px to the right of
   *    the bell instead so the popup spills into the dashboard area where the
   *    user can actually see it overlay content.
   *
   * Clamped either way so it never bleeds off-screen.
   */
  const recomputePosition = useCallback(() => {
    if (!buttonRef.current) return;
    const rect = buttonRef.current.getBoundingClientRect();
    const popupWidth = popupWidthFor(window.innerWidth);
    const isMobile = window.innerWidth < 640;
    let left = isMobile ? rect.right - popupWidth : rect.right + 8;
    if (left < 8) left = 8;
    const maxLeft = window.innerWidth - popupWidth - 8;
    if (left > maxLeft) left = maxLeft;
    setPopupPos({ top: rect.bottom + 8, left });
  }, []);

  // Recompute on viewport changes while the popup is open so it stays anchored.
  useEffect(() => {
    if (!open) return;
    function handler() { recomputePosition(); }
    window.addEventListener("resize", handler);
    window.addEventListener("scroll", handler, true);
    return () => {
      window.removeEventListener("resize", handler);
      window.removeEventListener("scroll", handler, true);
    };
  }, [open, recomputePosition]);

  async function handleOpen() {
    if (open) { setOpen(false); return; }
    recomputePosition();
    setOpen(true);
    setLoading(true);
    setExpandedId(null);
    try {
      const list = await listAlerts({ limit: 20 });
      setAlerts(list);
    } catch {
      setAlerts([]);
    } finally {
      setLoading(false);
    }
  }

  async function handleClickRow(alert: AlertDto) {
    // Mark read on first click, regardless of expand/collapse direction.
    if (!alert.readAtUtc) {
      await markAlertRead(alert.id).catch(() => {});
      setAlerts((prev) => prev?.map((a) => a.id === alert.id ? { ...a, readAtUtc: new Date().toISOString() } : a) ?? null);
      setCount((c) => Math.max(0, c - 1));
    }
    // Toggle expand. We no longer auto-navigate — users explicitly click "View
    // source →" when they want to be taken to the page that triggered the alert.
    setExpandedId((prev) => (prev === alert.id ? null : alert.id));
  }

  function handleViewSource(e: React.MouseEvent, alert: AlertDto) {
    e.stopPropagation();
    if (!alert.linkUrl) return;
    router.push(alert.linkUrl);
    setOpen(false);
  }

  async function handleDismiss(e: React.MouseEvent, alert: AlertDto) {
    e.stopPropagation();
    await dismissAlert(alert.id).catch(() => {});
    setAlerts((prev) => prev?.filter((a) => a.id !== alert.id) ?? null);
    if (!alert.readAtUtc) setCount((c) => Math.max(0, c - 1));
  }

  async function handleMarkAllRead() {
    await markAllAlertsRead().catch(() => {});
    setAlerts((prev) => prev?.map((a) => ({ ...a, readAtUtc: a.readAtUtc ?? new Date().toISOString() })) ?? null);
    setCount(0);
  }

  return (
    <div className="relative">
      <button
        ref={buttonRef}
        onClick={handleOpen}
        className="relative p-2 rounded-md text-slate-300 hover:bg-slate-800 hover:text-white transition-colors"
        aria-label={`Notifications${count > 0 ? ` (${count} unread)` : ""}`}
      >
        <Bell size={18} />
        {count > 0 && (
          <span className="absolute top-1 right-1 min-w-[16px] h-[16px] px-1 rounded-full bg-cyan-500 text-white text-[10px] font-semibold flex items-center justify-center">
            {count > 99 ? "99+" : count}
          </span>
        )}
      </button>

      {open && popupPos && typeof document !== "undefined" && createPortal(
        <div
          ref={popupRef}
          /* `position: fixed` plus a portal to document.body so the popup
             escapes the sidebar's `transform`-induced containing block.
             Without the portal, position:fixed gets re-anchored to the
             sidebar (which has Tailwind's `transform` class for the mobile
             slide animation) and the popup ends up trapped inside it. */
          style={{ position: "fixed", top: popupPos.top, left: popupPos.left, width: typeof window !== "undefined" ? popupWidthFor(window.innerWidth) : 320 }}
          className="max-h-[480px] flex flex-col rounded-lg shadow-lg border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 z-50"
        >
          <div className="flex items-center justify-between px-3 py-2 border-b border-slate-100 dark:border-slate-800">
            <span className="text-sm font-semibold text-slate-900 dark:text-slate-50">Notifications</span>
            {count > 0 && (
              <button
                onClick={handleMarkAllRead}
                className="text-[11px] text-cyan-600 hover:underline flex items-center gap-1"
              >
                <Check size={11} /> Mark all read
              </button>
            )}
          </div>

          <div className="overflow-y-auto flex-1">
            {loading && <p className="text-xs text-slate-400 dark:text-slate-500 text-center py-6">Loading…</p>}
            {!loading && alerts && alerts.length === 0 && (
              <p className="text-xs text-slate-400 dark:text-slate-500 text-center py-8">No notifications.</p>
            )}
            {!loading && alerts && alerts.map((a) => {
              const expanded = expandedId === a.id;
              return (
                <div
                  key={a.id}
                  role="button"
                  tabIndex={0}
                  onClick={() => handleClickRow(a)}
                  onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); handleClickRow(a); } }}
                  className={cn(
                    "w-full text-left px-3 py-2.5 border-b border-slate-100 dark:border-slate-800 hover:bg-slate-50 dark:hover:bg-slate-800/60 transition-colors group cursor-pointer",
                    !a.readAtUtc && "bg-cyan-50/40 dark:bg-cyan-950/20",
                  )}
                >
                  <div className="flex items-start gap-2">
                    <SeverityIcon severity={a.severity} />
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-1.5">
                        <p className={cn("text-sm font-medium text-slate-900 dark:text-slate-50 flex-1", !expanded && "truncate")}>{a.title}</p>
                        <ScopeChip scope={a.scope} />
                      </div>
                      <p className={cn("text-xs text-slate-500 dark:text-slate-400 mt-0.5 whitespace-pre-wrap", !expanded && "line-clamp-2")}>{a.body}</p>
                      <div className="flex items-center justify-between gap-2 mt-1">
                        <p className="text-[10px] text-slate-400 dark:text-slate-500">{timeAgo(a.createdAtUtc)}</p>
                        {expanded && a.linkUrl && (
                          <button
                            onClick={(e) => handleViewSource(e, a)}
                            className="text-[11px] font-medium text-cyan-600 dark:text-cyan-400 hover:underline flex items-center gap-1"
                          >
                            View source <ArrowRight size={11} />
                          </button>
                        )}
                      </div>
                    </div>
                    <button
                      onClick={(e) => handleDismiss(e, a)}
                      className="p-1 rounded text-slate-400 dark:text-slate-500 hover:bg-slate-200 dark:hover:bg-slate-700 hover:text-slate-700 dark:hover:text-slate-200 opacity-0 group-hover:opacity-100 transition-opacity"
                      aria-label="Dismiss"
                    >
                      <X size={12} />
                    </button>
                  </div>
                </div>
              );
            })}
          </div>
        </div>,
        document.body
      )}
    </div>
  );
}

function SeverityIcon({ severity }: { severity: AlertDto["severity"] }) {
  if (severity === "Critical") return <AlertCircle size={14} className="text-rose-500 mt-0.5 flex-shrink-0" />;
  if (severity === "Warning") return <AlertTriangle size={14} className="text-amber-500 mt-0.5 flex-shrink-0" />;
  return <Info size={14} className="text-cyan-500 mt-0.5 flex-shrink-0" />;
}

/**
 * Tiny chip that labels each alert as Business (operational, visible to Owner/Admin)
 * or Personal (security/privacy, visible only to the affected user).
 */
function ScopeChip({ scope }: { scope: AlertDto["scope"] }) {
  const isBusiness = scope === "Business";
  return (
    <span
      className={cn(
        "text-[9px] font-medium uppercase tracking-wide px-1.5 py-px rounded-full flex-shrink-0",
        isBusiness
          ? "bg-violet-50 text-violet-700 ring-1 ring-inset ring-violet-200 dark:bg-violet-950/40 dark:text-violet-300 dark:ring-violet-900"
          : "bg-slate-100 text-slate-600 ring-1 ring-inset ring-slate-200 dark:bg-slate-800 dark:text-slate-300 dark:ring-slate-700",
      )}
      title={isBusiness ? "Business alert — visible to Owner/Admin" : "Personal alert — visible only to you"}
    >
      {isBusiness ? "Business" : "Personal"}
    </span>
  );
}

function timeAgo(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime();
  const mins = Math.floor(diff / 60_000);
  if (mins < 1) return "just now";
  if (mins < 60) return `${mins}m ago`;
  const hours = Math.floor(mins / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  if (days < 7) return `${days}d ago`;
  return new Date(iso).toLocaleDateString();
}

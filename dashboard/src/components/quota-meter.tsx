"use client";

import { useQuery } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { MessageSquare, Send } from "lucide-react";
import { api } from "@/lib/api";

/**
 * QuotaMeter — surfaces the business's current-month assistant action quotas.
 *
 * Two views:
 *   - `<QuotaMeter compact />` — chip for the dashboard header. One overall % bar with hover tooltip
 *     showing both channels' breakdown. Clicking it expands into the popover.
 *   - `<QuotaMeter />` — full card for the Plan & Billing page. Both channels with progress bars,
 *     period reset date, and an upgrade CTA.
 *
 * Data shape comes from GET /api/subscription/quota — refreshed every 60s while the dashboard
 * is open. Cap=-1 means unlimited (rendered as ∞). Cap=0 means "not included" — for WhatsApp
 * this is the free tier; the meter renders an explicit "Add WhatsApp pack" upsell.
 */

type QuotaChannel = {
  used: number;
  cap: number;
  percentUsed: number;
  label: string;
  isUnlimited: boolean;
};

type QuotaSnapshot = {
  whatsApp: QuotaChannel;
  messaging: QuotaChannel;
  periodStartUtc: string;
  periodEndUtc: string;
  planName: string;
  whatsAppPackName: string;
};

function useQuota() {
  return useQuery<QuotaSnapshot>({
    queryKey: ["subscription-quota"],
    queryFn: async () => {
      const res = await api.get<{ data?: QuotaSnapshot } | QuotaSnapshot>("/subscription/quota");
      // ApiResponse envelope vs raw return — handle both since the controller's wrapping is inconsistent.
      const body = res.data as { data?: QuotaSnapshot } | QuotaSnapshot;
      if ("whatsApp" in body) return body as QuotaSnapshot;
      return (body as { data: QuotaSnapshot }).data;
    },
    refetchInterval: 60_000,
    staleTime: 30_000,
  });
}

export function QuotaMeter({ compact = false }: { compact?: boolean } = {}) {
  const { data, isLoading } = useQuota();
  const [open, setOpen] = useState(false);

  if (isLoading || !data) {
    return compact ? null : (
      <div className="rounded-lg border border-slate-200 dark:border-slate-800 p-4 text-sm text-slate-400">
        Loading quota…
      </div>
    );
  }

  if (compact) {
    return <CompactQuotaButton data={data} open={open} setOpen={setOpen} />;
  }

  return (
    <div className="rounded-lg border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 p-5">
      <QuotaCardBody data={data} />
    </div>
  );
}

/**
 * Compact-mode button + portal-rendered popover.
 *
 * Why a portal: the sidebar that hosts this button uses `overflow-y-auto`, which
 * causes browsers to implicitly clip overflow-x as well. A standard absolutely-
 * positioned popover got cut off at the sidebar's right edge on desktop. Rendering
 * via createPortal mounts the popover at the document body so it sits in its own
 * stacking context and the sidebar's overflow doesn't apply.
 *
 * Position is computed from the button's bounding rect each time the popover
 * opens. Right-aligned to the button. Closes on click-outside.
 */
function CompactQuotaButton({
  data,
  open,
  setOpen,
}: {
  data: QuotaSnapshot;
  open: boolean;
  setOpen: (open: boolean | ((prev: boolean) => boolean)) => void;
}) {
  const buttonRef = useRef<HTMLButtonElement>(null);
  const [coords, setCoords] = useState<{ top: number; right: number } | null>(null);
  const [mounted, setMounted] = useState(false);

  useEffect(() => {
    setMounted(true);
  }, []);

  useEffect(() => {
    if (!open || !buttonRef.current) return;
    const rect = buttonRef.current.getBoundingClientRect();
    // Right-align: distance from viewport right edge. mt-2 = 8px below button.
    setCoords({
      top: rect.bottom + 8,
      right: Math.max(8, window.innerWidth - rect.right),
    });
  }, [open]);

  return (
    <>
      <button
        ref={buttonRef}
        type="button"
        onClick={() => setOpen((o) => !o)}
        className="flex items-center gap-1 px-1.5 py-1 rounded-md hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
        aria-label="Show quota usage"
      >
        <CompactBar channel={data.messaging} accent="cyan" />
        <CompactBar channel={data.whatsApp} accent="emerald" />
      </button>

      {mounted && open && coords &&
        createPortal(
          <>
            <div className="fixed inset-0 z-[9998]" onClick={() => setOpen(false)} />
            <div
              className="fixed z-[9999] w-80 max-w-[calc(100vw-1rem)] rounded-lg border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 shadow-lg p-4"
              style={{ top: coords.top, right: coords.right }}
            >
              <QuotaCardBody data={data} />
            </div>
          </>,
          document.body,
        )}
    </>
  );
}

function QuotaCardBody({ data }: { data: QuotaSnapshot }) {
  const resetDate = new Date(data.periodEndUtc).toLocaleDateString(undefined, {
    month: "short",
    day: "numeric",
  });

  return (
    <>
      <div className="flex items-center justify-between mb-3">
        <h3 className="font-semibold text-sm text-slate-900 dark:text-slate-100">Assistant usage</h3>
        <span className="text-[11px] text-slate-400">Resets {resetDate}</span>
      </div>

      <ChannelRow
        icon={<Send size={14} className="text-cyan-600 dark:text-cyan-400" />}
        channel={data.messaging}
        accent="cyan"
        emptyHint={null}
      />

      <ChannelRow
        icon={<MessageSquare size={14} className="text-emerald-600 dark:text-emerald-400" />}
        channel={data.whatsApp}
        accent="emerald"
        emptyHint="Add a WhatsApp pack to enable"
      />
    </>
  );
}

function ChannelRow({
  icon,
  channel,
  accent,
  emptyHint,
}: {
  icon: React.ReactNode;
  channel: QuotaChannel;
  accent: "cyan" | "emerald";
  emptyHint: string | null;
}) {
  const isZeroCap = channel.cap === 0 && !channel.isUnlimited;

  return (
    <div className="py-2 first:pt-0 last:pb-0">
      <div className="flex items-center justify-between mb-1.5">
        <div className="flex items-center gap-1.5">
          {icon}
          <span className="text-xs font-medium text-slate-700 dark:text-slate-300">
            {channel.label}
          </span>
        </div>
        <span className="text-xs tabular-nums text-slate-600 dark:text-slate-400">
          {channel.isUnlimited ? (
            <>
              {channel.used.toLocaleString()} <span className="text-slate-400">/ ∞</span>
            </>
          ) : isZeroCap ? (
            <span className="text-slate-400 italic">not included</span>
          ) : (
            <>
              {channel.used.toLocaleString()}
              <span className="text-slate-400"> / {channel.cap.toLocaleString()}</span>
            </>
          )}
        </span>
      </div>

      {!isZeroCap && (
        <div className="h-1.5 rounded-full bg-slate-100 dark:bg-slate-800 overflow-hidden">
          <div
            className={`h-full transition-all ${barColor(accent, channel.percentUsed)}`}
            style={{ width: channel.isUnlimited ? "10%" : `${channel.percentUsed}%` }}
          />
        </div>
      )}

      {isZeroCap && emptyHint && (
        <p className="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5">{emptyHint}</p>
      )}
    </div>
  );
}

function CompactBar({ channel, accent }: { channel: QuotaChannel; accent: "cyan" | "emerald" }) {
  const isZero = channel.cap === 0 && !channel.isUnlimited;
  const widthPct = channel.isUnlimited ? 100 : isZero ? 0 : channel.percentUsed;

  return (
    <div className="h-1.5 w-6 rounded-full bg-slate-200 dark:bg-slate-700 overflow-hidden">
      <div
        className={`h-full ${barColor(accent, channel.percentUsed)}`}
        style={{ width: `${widthPct}%` }}
      />
    </div>
  );
}

function barColor(accent: "cyan" | "emerald", pct: number): string {
  // Color shifts as the meter fills — green → amber → red so a glance tells the story
  // without needing the number.
  if (pct >= 100) return "bg-rose-500";
  if (pct >= 80) return "bg-amber-500";
  return accent === "cyan" ? "bg-cyan-500" : "bg-emerald-500";
}

/** Hook so other components (e.g. CapHitDialog) can read the same cached snapshot. */
export function useQuotaSnapshot() {
  return useQuota();
}

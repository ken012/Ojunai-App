"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useState, useEffect } from "react";
import { LogoMark } from "@/components/logo-mark";
import { cn } from "@/lib/utils";
import { logout } from "@/lib/auth";
import { useBusiness } from "@/lib/data-sync";
import { ThemeToggle } from "@/components/theme-toggle";
import { NotificationBell } from "@/components/notification-bell";
import { QuotaMeter } from "@/components/quota-meter";
import {
  Home,
  ShoppingCart,
  Receipt,
  Package,
  Users,
  BarChart3,
  Settings,
  LogOut,
  Menu,
  X,
  Phone,
  ClipboardList,
  Activity,
  FileUp,
  Download,
  Truck,
} from "lucide-react";
import { usePlanStatus } from "@/lib/use-plan-status";

type NavItem = {
  href: string;
  label: string;
  icon: typeof Home;
  /** When set, only show this item if the predicate returns true. */
  showIf?: (ctx: { voiceAIVisible: boolean }) => boolean;
  /** Right-aligned badge label (e.g. "Trial"). */
  badge?: (ctx: { voiceAIPlanStatus?: string }) => string | null;
};
type NavGroup = {
  label: string | null;
  items: NavItem[];
  /** Render this group with reduced visual weight (lighter color, smaller icons). */
  muted?: boolean;
};

// Hub & spoke. Pulse = daily ops, Assets = what you own, Intelligence = look back / AI.
// "More" holds utility/meta destinations — present but visually de-emphasized.
const navGroups: NavGroup[] = [
  {
    label: null,
    items: [{ href: "/", label: "Today", icon: Home }],
  },
  {
    label: "Pulse",
    items: [
      { href: "/sales", label: "Sales", icon: ShoppingCart },
      { href: "/expenses", label: "Expenses", icon: Receipt },
      { href: "/reservations", label: "Bookings", icon: ClipboardList },
    ],
  },
  {
    label: "Assets",
    items: [
      { href: "/inventory", label: "Inventory", icon: Package },
      { href: "/purchasing", label: "Purchasing", icon: Truck },
      { href: "/contacts", label: "Contacts & Ledger", icon: Users },
    ],
  },
  {
    label: "Intelligence",
    items: [
      { href: "/reports", label: "Reports", icon: BarChart3 },
      {
        href: "/voice-ai",
        label: "Voice AI",
        icon: Phone,
        showIf: ({ voiceAIVisible }) => voiceAIVisible,
        badge: ({ voiceAIPlanStatus }) => (voiceAIPlanStatus === "trial" ? "Trial" : null),
      },
    ],
  },
  {
    label: "More",
    muted: true,
    items: [
      { href: "/activity", label: "Activity", icon: Activity },
      { href: "/import", label: "Import", icon: FileUp },
      { href: "/export", label: "Export", icon: Download },
    ],
  },
];

export function Sidebar() {
  const pathname = usePathname();
  const business = useBusiness();
  const { data: planStatus } = usePlanStatus();
  const [open, setOpen] = useState(false);

  // Close drawer on route change
  useEffect(() => {
    setOpen(false);
  }, [pathname]);

  return (
    <>
      {/* Mobile top bar with hamburger */}
      <div
        className="lg:hidden fixed top-0 left-0 right-0 z-30 flex items-center justify-between bg-slate-900 text-white pb-3 border-b border-slate-700"
        style={{
          paddingTop: "calc(env(safe-area-inset-top) + 0.75rem)",
          // Landscape on iPhone puts the camera/dynamic island on the left
          // or right edge of the top bar — without safe-area-inset-{left,right}
          // the notification bell and logo get clipped under it. max() preserves
          // the original 1rem inset on devices with no safe area.
          paddingLeft: "max(1rem, env(safe-area-inset-left))",
          paddingRight: "max(1rem, env(safe-area-inset-right))",
        }}
      >
        <LogoMark size="sm" className="text-cyan-300" />
        <div className="flex items-center gap-1">
          <QuotaMeter compact />
          <NotificationBell />
          <button
            onClick={() => setOpen(!open)}
            className="p-2 rounded hover:bg-slate-800"
            aria-label="Toggle menu"
          >
            {open ? <X size={20} /> : <Menu size={20} />}
          </button>
        </div>
      </div>

      {/* Mobile backdrop */}
      {open && (
        <div
          className="lg:hidden fixed inset-0 z-30 bg-black/50"
          onClick={() => setOpen(false)}
        />
      )}

      {/* Sidebar itself */}
      <aside
        style={{
          paddingTop: "env(safe-area-inset-top)",
          // Lift the footer away from the iOS home-indicator gesture zone (~60pt
          // from the bottom edge per Apple HIG). On devices without a home
          // indicator the safe-area inset is 0, so this just adds a comfortable
          // 1.5rem gap below the footer.
          paddingBottom: "calc(env(safe-area-inset-bottom) + 1.5rem)",
          // Mobile drawer slides in from the left, so in landscape with the
          // camera on the left edge of the screen the logo and nav items would
          // sit under it. safe-area-inset-left handles that.
          paddingLeft: "env(safe-area-inset-left)",
        }}
        className={cn(
          "bg-slate-900 text-white flex flex-col z-40",
          // Desktop: sticky sidebar, stays in view while content scrolls
          "lg:w-60 lg:flex-shrink-0 lg:h-screen lg:sticky lg:top-0 lg:overflow-y-auto",
          // Mobile: fixed drawer
          "fixed top-0 left-0 h-screen w-64 transform transition-transform lg:translate-x-0",
          open ? "translate-x-0" : "-translate-x-full lg:translate-x-0"
        )}
      >
        {/* Logo */}
        <div className="p-5 border-b border-slate-700">
          <div className="flex items-center justify-between">
            <LogoMark size="md" className="text-cyan-300" />
            {/* Quota meter + bell only on desktop here. Mobile shows them in the
                top bar (line ~113), so they'd render twice if visible here when
                the drawer slides open. */}
            <div className="hidden lg:flex items-center gap-1">
              <QuotaMeter compact />
              <NotificationBell />
            </div>
          </div>
          {business && (
            <p className="text-xs text-slate-400 mt-2.5 truncate">{business.name}</p>
          )}
        </div>

        {/* Nav */}
        <nav className="flex-1 p-3 overflow-y-auto">
          {navGroups.map((group, gi) => {
            const visibleItems = group.items.filter((it) =>
              it.showIf ? it.showIf({ voiceAIVisible: !!planStatus?.voiceAIFeatureVisible }) : true
            );
            if (visibleItems.length === 0) return null;
            return (
              <div
                key={gi}
                className={cn(
                  gi > 0 && "mt-4",
                  // Visually separate the muted "More" group
                  group.muted && "mt-6 pt-4 border-t border-slate-800"
                )}
              >
                {group.label && (
                  <p
                    className={cn(
                      "px-3 mb-1 text-[10px] font-bold uppercase tracking-wider",
                      group.muted ? "text-slate-600" : "text-slate-500"
                    )}
                  >
                    {group.label}
                  </p>
                )}
                <div className="space-y-1">
                  {visibleItems.map(({ href, label, icon: Icon, badge }) => {
                    const active = pathname === href;
                    const badgeText = badge?.({ voiceAIPlanStatus: planStatus?.voiceAIPlanStatus }) ?? null;
                    return (
                      <Link
                        key={href}
                        href={href}
                        className={cn(
                          "flex items-center gap-3 rounded-lg font-medium transition-colors",
                          group.muted
                            ? "px-3 py-1.5 text-[13px]"
                            : "px-3 py-2 text-sm",
                          active
                            ? "bg-gradient-to-r from-cyan-500 to-violet-500 text-white shadow-sm"
                            : group.muted
                              ? "text-slate-500 hover:bg-slate-800 hover:text-slate-200"
                              : "text-slate-400 hover:bg-slate-800 hover:text-white"
                        )}
                      >
                        <Icon size={group.muted ? 14 : 16} />
                        {label}
                        {badgeText && (
                          <span className="ml-auto text-[10px] bg-amber-500/20 text-amber-300 px-1.5 py-0.5 rounded-full">
                            {badgeText}
                          </span>
                        )}
                      </Link>
                    );
                  })}
                </div>
              </div>
            );
          })}
        </nav>

        {/* Footer: Theme toggle + Settings + Logout */}
        <div className="p-3 border-t border-slate-700 space-y-1">
          <ThemeToggle />
          <Link
            href="/settings"
            className={cn(
              "flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition-colors",
              pathname === "/settings"
                ? "bg-slate-800 text-white"
                : "text-slate-400 hover:bg-slate-800 hover:text-white"
            )}
          >
            <Settings size={16} />
            Settings
          </Link>
          <button
            onClick={logout}
            className="flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium text-slate-400 hover:bg-slate-800 hover:text-white transition-colors w-full"
          >
            <LogOut size={16} />
            Sign Out
          </button>
        </div>
      </aside>
    </>
  );
}

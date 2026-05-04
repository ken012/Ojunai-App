"use client";

import Link from "next/link";
import Image from "next/image";
import { usePathname } from "next/navigation";
import { useState, useEffect } from "react";
import { cn } from "@/lib/utils";
import { logout } from "@/lib/auth";
import { useBusiness } from "@/lib/data-sync";
import {
  LayoutDashboard,
  ShoppingCart,
  Receipt,
  Package,
  Users,
  BarChart3,
  Settings,
  LogOut,
  Menu,
  X,
  Activity,
  FileUp,
  Download,
  Phone,
  ClipboardList,
} from "lucide-react";
import { usePlanStatus } from "@/lib/use-plan-status";

type NavItem = { href: string; label: string; icon: typeof LayoutDashboard };
type NavGroup = { label: string | null; items: NavItem[] };

const navGroups: NavGroup[] = [
  {
    label: null,
    items: [{ href: "/", label: "Dashboard", icon: LayoutDashboard }],
  },
  {
    label: "Sell",
    items: [
      { href: "/sales", label: "Sales", icon: ShoppingCart },
      { href: "/expenses", label: "Expenses", icon: Receipt },
      { href: "/reservations", label: "Reservations", icon: ClipboardList },
    ],
  },
  {
    label: "Stock",
    items: [{ href: "/inventory", label: "Inventory", icon: Package }],
  },
  {
    label: "People",
    items: [{ href: "/contacts", label: "Contacts", icon: Users }],
  },
  {
    label: "Insights",
    items: [
      { href: "/reports", label: "Reports", icon: BarChart3 },
      { href: "/activity", label: "Activity", icon: Activity },
    ],
  },
  {
    label: "Data",
    items: [
      { href: "/import", label: "Import", icon: FileUp },
      { href: "/export", label: "Export", icon: Download },
    ],
  },
  {
    label: null,
    items: [{ href: "/settings", label: "Settings", icon: Settings }],
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
      <div className="lg:hidden fixed top-0 left-0 right-0 z-30 flex items-center justify-between bg-slate-900 text-white px-4 py-3 border-b border-slate-700">
        <div className="flex items-center gap-2">
          <Image src="/icon.png" alt="" width={28} height={28} priority />
          <h1 className="text-xl font-black bg-gradient-to-r from-cyan-400 to-violet-400 bg-clip-text text-transparent">
            Ojunai
          </h1>
        </div>
        <button
          onClick={() => setOpen(!open)}
          className="p-2 rounded hover:bg-slate-800"
          aria-label="Toggle menu"
        >
          {open ? <X size={20} /> : <Menu size={20} />}
        </button>
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
          <div className="flex items-center gap-2.5">
            <Image src="/icon.png" alt="" width={36} height={36} priority />
            <h1 className="text-2xl font-black bg-gradient-to-r from-cyan-400 to-violet-400 bg-clip-text text-transparent">
              Ojunai
            </h1>
          </div>
          {business && (
            <p className="text-xs text-slate-400 mt-2 truncate">{business.name}</p>
          )}
        </div>

        {/* Nav */}
        <nav className="flex-1 p-3 overflow-y-auto">
          {navGroups.map((group, gi) => (
            <div key={gi} className={gi > 0 ? "mt-4" : ""}>
              {group.label && (
                <p className="px-3 mb-1 text-[10px] font-bold uppercase tracking-wider text-slate-500">
                  {group.label}
                </p>
              )}
              <div className="space-y-1">
                {group.items.map(({ href, label, icon: Icon }) => {
                  const active = pathname === href;
                  return (
                    <Link
                      key={href}
                      href={href}
                      className={cn(
                        "flex items-center gap-3 px-3 py-2 rounded-lg text-sm font-medium transition-colors",
                        active
                          ? "bg-gradient-to-r from-cyan-500 to-violet-500 text-white shadow-sm"
                          : "text-slate-400 hover:bg-slate-800 hover:text-white"
                      )}
                    >
                      <Icon size={16} />
                      {label}
                    </Link>
                  );
                })}
              </div>
            </div>
          ))}
          {planStatus?.voiceAIFeatureVisible && (
            <div className="mt-4">
              <p className="px-3 mb-1 text-[10px] font-bold uppercase tracking-wider text-slate-500">
                AI
              </p>
              <Link
                href="/voice-ai"
                className={cn(
                  "flex items-center gap-3 px-3 py-2 rounded-lg text-sm font-medium transition-colors",
                  pathname === "/voice-ai"
                    ? "bg-gradient-to-r from-cyan-500 to-violet-500 text-white shadow-sm"
                    : "text-slate-400 hover:bg-slate-800 hover:text-white"
                )}
              >
                <Phone size={16} />
                Voice AI
                {planStatus.voiceAIPlanStatus === "trial" && (
                  <span className="ml-auto text-[10px] bg-amber-500/20 text-amber-300 px-1.5 py-0.5 rounded-full">Trial</span>
                )}
              </Link>
            </div>
          )}
        </nav>

        {/* Logout */}
        <div className="p-3 border-t border-slate-700">
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

"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useState, useEffect } from "react";
import { cn } from "@/lib/utils";
import { logout, getStoredBusiness } from "@/lib/auth";
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
} from "lucide-react";

const navItems = [
  { href: "/", label: "Dashboard", icon: LayoutDashboard },
  { href: "/sales", label: "Sales", icon: ShoppingCart },
  { href: "/expenses", label: "Expenses", icon: Receipt },
  { href: "/inventory", label: "Inventory", icon: Package },
  { href: "/contacts", label: "Contacts & Ledger", icon: Users },
  { href: "/reports", label: "Reports", icon: BarChart3 },
  { href: "/activity", label: "Activity", icon: Activity },
  { href: "/import", label: "Import", icon: FileUp },
  { href: "/settings", label: "Settings", icon: Settings },
];

export function Sidebar() {
  const pathname = usePathname();
  const business = getStoredBusiness();
  const [open, setOpen] = useState(false);

  // Close drawer on route change
  useEffect(() => {
    setOpen(false);
  }, [pathname]);

  return (
    <>
      {/* Mobile top bar with hamburger */}
      <div className="lg:hidden fixed top-0 left-0 right-0 z-30 flex items-center justify-between bg-slate-900 text-white px-4 py-3 border-b border-slate-700">
        <h1 className="text-lg font-black">
          Biz<span className="text-sky-400">Pilot</span>
        </h1>
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
          <h1 className="text-xl font-black">
            Biz<span className="text-sky-400">Pilot</span>
          </h1>
          {business && (
            <p className="text-xs text-slate-400 mt-1 truncate">{business.name}</p>
          )}
        </div>

        {/* Nav */}
        <nav className="flex-1 p-3 space-y-1 overflow-y-auto">
          {navItems.map(({ href, label, icon: Icon }) => {
            const active = pathname === href;
            return (
              <Link
                key={href}
                href={href}
                className={cn(
                  "flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition-colors",
                  active
                    ? "bg-sky-500 text-white"
                    : "text-slate-400 hover:bg-slate-800 hover:text-white"
                )}
              >
                <Icon size={16} />
                {label}
              </Link>
            );
          })}
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

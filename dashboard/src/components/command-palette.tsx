"use client";

import * as React from "react";
import { useRouter } from "next/navigation";
import {
  Search, LayoutDashboard, ShoppingCart, Receipt, Package, Users, BarChart3,
  Activity, ClipboardList, Settings, FileUp, Download, Phone, Plus, ArrowRight,
} from "lucide-react";

/**
 * ⌘K command palette. Linear/Stripe pattern.
 * Triggered by Cmd+K (Mac) or Ctrl+K (Windows/Linux).
 *
 * Mount at app root (already done in providers.tsx via <CommandPaletteProvider>).
 */

type CommandItem = {
  id: string;
  label: string;
  hint?: string;
  icon: React.ReactNode;
  keywords?: string;
  action: () => void;
};

type CommandSection = {
  label: string;
  items: CommandItem[];
};

export function CommandPaletteProvider({ children }: { children: React.ReactNode }) {
  const [open, setOpen] = React.useState(false);
  const router = useRouter();

  // Global ⌘K / Ctrl+K listener
  React.useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setOpen((o) => !o);
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  const close = () => setOpen(false);

  const sections: CommandSection[] = React.useMemo(() => [
    {
      label: "Quick actions",
      items: [
        { id: "new-sale", label: "Record a sale", hint: "/sales?new=1", keywords: "add new record sale transaction", icon: <Plus size={14} />, action: () => { router.push("/sales?new=1"); close(); } },
        { id: "new-expense", label: "Add expense", hint: "/expenses?new=1", keywords: "add new expense cost", icon: <Plus size={14} />, action: () => { router.push("/expenses?new=1"); close(); } },
        { id: "new-product", label: "Add product", hint: "/inventory?new=1", keywords: "add new product item stock", icon: <Plus size={14} />, action: () => { router.push("/inventory?new=1"); close(); } },
        { id: "new-contact", label: "Add contact", hint: "/contacts?new=1", keywords: "add new customer supplier contact", icon: <Plus size={14} />, action: () => { router.push("/contacts?new=1"); close(); } },
      ],
    },
    {
      label: "Pages",
      items: [
        { id: "p-dashboard", label: "Dashboard", hint: "/", icon: <LayoutDashboard size={14} />, action: () => { router.push("/"); close(); } },
        { id: "p-sales", label: "Sales", hint: "/sales", icon: <ShoppingCart size={14} />, action: () => { router.push("/sales"); close(); } },
        { id: "p-expenses", label: "Expenses", hint: "/expenses", icon: <Receipt size={14} />, action: () => { router.push("/expenses"); close(); } },
        { id: "p-inventory", label: "Inventory", hint: "/inventory", icon: <Package size={14} />, action: () => { router.push("/inventory"); close(); } },
        { id: "p-reservations", label: "Reservations", hint: "/reservations", icon: <ClipboardList size={14} />, action: () => { router.push("/reservations"); close(); } },
        { id: "p-contacts", label: "Contacts", hint: "/contacts", icon: <Users size={14} />, keywords: "customers suppliers debtors", action: () => { router.push("/contacts"); close(); } },
        { id: "p-reports", label: "Reports", hint: "/reports", icon: <BarChart3 size={14} />, action: () => { router.push("/reports"); close(); } },
        { id: "p-activity", label: "Activity", hint: "/activity", icon: <Activity size={14} />, action: () => { router.push("/activity"); close(); } },
        { id: "p-import", label: "Import", hint: "/import", icon: <FileUp size={14} />, action: () => { router.push("/import"); close(); } },
        { id: "p-export", label: "Export", hint: "/export", icon: <Download size={14} />, action: () => { router.push("/export"); close(); } },
        { id: "p-settings", label: "Settings", hint: "/settings", icon: <Settings size={14} />, action: () => { router.push("/settings"); close(); } },
        { id: "p-voice-ai", label: "Voice AI", hint: "/voice-ai", icon: <Phone size={14} />, action: () => { router.push("/voice-ai"); close(); } },
      ],
    },
    {
      label: "Settings shortcuts",
      items: [
        { id: "s-business", label: "Settings: Business", hint: "/settings#business", icon: <Settings size={14} />, action: () => { router.push("/settings#business"); close(); } },
        { id: "s-receipts", label: "Settings: Receipts", hint: "/settings#receipts", keywords: "vat tax tin receipt template", icon: <Settings size={14} />, action: () => { router.push("/settings#receipts"); close(); } },
        { id: "s-plan", label: "Settings: Plan & Billing", hint: "/settings#plan", icon: <Settings size={14} />, action: () => { router.push("/settings#plan"); close(); } },
        { id: "s-team", label: "Settings: Team", hint: "/settings#team", icon: <Settings size={14} />, action: () => { router.push("/settings#team"); close(); } },
      ],
    },
  ], [router]);

  return (
    <>
      {children}
      {open && <CommandPaletteUI sections={sections} onClose={close} />}
    </>
  );
}

function CommandPaletteUI({ sections, onClose }: { sections: CommandSection[]; onClose: () => void }) {
  const [query, setQuery] = React.useState("");
  const [selectedIdx, setSelectedIdx] = React.useState(0);
  const inputRef = React.useRef<HTMLInputElement>(null);

  // Flatten + filter
  const filtered = React.useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return sections;
    return sections
      .map((sec) => ({
        ...sec,
        items: sec.items.filter((item) =>
          (item.label + " " + (item.keywords ?? "") + " " + (item.hint ?? "")).toLowerCase().includes(q)
        ),
      }))
      .filter((sec) => sec.items.length > 0);
  }, [sections, query]);

  const flatItems = React.useMemo(() => filtered.flatMap((s) => s.items), [filtered]);

  React.useEffect(() => {
    setSelectedIdx(0);
  }, [query]);

  React.useEffect(() => {
    inputRef.current?.focus();
  }, []);

  React.useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        onClose();
      } else if (e.key === "ArrowDown") {
        e.preventDefault();
        setSelectedIdx((i) => Math.min(i + 1, flatItems.length - 1));
      } else if (e.key === "ArrowUp") {
        e.preventDefault();
        setSelectedIdx((i) => Math.max(i - 1, 0));
      } else if (e.key === "Enter") {
        e.preventDefault();
        flatItems[selectedIdx]?.action();
      }
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [flatItems, selectedIdx, onClose]);

  return (
    <div
      className="fixed inset-0 z-[200] flex items-start justify-center pt-[12vh] px-4"
      role="dialog"
      aria-modal="true"
    >
      <div className="absolute inset-0 bg-black/40 backdrop-blur-sm" onClick={onClose} aria-hidden="true" />
      <div className="relative w-full max-w-xl bg-white rounded-xl shadow-2xl ring-1 ring-slate-200 overflow-hidden animate-in fade-in slide-in-from-top-4 duration-150">
        {/* Search */}
        <div className="flex items-center gap-3 px-4 border-b border-slate-200">
          <Search size={16} className="text-slate-400" />
          <input
            ref={inputRef}
            type="search"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Search pages, actions, settings…"
            className="flex-1 h-12 bg-transparent outline-none text-sm placeholder:text-slate-400"
          />
          <kbd className="text-[10px] font-semibold text-slate-400 bg-slate-100 px-1.5 py-0.5 rounded border border-slate-200">
            ESC
          </kbd>
        </div>

        {/* Results */}
        <div className="max-h-[60vh] overflow-y-auto py-2">
          {flatItems.length === 0 ? (
            <div className="text-center py-10 text-sm text-slate-400">
              No matches for &ldquo;{query}&rdquo;
            </div>
          ) : (
            (() => {
              let runningIdx = -1;
              return filtered.map((sec) => (
                <div key={sec.label} className="mb-1">
                  <p className="px-4 pt-2 pb-1 text-[10px] font-semibold uppercase tracking-wider text-slate-400">
                    {sec.label}
                  </p>
                  {sec.items.map((item) => {
                    runningIdx++;
                    const idx = runningIdx;
                    const active = idx === selectedIdx;
                    return (
                      <button
                        key={item.id}
                        onMouseEnter={() => setSelectedIdx(idx)}
                        onClick={() => item.action()}
                        className={`w-full text-left px-4 py-2 flex items-center gap-3 text-sm transition-colors ${
                          active ? "bg-slate-100 text-slate-900" : "text-slate-700 hover:bg-slate-50"
                        }`}
                      >
                        <span className={`p-1.5 rounded-md ${active ? "bg-white text-slate-700" : "text-slate-500"}`}>
                          {item.icon}
                        </span>
                        <span className="flex-1 font-medium">{item.label}</span>
                        {item.hint && (
                          <span className="text-[11px] text-slate-400 font-mono">{item.hint}</span>
                        )}
                        {active && <ArrowRight size={12} className="text-slate-400" />}
                      </button>
                    );
                  })}
                </div>
              ));
            })()
          )}
        </div>

        {/* Footer hints */}
        <div className="border-t border-slate-200 px-4 py-2 flex items-center gap-4 text-[11px] text-slate-400">
          <span className="flex items-center gap-1.5">
            <kbd className="bg-slate-100 px-1.5 py-0.5 rounded border border-slate-200 font-semibold">↑↓</kbd>
            Navigate
          </span>
          <span className="flex items-center gap-1.5">
            <kbd className="bg-slate-100 px-1.5 py-0.5 rounded border border-slate-200 font-semibold">↵</kbd>
            Select
          </span>
          <span className="ml-auto flex items-center gap-1.5">
            <kbd className="bg-slate-100 px-1.5 py-0.5 rounded border border-slate-200 font-semibold">⌘K</kbd>
            to toggle
          </span>
        </div>
      </div>
    </div>
  );
}

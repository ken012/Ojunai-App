"use client";

import * as React from "react";
import { useRouter } from "next/navigation";
import {
  Search, LayoutDashboard, ShoppingCart, Receipt, Package, Users, BarChart3,
  Activity, ClipboardList, Settings, FileUp, Download, Phone, Plus, ArrowRight, Keyboard, X as XIcon,
} from "lucide-react";

/**
 * ⌘K command palette + global hotkeys.
 * Linear/Stripe pattern. One keyboard authority for the whole app.
 *
 * Mount at app root (already done in providers.tsx).
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

// Returns true if the active focus is somewhere a single keypress shouldn't
// trigger an app-wide shortcut (typing in a field, editing a contenteditable, etc).
function isTypingInForm(): boolean {
  const el = document.activeElement;
  if (!el) return false;
  const tag = el.tagName;
  if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT") return true;
  if ((el as HTMLElement).isContentEditable) return true;
  return false;
}

export function CommandPaletteProvider({ children }: { children: React.ReactNode }) {
  const [open, setOpen] = React.useState(false);
  const [cheatSheetOpen, setCheatSheetOpen] = React.useState(false);
  const router = useRouter();

  // Track g-prefix so two-key sequences like `g s` work like Vim/Linear.
  const gPrefixRef = React.useRef<number | null>(null);

  // Global keyboard listener — handles ⌘K, single-key actions, and g-prefix nav.
  React.useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      // ⌘K / Ctrl+K toggles palette regardless of focus context
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setOpen((o) => !o);
        return;
      }

      // Bail out for any other modifier combo or typing context
      if (e.metaKey || e.ctrlKey || e.altKey) return;
      if (isTypingInForm()) return;

      const key = e.key.toLowerCase();

      // ? opens the cheat sheet (Shift+/ on US keyboards => key="?")
      if (e.key === "?") {
        e.preventDefault();
        setCheatSheetOpen(true);
        return;
      }
      // Escape closes the cheat sheet (the palette has its own listener)
      if (e.key === "Escape" && cheatSheetOpen) {
        e.preventDefault();
        setCheatSheetOpen(false);
        return;
      }

      // Two-key g-prefix: pressing `g` arms the prefix for ~1.2s.
      if (key === "g" && !gPrefixRef.current) {
        e.preventDefault();
        gPrefixRef.current = window.setTimeout(() => { gPrefixRef.current = null; }, 1200);
        return;
      }

      // If g-prefix is armed, the next key is a navigation target.
      if (gPrefixRef.current) {
        clearTimeout(gPrefixRef.current);
        gPrefixRef.current = null;
        const navMap: Record<string, string> = {
          d: "/", h: "/", t: "/",                 // (d)ashboard / (h)ome / (t)oday
          s: "/sales",
          e: "/expenses",
          i: "/inventory",
          c: "/contacts",
          r: "/reports",
          b: "/reservations",                    // (b)ookings
          v: "/voice-ai",
          a: "/activity",
          ",": "/settings",
        };
        const dest = navMap[key];
        if (dest) {
          e.preventDefault();
          router.push(dest);
        }
        return;
      }

      // Single-key action shortcuts
      switch (key) {
        case "s": e.preventDefault(); router.push("/sales?new=1"); break;
        case "e": e.preventDefault(); router.push("/expenses?new=1"); break;
        case "p": e.preventDefault(); router.push("/inventory?new=1"); break; // (p)roduct
        case "n": e.preventDefault(); router.push("/contacts?new=1"); break; // (n)ew contact
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [router, cheatSheetOpen]);

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
      label: "Today",
      items: [
        { id: "p-dashboard", label: "Today", hint: "/", keywords: "dashboard home overview", icon: <LayoutDashboard size={14} />, action: () => { router.push("/"); close(); } },
      ],
    },
    {
      label: "Pulse",
      items: [
        { id: "p-sales", label: "Sales", hint: "/sales", icon: <ShoppingCart size={14} />, action: () => { router.push("/sales"); close(); } },
        { id: "p-expenses", label: "Expenses", hint: "/expenses", icon: <Receipt size={14} />, action: () => { router.push("/expenses"); close(); } },
        { id: "p-reservations", label: "Bookings", hint: "/reservations", keywords: "reservations bookings calendar", icon: <ClipboardList size={14} />, action: () => { router.push("/reservations"); close(); } },
      ],
    },
    {
      label: "Assets",
      items: [
        { id: "p-inventory", label: "Inventory", hint: "/inventory", icon: <Package size={14} />, action: () => { router.push("/inventory"); close(); } },
        { id: "p-contacts", label: "Contacts", hint: "/contacts", icon: <Users size={14} />, keywords: "customers suppliers debtors", action: () => { router.push("/contacts"); close(); } },
      ],
    },
    {
      label: "Intelligence",
      items: [
        { id: "p-reports", label: "Reports", hint: "/reports", icon: <BarChart3 size={14} />, action: () => { router.push("/reports"); close(); } },
        { id: "p-voice-ai", label: "Voice AI", hint: "/voice-ai", icon: <Phone size={14} />, action: () => { router.push("/voice-ai"); close(); } },
      ],
    },
    {
      label: "More",
      items: [
        { id: "p-activity", label: "Activity log", hint: "/activity", keywords: "history audit feed", icon: <Activity size={14} />, action: () => { router.push("/activity"); close(); } },
        { id: "p-import", label: "Import data", hint: "/import", keywords: "csv upload bulk", icon: <FileUp size={14} />, action: () => { router.push("/import"); close(); } },
        { id: "p-export", label: "Export data", hint: "/export", keywords: "csv download backup", icon: <Download size={14} />, action: () => { router.push("/export"); close(); } },
        { id: "p-settings", label: "Settings", hint: "/settings", icon: <Settings size={14} />, action: () => { router.push("/settings"); close(); } },
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
    {
      label: "Help",
      items: [
        { id: "h-shortcuts", label: "Keyboard shortcuts", hint: "?", keywords: "hotkeys cheat sheet help shortcuts", icon: <Keyboard size={14} />, action: () => { close(); setCheatSheetOpen(true); } },
      ],
    },
  ], [router]);

  return (
    <>
      {children}
      {open && <CommandPaletteUI sections={sections} onClose={close} />}
      {cheatSheetOpen && <CheatSheet onClose={() => setCheatSheetOpen(false)} />}
    </>
  );
}

// ── Cheat sheet ─────────────────────────────────────────────────────────────
// Shown on `?`. Lists every global hotkey grouped by purpose.

const CHEATSHEET_GROUPS: { label: string; items: { keys: string[]; desc: string }[] }[] = [
  {
    label: "Quick actions",
    items: [
      { keys: ["S"], desc: "New sale" },
      { keys: ["E"], desc: "New expense" },
      { keys: ["P"], desc: "New product" },
      { keys: ["N"], desc: "New contact" },
    ],
  },
  {
    label: "Navigation",
    items: [
      { keys: ["G", "D"], desc: "Today (Dashboard)" },
      { keys: ["G", "S"], desc: "Sales" },
      { keys: ["G", "E"], desc: "Expenses" },
      { keys: ["G", "I"], desc: "Inventory" },
      { keys: ["G", "C"], desc: "Contacts" },
      { keys: ["G", "R"], desc: "Reports" },
      { keys: ["G", "B"], desc: "Bookings" },
      { keys: ["G", "V"], desc: "Voice AI" },
      { keys: ["G", "A"], desc: "Activity log" },
      { keys: ["G", ","], desc: "Settings" },
    ],
  },
  {
    label: "Global",
    items: [
      { keys: ["⌘", "K"], desc: "Command palette (search anything)" },
      { keys: ["?"], desc: "Show this cheat sheet" },
      { keys: ["Esc"], desc: "Close dialogs / drawers / panels" },
    ],
  },
];

function Kbd({ children }: { children: React.ReactNode }) {
  return (
    <kbd className="inline-flex items-center justify-center min-w-[24px] h-6 px-1.5 text-[11px] font-semibold rounded bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-300 ring-1 ring-slate-200 dark:ring-slate-700 font-mono">
      {children}
    </kbd>
  );
}

function CheatSheet({ onClose }: { onClose: () => void }) {
  return (
    <div
      className="fixed inset-0 z-[200] flex items-start justify-center pt-[10vh] px-4"
      role="dialog"
      aria-modal="true"
      aria-label="Keyboard shortcuts"
    >
      <div className="absolute inset-0 bg-black/40 backdrop-blur-sm" onClick={onClose} aria-hidden="true" />
      <div className="relative w-full max-w-2xl bg-white dark:bg-slate-900 rounded-xl shadow-2xl ring-1 ring-slate-200 dark:ring-slate-800 overflow-hidden animate-in fade-in slide-in-from-top-4 duration-150">
        <div className="flex items-center justify-between px-5 py-3 border-b border-slate-200 dark:border-slate-800">
          <div className="flex items-center gap-2">
            <Keyboard size={16} className="text-cyan-500" />
            <h2 className="text-sm font-semibold text-slate-900 dark:text-slate-50">Keyboard shortcuts</h2>
          </div>
          <button
            onClick={onClose}
            className="p-1 rounded hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-400 dark:text-slate-500 hover:text-slate-700 dark:hover:text-slate-300"
            aria-label="Close"
          >
            <XIcon size={16} />
          </button>
        </div>
        <div className="px-5 py-4 max-h-[70vh] overflow-y-auto">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-8 gap-y-5">
            {CHEATSHEET_GROUPS.map((group) => (
              <div key={group.label}>
                <p className="text-[10px] font-semibold uppercase tracking-wider text-slate-400 dark:text-slate-500 mb-2">
                  {group.label}
                </p>
                <div className="space-y-1.5">
                  {group.items.map((item, i) => (
                    <div key={i} className="flex items-center justify-between gap-3 text-sm">
                      <span className="text-slate-700 dark:text-slate-300">{item.desc}</span>
                      <span className="flex items-center gap-1 flex-shrink-0">
                        {item.keys.map((k, j) => (
                          <Kbd key={j}>{k}</Kbd>
                        ))}
                      </span>
                    </div>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </div>
        <div className="px-5 py-2.5 border-t border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-950/50 text-[11px] text-slate-500 dark:text-slate-400">
          Tip: shortcuts only fire when you&rsquo;re not typing in a field.
          Press <Kbd>?</Kbd> any time to open this sheet.
        </div>
      </div>
    </div>
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
      <div className="relative w-full max-w-xl bg-white dark:bg-slate-900 rounded-xl shadow-2xl ring-1 ring-slate-200 overflow-hidden animate-in fade-in slide-in-from-top-4 duration-150">
        {/* Search */}
        <div className="flex items-center gap-3 px-4 border-b border-slate-200 dark:border-slate-800">
          <Search size={16} className="text-slate-400 dark:text-slate-500" />
          <input
            ref={inputRef}
            type="search"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Search pages, actions, settings…"
            className="flex-1 h-12 bg-transparent outline-none text-sm placeholder:text-slate-400"
          />
          <kbd className="text-[10px] font-semibold text-slate-400 dark:text-slate-500 bg-slate-100 dark:bg-slate-800 px-1.5 py-0.5 rounded border border-slate-200 dark:border-slate-800">
            ESC
          </kbd>
        </div>

        {/* Results */}
        <div className="max-h-[60vh] overflow-y-auto py-2">
          {flatItems.length === 0 ? (
            <div className="text-center py-10 text-sm text-slate-400 dark:text-slate-500">
              No matches for &ldquo;{query}&rdquo;
            </div>
          ) : (
            (() => {
              let runningIdx = -1;
              return filtered.map((sec) => (
                <div key={sec.label} className="mb-1">
                  <p className="px-4 pt-2 pb-1 text-[10px] font-semibold uppercase tracking-wider text-slate-400 dark:text-slate-500">
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
                          active ? "bg-slate-100 dark:bg-slate-800 text-slate-900 dark:text-slate-50" : "text-slate-700 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-800"
                        }`}
                      >
                        <span className={`p-1.5 rounded-md ${active ? "bg-white dark:bg-slate-900 text-slate-700 dark:text-slate-300" : "text-slate-500 dark:text-slate-400"}`}>
                          {item.icon}
                        </span>
                        <span className="flex-1 font-medium">{item.label}</span>
                        {item.hint && (
                          <span className="text-[11px] text-slate-400 dark:text-slate-500 font-mono">{item.hint}</span>
                        )}
                        {active && <ArrowRight size={12} className="text-slate-400 dark:text-slate-500" />}
                      </button>
                    );
                  })}
                </div>
              ));
            })()
          )}
        </div>

        {/* Footer hints */}
        <div className="border-t border-slate-200 dark:border-slate-800 px-4 py-2 flex items-center gap-4 text-[11px] text-slate-400 dark:text-slate-500">
          <span className="flex items-center gap-1.5">
            <kbd className="bg-slate-100 dark:bg-slate-800 px-1.5 py-0.5 rounded border border-slate-200 dark:border-slate-800 font-semibold">↑↓</kbd>
            Navigate
          </span>
          <span className="flex items-center gap-1.5">
            <kbd className="bg-slate-100 dark:bg-slate-800 px-1.5 py-0.5 rounded border border-slate-200 dark:border-slate-800 font-semibold">↵</kbd>
            Select
          </span>
          <span className="flex items-center gap-1.5">
            <kbd className="bg-slate-100 dark:bg-slate-800 px-1.5 py-0.5 rounded border border-slate-200 dark:border-slate-800 font-semibold">?</kbd>
            Shortcuts
          </span>
          <span className="ml-auto flex items-center gap-1.5">
            <kbd className="bg-slate-100 dark:bg-slate-800 px-1.5 py-0.5 rounded border border-slate-200 dark:border-slate-800 font-semibold">⌘K</kbd>
            to toggle
          </span>
        </div>
      </div>
    </div>
  );
}

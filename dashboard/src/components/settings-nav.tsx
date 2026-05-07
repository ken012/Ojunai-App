"use client";

import { useEffect, useState } from "react";
import { cn } from "@/lib/utils";

interface NavItem {
  href: string;   // "#section-id"
  label: string;
}

/**
 * Sticky left-rail nav for the Settings page on desktop. Hidden on mobile (the
 * accordion takes over there). Highlights the section currently in view by
 * watching IntersectionObserver entries on each section anchor.
 */
export function SettingsNav({ items }: { items: NavItem[] }) {
  const [activeId, setActiveId] = useState<string | null>(null);

  useEffect(() => {
    if (typeof window === "undefined") return;

    // Find each section element by id (strip the leading '#').
    const sections = items
      .map((it) => document.getElementById(it.href.slice(1)))
      .filter((el): el is HTMLElement => el !== null);
    if (sections.length === 0) return;

    // Pick the section whose top is closest to the viewport's top edge while still
    // visible. rootMargin biases the trigger to a band near the top so the active
    // entry advances as soon as the next section's heading reaches that band.
    const observer = new IntersectionObserver(
      (entries) => {
        // Take all currently-intersecting entries and choose the topmost one.
        const visible = entries
          .filter((e) => e.isIntersecting)
          .sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top);
        if (visible.length > 0) {
          setActiveId(visible[0].target.id);
        }
      },
      {
        // Trigger when the section's top crosses ~30% down from the viewport top.
        rootMargin: "0px 0px -70% 0px",
        threshold: 0,
      },
    );

    sections.forEach((s) => observer.observe(s));
    return () => observer.disconnect();
  }, [items]);

  return (
    <aside className="hidden lg:block">
      <nav className="sticky top-6 space-y-0.5 text-sm">
        {items.map((item) => {
          const isActive = activeId === item.href.slice(1);
          return (
            <a
              key={item.href}
              href={item.href}
              className={cn(
                "block px-3 py-1.5 rounded-md transition-colors",
                isActive
                  ? "bg-cyan-50 text-cyan-700 font-medium dark:bg-cyan-950/40 dark:text-cyan-300"
                  : "text-slate-600 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800 hover:text-slate-900 dark:hover:text-slate-50",
              )}
            >
              {item.label}
            </a>
          );
        })}
      </nav>
    </aside>
  );
}

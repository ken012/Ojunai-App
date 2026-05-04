"use client";

import * as React from "react";
import { X } from "lucide-react";
import { cn } from "@/lib/utils";

/**
 * Right-side drawer (Sheet pattern).
 * Slides in from the right; backdrop closes on click; Esc closes.
 * Used for contact profiles, sale details, etc. — Linear/Stripe pattern.
 */

export function Drawer({
  open,
  onClose,
  children,
  width = "md",
}: {
  open: boolean;
  onClose: () => void;
  children: React.ReactNode;
  width?: "sm" | "md" | "lg";
}) {
  // Esc to close
  React.useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  // Lock body scroll while open
  React.useEffect(() => {
    if (!open) return;
    const original = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      document.body.style.overflow = original;
    };
  }, [open]);

  if (!open) return null;

  const widthClass =
    width === "sm" ? "max-w-md" : width === "lg" ? "max-w-2xl" : "max-w-xl";

  return (
    <div className="fixed inset-0 z-50" role="dialog" aria-modal="true">
      {/* Backdrop */}
      <div
        className="absolute inset-0 bg-black/40 transition-opacity"
        onClick={onClose}
        aria-hidden="true"
      />
      {/* Panel */}
      <div
        className={cn(
          "absolute right-0 top-0 h-full w-full bg-white shadow-2xl flex flex-col",
          "animate-in slide-in-from-right duration-200",
          widthClass
        )}
      >
        {children}
      </div>
    </div>
  );
}

export function DrawerHeader({
  title,
  subtitle,
  onClose,
  actions,
}: {
  title: string;
  subtitle?: string;
  onClose: () => void;
  actions?: React.ReactNode;
}) {
  return (
    <div className="flex items-start justify-between gap-3 px-6 py-4 border-b border-slate-200 flex-shrink-0">
      <div className="min-w-0 flex-1">
        <h2 className="text-lg font-bold text-slate-900 truncate">{title}</h2>
        {subtitle && <p className="text-sm text-slate-500 mt-0.5 truncate">{subtitle}</p>}
      </div>
      <div className="flex items-center gap-2 flex-shrink-0">
        {actions}
        <button
          onClick={onClose}
          className="p-1.5 rounded-md text-slate-400 hover:text-slate-700 hover:bg-slate-100 transition-colors"
          aria-label="Close"
        >
          <X size={18} />
        </button>
      </div>
    </div>
  );
}

export function DrawerBody({ children, className }: { children: React.ReactNode; className?: string }) {
  return <div className={cn("flex-1 overflow-y-auto px-6 py-5", className)}>{children}</div>;
}

export function DrawerFooter({ children, className }: { children: React.ReactNode; className?: string }) {
  return (
    <div className={cn("flex items-center justify-end gap-2 px-6 py-3 border-t border-slate-200 bg-slate-50/50 flex-shrink-0", className)}>
      {children}
    </div>
  );
}

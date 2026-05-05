"use client";

import * as React from "react";
import { CheckCircle2, AlertTriangle, Info, X } from "lucide-react";

/**
 * Lightweight toast system. No deps.
 *   const { toast } = useToast();
 *   toast.success("Saved");
 *   toast.error("Failed to save");
 *   toast.info("Heads up");
 *
 * Wrap app in <Toaster /> at layout root.
 */

type ToastVariant = "success" | "error" | "info";

type Toast = {
  id: number;
  variant: ToastVariant;
  title: string;
  description?: string;
};

type ToastContextValue = {
  push: (variant: ToastVariant, title: string, description?: string) => void;
};

const ToastContext = React.createContext<ToastContextValue | null>(null);

export function Toaster({ children }: { children?: React.ReactNode }) {
  const [toasts, setToasts] = React.useState<Toast[]>([]);
  const idRef = React.useRef(0);

  const push = React.useCallback((variant: ToastVariant, title: string, description?: string) => {
    const id = ++idRef.current;
    setToasts((curr) => [...curr, { id, variant, title, description }]);
    // Auto-dismiss after 4s for success/info, 6s for error
    setTimeout(() => {
      setToasts((curr) => curr.filter((t) => t.id !== id));
    }, variant === "error" ? 6000 : 4000);
  }, []);

  const dismiss = (id: number) => setToasts((curr) => curr.filter((t) => t.id !== id));

  return (
    <ToastContext.Provider value={{ push }}>
      {children}
      <div className="fixed top-4 right-4 z-[100] flex flex-col gap-2 pointer-events-none">
        {toasts.map((t) => (
          <ToastItem key={t.id} toast={t} onDismiss={() => dismiss(t.id)} />
        ))}
      </div>
    </ToastContext.Provider>
  );
}

function ToastItem({ toast, onDismiss }: { toast: Toast; onDismiss: () => void }) {
  const { variant, title, description } = toast;
  const styles = variant === "success"
    ? { icon: <CheckCircle2 size={18} className="text-emerald-600" />, ring: "ring-emerald-200" }
    : variant === "error"
    ? { icon: <AlertTriangle size={18} className="text-red-600" />, ring: "ring-red-200" }
    : { icon: <Info size={18} className="text-cyan-600" />, ring: "ring-cyan-200" };

  return (
    <div
      role="status"
      className={`pointer-events-auto bg-white shadow-lg rounded-xl ring-1 ${styles.ring} px-4 py-3 min-w-[280px] max-w-md flex items-start gap-3 animate-in slide-in-from-right-2 fade-in duration-200`}
    >
      <div className="flex-shrink-0 mt-0.5">{styles.icon}</div>
      <div className="flex-1 min-w-0">
        <p className="text-sm font-semibold text-slate-900">{title}</p>
        {description && <p className="text-xs text-slate-500 mt-0.5">{description}</p>}
      </div>
      <button
        onClick={onDismiss}
        className="flex-shrink-0 text-slate-400 hover:text-slate-700 transition-colors p-0.5 rounded"
        aria-label="Dismiss"
      >
        <X size={14} />
      </button>
    </div>
  );
}

export function useToast() {
  const ctx = React.useContext(ToastContext);
  if (!ctx) {
    // No-op fallback — useful in tests or before Toaster mounts
    return {
      toast: {
        success: () => {},
        error: () => {},
        info: () => {},
      },
    };
  }
  return {
    toast: {
      success: (title: string, description?: string) => ctx.push("success", title, description),
      error: (title: string, description?: string) => ctx.push("error", title, description),
      info: (title: string, description?: string) => ctx.push("info", title, description),
    },
  };
}

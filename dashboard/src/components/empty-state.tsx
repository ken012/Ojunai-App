import { ReactNode } from "react";

/**
 * Consistent empty-state pattern.
 * Use for: empty lists, no-data tables, blank dashboards.
 */
export function EmptyState({
  icon,
  title,
  description,
  action,
  compact = false,
}: {
  icon?: ReactNode;
  title: string;
  description?: string;
  action?: ReactNode;
  compact?: boolean;
}) {
  return (
    <div
      className={`flex flex-col items-center justify-center text-center ${
        compact ? "py-6" : "py-12"
      }`}
    >
      {icon && (
        <div
          className={`${
            compact ? "h-10 w-10" : "h-14 w-14"
          } rounded-full bg-slate-100 flex items-center justify-center text-slate-400 mb-3`}
        >
          {icon}
        </div>
      )}
      <p className={`${compact ? "text-sm" : "text-base"} font-semibold text-slate-700`}>
        {title}
      </p>
      {description && (
        <p className="text-xs text-slate-500 mt-1 max-w-sm">{description}</p>
      )}
      {action && <div className="mt-4">{action}</div>}
    </div>
  );
}

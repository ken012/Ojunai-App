import { MessageCircle, User, FileUp } from "lucide-react";

export function SourceBadge({ source }: { source?: string }) {
  if (source === "WhatsApp") {
    return (
      <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-emerald-50 text-emerald-700 border border-emerald-200" title="Recorded via WhatsApp">
        <MessageCircle size={11} /> WhatsApp
      </span>
    );
  }

  if (source === "Import") {
    return (
      <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-violet-50 text-violet-700 border border-violet-200" title="Imported from CSV">
        <FileUp size={11} /> CSV Import
      </span>
    );
  }

  return (
    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-slate-100 text-slate-600 border border-slate-200" title="Entered manually">
      <User size={11} /> Manual
    </span>
  );
}

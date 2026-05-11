import { MessageCircle, Send, MessageSquare, User, FileUp, Mic, LayoutDashboard } from "lucide-react";

/**
 * Renders an attribution badge for where a record originated. Source strings match the
 * EntrySource constants on the API (WhatsApp, Telegram, Messenger, Dashboard, Voice, Import,
 * Manual). "Manual" is treated as legacy-Dashboard for display purposes — older records
 * created before we split the constant still show with the Dashboard styling.
 */
export function SourceBadge({ source }: { source?: string }) {
  if (source === "WhatsApp") {
    return (
      <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-emerald-50 text-emerald-700 border border-emerald-200" title="Recorded via WhatsApp">
        <MessageCircle size={11} /> WhatsApp
      </span>
    );
  }

  if (source === "Telegram") {
    return (
      <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-sky-50 text-sky-700 border border-sky-200" title="Recorded via Telegram">
        <Send size={11} /> Telegram
      </span>
    );
  }

  if (source === "Messenger") {
    return (
      <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-blue-50 text-blue-700 border border-blue-200" title="Recorded via Facebook Messenger">
        <MessageSquare size={11} /> Messenger
      </span>
    );
  }

  if (source === "Voice") {
    return (
      <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-amber-50 text-amber-700 border border-amber-200" title="Recorded via Voice AI">
        <Mic size={11} /> Voice
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

  if (source === "Dashboard") {
    return (
      <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-300 border border-slate-200 dark:border-slate-700" title="Entered through the dashboard">
        <LayoutDashboard size={11} /> Dashboard
      </span>
    );
  }

  // Legacy "Manual" source (pre-Dashboard rename) plus any unknown value fall through here.
  return (
    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400 border border-slate-200 dark:border-slate-800" title="Entered manually">
      <User size={11} /> {source === "Manual" ? "Dashboard" : source ?? "Manual"}
    </span>
  );
}

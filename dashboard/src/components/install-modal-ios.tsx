"use client";

import { Share, Plus, Smartphone } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";

export function InstallModalIos({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (next: boolean) => void;
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-sm">
        <DialogHeader>
          <DialogTitle>Install Ojunai on your iPhone</DialogTitle>
          <DialogDescription>Three steps. Takes about 10 seconds.</DialogDescription>
        </DialogHeader>

        <ol className="mt-2 space-y-4">
          <Step
            n={1}
            icon={<Share className="h-5 w-5" aria-hidden />}
            title="Tap the Share button"
            body="It's at the bottom of Safari (a square with an arrow pointing up)."
          />
          <Step
            n={2}
            icon={<Plus className="h-5 w-5" aria-hidden />}
            title="Choose Add to Home Screen"
            body="Scroll down in the share sheet if you don't see it right away."
          />
          <Step
            n={3}
            icon={<Smartphone className="h-5 w-5" aria-hidden />}
            title="Tap Add"
            body="The Ojunai icon appears on your home screen. Open it from there for the full app experience."
          />
        </ol>

        <p className="mt-2 text-xs text-muted-foreground">
          Tip: this only works in Safari. If you&apos;re in Chrome or another browser, copy this page&apos;s link
          and open it in Safari first.
        </p>
      </DialogContent>
    </Dialog>
  );
}

function Step({
  n,
  icon,
  title,
  body,
}: {
  n: number;
  icon: React.ReactNode;
  title: string;
  body: string;
}) {
  return (
    <li className="flex gap-3">
      <div className="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-cyan-500 to-violet-500 text-xs font-bold text-white">
        {n}
      </div>
      <div className="flex-1 pt-0.5">
        <div className="flex items-center gap-2 font-medium">
          <span className="text-foreground/70">{icon}</span>
          {title}
        </div>
        <p className="mt-0.5 text-sm text-muted-foreground">{body}</p>
      </div>
    </li>
  );
}

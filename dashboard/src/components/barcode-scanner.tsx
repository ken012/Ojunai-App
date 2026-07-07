"use client";

import { useEffect, useRef, useState } from "react";
import { BrowserMultiFormatReader } from "@zxing/library";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { X } from "lucide-react";

/**
 * Camera barcode scanner in a modal. Opens the device camera, continuously decodes 1D barcodes
 * (EAN/UPC/Code-128 etc.) and calls onScan with the first code found, then closes. Fully cleans
 * up the camera stream on close/unmount. Additive — used by the product form and PO create flow.
 */
export function BarcodeScanner({ open, onClose, onScan }: {
  open: boolean;
  onClose: () => void;
  onScan: (code: string) => void;
}) {
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const readerRef = useRef<BrowserMultiFormatReader | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    setError(null);
    const reader = new BrowserMultiFormatReader();
    readerRef.current = reader;

    reader
      .decodeFromVideoDevice(null, videoRef.current!, (result) => {
        if (result && !cancelled) {
          const text = result.getText();
          if (text) {
            onScan(text);
            onClose();
          }
        }
      })
      .catch((e: unknown) => {
        if (cancelled) return;
        const name = (e as { name?: string })?.name;
        setError(
          name === "NotAllowedError"
            ? "Camera permission denied. Allow camera access, or type the barcode manually."
            : name === "NotFoundError"
              ? "No camera found on this device. Type the barcode manually."
              : "Couldn't start the camera. Type the barcode manually."
        );
      });

    return () => {
      cancelled = true;
      try { reader.reset(); } catch { /* already stopped */ }
      readerRef.current = null;
    };
  }, [open, onScan, onClose]);

  return (
    <Dialog open={open} onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-w-sm">
        <DialogHeader>
          <DialogTitle>Scan barcode</DialogTitle>
        </DialogHeader>
        {error ? (
          <p className="text-sm text-rose-500 py-4">{error}</p>
        ) : (
          <div className="relative overflow-hidden rounded-lg bg-black aspect-[4/3]">
            {/* eslint-disable-next-line jsx-a11y/media-has-caption */}
            <video ref={videoRef} className="w-full h-full object-cover" />
            <div className="pointer-events-none absolute inset-x-6 top-1/2 h-0.5 -translate-y-1/2 bg-rose-500/70" />
          </div>
        )}
        <p className="text-[11px] text-slate-400 dark:text-slate-500">
          Point the camera at the product barcode. It scans automatically.
        </p>
        <button
          type="button"
          onClick={onClose}
          className="absolute right-3 top-3 text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"
          aria-label="Close scanner"
        >
          <X size={18} />
        </button>
      </DialogContent>
    </Dialog>
  );
}

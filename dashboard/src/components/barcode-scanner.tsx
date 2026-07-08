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

  // Keep the latest callbacks in refs so the camera effect depends ONLY on `open`. Without this the
  // effect re-ran on every parent render (onScan/onClose are new identities each time), tearing down
  // and restarting the camera constantly → a black, never-decoding feed even though the stream was live.
  const onScanRef = useRef(onScan);
  const onCloseRef = useRef(onClose);
  useEffect(() => { onScanRef.current = onScan; onCloseRef.current = onClose; });

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    let stream: MediaStream | null = null;
    setError(null);
    const reader = new BrowserMultiFormatReader();
    readerRef.current = reader;
    const video = videoRef.current;

    // Acquire the camera OURSELVES (rear-facing) and drive the <video> directly, then let ZXing
    // decode from the playing element. Doing the getUserMedia + srcObject + play() by hand — rather
    // than letting the library manage it — is what makes the feed actually render on mobile Safari/
    // Chrome, where the library's own path left the video black.
    (async () => {
      try {
        if (!navigator.mediaDevices?.getUserMedia) {
          setError("This browser can't access the camera. Type the barcode manually.");
          return;
        }
        // Force the REAR camera on phones. `exact` guarantees it (front cam can't read a barcode
        // the user points at a product). If the device has no rear camera (e.g. a laptop),
        // getUserMedia throws OverconstrainedError → fall back to any available camera.
        try {
          stream = await navigator.mediaDevices.getUserMedia({
            video: { facingMode: { exact: "environment" } },
            audio: false,
          });
        } catch (e: unknown) {
          const n = (e as { name?: string })?.name;
          if (n === "OverconstrainedError" || n === "NotFoundError") {
            stream = await navigator.mediaDevices.getUserMedia({ video: true, audio: false });
          } else {
            throw e;
          }
        }
        if (cancelled || !video) { stream.getTracks().forEach((t) => t.stop()); return; }

        video.srcObject = stream;
        video.setAttribute("playsinline", "true"); // belt-and-suspenders for older iOS
        await video.play().catch(() => { /* autoplay policy; frames still flow */ });

        reader.decodeContinuously(video, (result) => {
          if (result && !cancelled) {
            const text = result.getText();
            if (text) { onScanRef.current(text); onCloseRef.current(); }
          }
        });
      } catch (e: unknown) {
        if (cancelled) return;
        const name = (e as { name?: string })?.name;
        setError(
          name === "NotAllowedError"
            ? "Camera permission denied. Allow camera access, or type the barcode manually."
            : name === "NotFoundError"
              ? "No camera found on this device. Type the barcode manually."
              : "Couldn't start the camera. Type the barcode manually."
        );
      }
    })();

    return () => {
      cancelled = true;
      try { reader.stopContinuousDecode(); } catch { /* no-op */ }
      try { reader.reset(); } catch { /* already stopped */ }
      if (stream) stream.getTracks().forEach((t) => t.stop());
      if (video) video.srcObject = null;
      readerRef.current = null;
    };
  }, [open]);

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
            {/* autoPlay + muted + playsInline are REQUIRED for iOS Safari to show a camera stream
                inline — without them the video stays black and never decodes. */}
            {/* eslint-disable-next-line jsx-a11y/media-has-caption */}
            <video ref={videoRef} autoPlay muted playsInline className="w-full h-full object-cover" />
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

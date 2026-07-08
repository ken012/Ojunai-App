"use client";

import { useEffect, useRef, useState } from "react";
import { BrowserMultiFormatReader } from "@zxing/library";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { X } from "lucide-react";

/**
 * Camera barcode scanner in a modal. Drives the camera by hand (getUserMedia → attach to <video> →
 * play → decodeContinuously) rather than via the library's browser helper, which left the feed black
 * on phones. Shows a live status line so failures are observable, not guessed.
 */
export function BarcodeScanner({ open, onClose, onScan }: {
  open: boolean;
  onClose: () => void;
  onScan: (code: string) => void;
}) {
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const readerRef = useRef<BrowserMultiFormatReader | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState<string>("Starting…");

  const onScanRef = useRef(onScan);
  const onCloseRef = useRef(onClose);
  useEffect(() => { onScanRef.current = onScan; onCloseRef.current = onClose; });

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    let stream: MediaStream | null = null;
    setError(null);
    setStatus("Requesting camera…");
    const reader = new BrowserMultiFormatReader();
    readerRef.current = reader;

    async function waitForVideoEl(tries = 20): Promise<HTMLVideoElement | null> {
      for (let i = 0; i < tries; i++) {
        if (videoRef.current) return videoRef.current;
        await new Promise((r) => requestAnimationFrame(r));
      }
      return videoRef.current;
    }

    (async () => {
      try {
        if (!navigator.mediaDevices?.getUserMedia) {
          setError("This browser can't access the camera (needs HTTPS + a supported browser). Type the barcode instead.");
          return;
        }

        // Force the REAR camera; fall back to any camera only if there's no rear one (laptops).
        try {
          stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { exact: "environment" } }, audio: false });
        } catch (e: unknown) {
          const n = (e as { name?: string })?.name;
          if (n === "OverconstrainedError" || n === "NotFoundError") {
            stream = await navigator.mediaDevices.getUserMedia({ video: true, audio: false });
          } else { throw e; }
        }
        if (cancelled) { stream.getTracks().forEach((t) => t.stop()); return; }

        const track = stream.getVideoTracks()[0];
        setStatus(`Camera: ${track?.label || "on"} — starting video…`);

        const video = await waitForVideoEl();
        if (cancelled || !video) { stream.getTracks().forEach((t) => t.stop()); return; }

        video.setAttribute("playsinline", "true");
        video.muted = true;
        video.srcObject = stream;

        video.onloadedmetadata = () => setStatus(`Video ${video.videoWidth}×${video.videoHeight} — playing…`);
        video.onplaying = () => setStatus(`Live · ${video.videoWidth}×${video.videoHeight} · point at a barcode`);

        try { await video.play(); }
        catch { setStatus("Tap the video if it stays black (autoplay blocked)."); }

        reader.decodeContinuously(video, (result) => {
          if (result && !cancelled) {
            const text = result.getText();
            if (text) { onScanRef.current(text); onCloseRef.current(); }
          }
        });
      } catch (e: unknown) {
        if (cancelled) return;
        const name = (e as { name?: string })?.name;
        const msg = (e as { message?: string })?.message;
        setError(
          name === "NotAllowedError"
            ? "Camera permission denied. Allow camera access in your browser settings, or type the barcode."
            : name === "NotFoundError"
              ? "No camera found. Type the barcode manually."
              : `Couldn't start the camera (${name || msg || "unknown"}). Type the barcode manually.`
        );
      }
    })();

    return () => {
      cancelled = true;
      try { reader.stopContinuousDecode(); } catch { /* no-op */ }
      try { reader.reset(); } catch { /* no-op */ }
      if (stream) stream.getTracks().forEach((t) => t.stop());
      if (videoRef.current) videoRef.current.srcObject = null;
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
          <div className="relative overflow-hidden rounded-lg bg-black aspect-[3/4] sm:aspect-[4/3]">
            {/* eslint-disable-next-line jsx-a11y/media-has-caption */}
            <video
              ref={videoRef}
              autoPlay
              muted
              playsInline
              onClick={() => videoRef.current?.play?.().catch(() => {})}
              className="absolute inset-0 w-full h-full object-cover"
            />
            <div className="pointer-events-none absolute inset-x-6 top-1/2 h-0.5 -translate-y-1/2 bg-rose-500/70" />
            <p className="absolute bottom-1 inset-x-0 text-center text-[10px] text-white/80 px-2 truncate">{status}</p>
          </div>
        )}
        <p className="text-[11px] text-slate-400 dark:text-slate-500">
          Point the rear camera at the product barcode — it scans automatically.
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

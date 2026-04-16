"use client";

import Link from "next/link";

const WHATSAPP_NUMBER = process.env.NEXT_PUBLIC_WHATSAPP_NUMBER || "+1 415 523 8886";
const SANDBOX_JOIN_CODE = process.env.NEXT_PUBLIC_TWILIO_JOIN_CODE || "join <your-code>";
const WA_LINK = `https://wa.me/${WHATSAPP_NUMBER.replace(/[^0-9]/g, "")}?text=${encodeURIComponent(SANDBOX_JOIN_CODE)}`;

export default function GetStartedPage() {
  return (
    <div className="max-w-2xl mx-auto p-6 space-y-6">
      <div>
        <h1 className="text-3xl font-bold">Get started</h1>
        <p className="text-muted-foreground mt-1">
          Run your business by chatting on WhatsApp. Three steps to your first sale.
        </p>
      </div>

      <ol className="space-y-4">
        <li className="border rounded-lg p-4">
          <div className="font-semibold">Step 1 — Connect WhatsApp</div>
          <p className="text-sm text-muted-foreground mt-1">
            Send <code className="bg-muted px-1 rounded">{SANDBOX_JOIN_CODE}</code> to{" "}
            <strong>{WHATSAPP_NUMBER}</strong> on WhatsApp.
          </p>
          <a
            href={WA_LINK}
            target="_blank"
            rel="noreferrer"
            className="inline-block mt-3 px-4 py-2 bg-green-600 text-white rounded font-medium"
          >
            Open WhatsApp
          </a>
        </li>

        <li className="border rounded-lg p-4">
          <div className="font-semibold">Step 2 — Record your first sale</div>
          <p className="text-sm text-muted-foreground mt-1">
            Reply with something like:
          </p>
          <pre className="bg-muted p-3 rounded mt-2 text-sm">Sold 5 bags of rice for ₦25000</pre>
          <p className="text-sm text-muted-foreground mt-2">
            You will get a confirmation message back within seconds.
          </p>
        </li>

        <li className="border rounded-lg p-4">
          <div className="font-semibold">Step 3 — View your dashboard</div>
          <p className="text-sm text-muted-foreground mt-1">
            Refresh your dashboard. The sale, the inventory change, and today&apos;s totals will all be there.
          </p>
          <Link
            href="/"
            className="inline-block mt-3 px-4 py-2 border rounded font-medium"
          >
            Go to dashboard
          </Link>
        </li>
      </ol>

      <div className="text-xs text-muted-foreground border-t pt-4">
        Connected number: <strong>{WHATSAPP_NUMBER}</strong>. Having trouble? Make sure the
        WhatsApp number you message from matches the one on your account.
      </div>
    </div>
  );
}

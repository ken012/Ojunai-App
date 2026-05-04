import type { Metadata } from "next";
import { Inter } from "next/font/google";
import "./globals.css";
import { Providers } from "./providers";

const inter = Inter({ subsets: ["latin"] });

export const metadata: Metadata = {
  title: "Ojunai",
  description: "WhatsApp-first AI business operator for Nigerian SMEs",
  verification: {
    google: "8-npwdTxdHqH0oAP7Nii75oolo1dgozM1oKUTwRJGR0",
  },
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en">
      <body className={inter.className}>
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}

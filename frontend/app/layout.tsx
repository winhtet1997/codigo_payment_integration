import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Checkout",
  description: "Sandbox checkout for 2C2P payments in SGD."
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>
        <main className="mx-auto max-w-xl px-4 py-10">{children}</main>
      </body>
    </html>
  );
}

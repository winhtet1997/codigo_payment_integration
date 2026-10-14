"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { formatMoney, getTransaction, inquire, TransactionDetail } from "@/lib/api";

export default function ResultView({ invoiceNo }: { invoiceNo: string }) {
  const router = useRouter();
  const [detail, setDetail] = useState<TransactionDetail | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    let cancelled = false;
    let timer = 0;

    async function tick() {
      try {
        let next = await getTransaction(invoiceNo);
        if (next.status === "Pending") {
          try {
            next = await inquire(invoiceNo);
          } catch {
            // The row is still the source of truth. Keep polling if inquiry cannot reach 2C2P.
          }
        }
        if (cancelled) return;
        setDetail(next);
        if (next.status === "Pending") {
          timer = window.setTimeout(tick, 3000);
        }
      } catch (reason) {
        if (!cancelled) setError(reason instanceof Error ? reason.message : "Could not load the payment.");
      }
    }

    tick();
    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, [invoiceNo]);

  if (error) {
    return <p className="rounded-xl bg-white p-6 text-sm text-red-700 shadow-sm">{error}</p>;
  }

  if (!detail) {
    return <p className="rounded-xl bg-white p-6 text-sm text-slate-600 shadow-sm">Loading payment…</p>;
  }

  function tryAgain() {
    const attempt = crypto.randomUUID();
    router.push(`/?customerId=${detail.customerId}&productId=${detail.productId}&attempt=${attempt}`);
  }

  return (
    <section className="space-y-4 rounded-xl bg-white p-6 shadow-sm">
      {detail.status === "Succeeded" ? (
        <>
          <h1 className="text-2xl font-semibold text-emerald-800">Payment succeeded</h1>
          <dl className="space-y-1 text-sm">
            <Row label="Product" value={detail.productName} />
            <Row label="Amount" value={formatMoney(detail.amount, detail.currency)} />
            <Row label="Invoice" value={detail.invoiceNo} />
            <Row label="Payment method" value={detail.paymentChannel ?? "—"} />
            <Row label="Card or account" value={detail.maskedCardNo ?? "—"} />
          </dl>
        </>
      ) : null}

      {detail.status === "Failed" ? (
        <>
          <h1 className="text-2xl font-semibold text-red-800">Payment failed</h1>
          <p className="text-sm">{detail.respDesc || "The payment was declined."}</p>
          <button
            type="button"
            onClick={tryAgain}
            className="inline-block rounded-md bg-slate-900 px-4 py-2 text-sm text-white"
          >
            Go back
          </button>
        </>
      ) : null}

      {detail.status === "Pending" ? (
        <>
          <h1 className="text-2xl font-semibold text-amber-800">Processing payment</h1>
          <p className="text-sm text-slate-600">
            Invoice {detail.invoiceNo}. This page checks the payment every few seconds.
          </p>
        </>
      ) : null}
    </section>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-4">
      <dt className="text-slate-500">{label}</dt>
      <dd className="text-right">{value}</dd>
    </div>
  );
}

"use client";

import { FormEvent, useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import {
  checkout,
  Customer,
  formatMoney,
  getCards,
  getCustomers,
  getProducts,
  Product,
  SavedCard
} from "@/lib/api";

export default function CheckoutForm() {
  const router = useRouter();
  const params = useSearchParams();
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [cards, setCards] = useState<SavedCard[]>([]);
  const [customerId, setCustomerId] = useState(params.get("customerId") ?? "");
  const [productId, setProductId] = useState(params.get("productId") ?? "");
  const [cardChoice, setCardChoice] = useState("new");
  const [idempotencyKey, setIdempotencyKey] = useState("");
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    let cancelled = false;
    Promise.all([getCustomers(), getProducts()])
      .then(([nextCustomers, nextProducts]) => {
        if (cancelled) return;
        setCustomers(nextCustomers);
        setProducts(nextProducts);
        setCustomerId((current) => current || (nextCustomers[0] ? String(nextCustomers[0].id) : ""));
        setProductId((current) => current || (nextProducts[0] ? String(nextProducts[0].id) : ""));
      })
      .catch((reason: Error) => {
        if (!cancelled) setError(reason.message);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (!customerId) {
      setCards([]);
      return;
    }

    let cancelled = false;
    getCards(Number(customerId))
      .then((nextCards) => {
        if (!cancelled) setCards(nextCards);
      })
      .catch(() => {
        if (!cancelled) setCards([]);
      });
    return () => {
      cancelled = true;
    };
  }, [customerId]);

  useEffect(() => {
    setCardChoice("new");
  }, [customerId]);

  // One key per purchase, kept across refresh. A new selection, or Go back after
  // a failure (the attempt query), starts a different charge.
  useEffect(() => {
    if (!customerId || !productId) {
      return;
    }

    const attempt = params.get("attempt") ?? "initial";
    const storageKey = `checkout-key:${customerId}:${productId}:${cardChoice}:${attempt}`;
    const existing = window.sessionStorage.getItem(storageKey);
    if (existing) {
      setIdempotencyKey(existing);
      return;
    }

    const created = crypto.randomUUID();
    window.sessionStorage.setItem(storageKey, created);
    setIdempotencyKey(created);
  }, [customerId, productId, cardChoice, params]);

  const customer = customers.find((item) => String(item.id) === customerId);
  const product = products.find((item) => String(item.id) === productId);
  const usingSavedCard = cardChoice !== "new";

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!customer || !product || submitting) return;

    setSubmitting(true);
    setError("");
    try {
      const result = await checkout({
        customerId: customer.id,
        productId: product.id,
        customerCardId: usingSavedCard ? Number(cardChoice) : null,
        idempotencyKey
      });

      if (result.redirectUrl) {
        window.location.assign(result.redirectUrl);
        return;
      }

      router.push(`/result/${encodeURIComponent(result.invoiceNo)}`);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Checkout failed.");
      setSubmitting(false);
    }
  }

  return (
    <form onSubmit={onSubmit} className="space-y-6 rounded-xl bg-white p-6 shadow-sm">
      <header>
        <p className="text-xs font-medium uppercase tracking-wide text-slate-500">Sandbox · Singapore · SGD</p>
        <h1 className="mt-1 text-2xl font-semibold">Checkout</h1>
      </header>

      {loading ? <p className="text-sm text-slate-600">Loading customers and products…</p> : null}

      <label className="block text-sm font-medium">
        Customer
        <select
          className="mt-1 w-full rounded-md border border-slate-300 px-3 py-2"
          value={customerId}
          onChange={(event) => setCustomerId(event.target.value)}
          disabled={submitting}
        >
          {customers.map((item) => (
            <option key={item.id} value={item.id}>
              {item.firstName} {item.lastName}
            </option>
          ))}
        </select>
      </label>

      <label className="block text-sm font-medium">
        Product
        <select
          className="mt-1 w-full rounded-md border border-slate-300 px-3 py-2"
          value={productId}
          onChange={(event) => setProductId(event.target.value)}
          disabled={submitting}
        >
          {products.map((item) => (
            <option key={item.id} value={item.id}>
              {item.name} · {formatMoney(item.price, item.currency)} · {item.paymentType}
            </option>
          ))}
        </select>
      </label>

      <section className="rounded-lg bg-slate-50 p-4 text-sm">
        <h2 className="font-medium">Payment summary</h2>
        {customer && product ? (
          <dl className="mt-3 space-y-1">
            <div className="flex justify-between gap-4">
              <dt className="text-slate-500">Customer</dt>
              <dd>
                {customer.firstName} {customer.lastName}
              </dd>
            </div>
            <div className="flex justify-between gap-4">
              <dt className="text-slate-500">Product</dt>
              <dd>{product.name}</dd>
            </div>
            <div className="flex justify-between gap-4">
              <dt className="text-slate-500">Payment type</dt>
              <dd>{product.paymentType}</dd>
            </div>
            <div className="flex justify-between gap-4">
              <dt className="text-slate-500">Price</dt>
              <dd className="font-medium">{formatMoney(product.price, product.currency)}</dd>
            </div>
          </dl>
        ) : (
          <p className="mt-2 text-slate-500">Choose a customer and a product.</p>
        )}
      </section>

      <fieldset className="space-y-2 text-sm">
        <legend className="font-medium">How to pay</legend>
        {cards.length === 0 ? <p className="text-slate-500">No saved cards yet.</p> : null}
        {cards.map((card) => (
          <label key={card.id} className="flex items-center gap-2">
            <input
              type="radio"
              name="card"
              value={card.id}
              checked={cardChoice === String(card.id)}
              onChange={() => setCardChoice(String(card.id))}
              disabled={submitting}
            />
            {card.maskedCardNo}
          </label>
        ))}
        <label className="flex items-center gap-2">
          <input
            type="radio"
            name="card"
            value="new"
            checked={cardChoice === "new"}
            onChange={() => setCardChoice("new")}
            disabled={submitting}
          />
          Pay with a new payment method
        </label>
        {product?.paymentType === "Recurring" && cardChoice === "new" ? (
          <p className="text-slate-600">
            The next page is card only. Tick the option to save the card. If you skip it, the payment can still
            succeed, but the card will not be stored.
          </p>
        ) : null}
        {product?.paymentType === "OneTime" && cardChoice === "new" ? (
          <p className="text-slate-600">The next page lists every payment method enabled for this merchant.</p>
        ) : null}
        {usingSavedCard ? (
          <p className="text-slate-600">The saved card is charged here. You may still be asked to approve the payment.</p>
        ) : null}
      </fieldset>

      {error ? <p className="text-sm text-red-700">{error}</p> : null}

      <button
        type="submit"
        disabled={submitting || !customer || !product || !idempotencyKey}
        className="w-full rounded-md bg-slate-900 px-4 py-2 text-white disabled:opacity-50"
      >
        {submitting ? "Processing…" : "Checkout"}
      </button>
    </form>
  );
}

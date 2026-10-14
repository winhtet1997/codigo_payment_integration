import { Suspense } from "react";
import CheckoutForm from "./checkout-form";

export default function HomePage() {
  return (
    <Suspense fallback={<p className="text-sm text-slate-600">Loading checkout…</p>}>
      <CheckoutForm />
    </Suspense>
  );
}

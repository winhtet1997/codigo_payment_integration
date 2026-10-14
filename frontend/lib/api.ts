export const apiBase = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

export type Customer = {
  id: number;
  firstName: string;
  lastName: string;
  email: string;
};

export type Product = {
  id: number;
  name: string;
  description: string;
  price: number;
  currency: string;
  paymentType: "OneTime" | "Recurring";
};

export type SavedCard = {
  id: number;
  maskedCardNo: string;
  createdAt: string;
};

export type CheckoutResult = {
  invoiceNo: string;
  status: string;
  redirectUrl: string | null;
};

export type TransactionDetail = {
  invoiceNo: string;
  status: "Pending" | "Succeeded" | "Failed" | string;
  respCode: string | null;
  respDesc: string | null;
  amount: number;
  currency: string;
  productName: string;
  paymentType: string;
  customerId: number;
  productId: number;
  paymentChannel: string | null;
  maskedCardNo: string | null;
  paidAt: string | null;
};

async function readError(response: Response): Promise<string> {
  try {
    const body = await response.json();
    return body.detail || body.title || `Request failed (${response.status})`;
  } catch {
    return `Request failed (${response.status})`;
  }
}

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(`${apiBase}${path}`, { cache: "no-store" });
  if (!response.ok) {
    throw new Error(await readError(response));
  }
  return response.json() as Promise<T>;
}

export function getCustomers() {
  return getJson<Customer[]>("/api/customers");
}

export function getProducts() {
  return getJson<Product[]>("/api/products");
}

export function getCards(customerId: number) {
  return getJson<SavedCard[]>(`/api/customers/${customerId}/cards`);
}

export function getTransaction(invoiceNo: string) {
  return getJson<TransactionDetail>(`/api/transactions/${encodeURIComponent(invoiceNo)}`);
}

export async function inquire(invoiceNo: string): Promise<TransactionDetail> {
  const response = await fetch(`${apiBase}/api/transactions/${encodeURIComponent(invoiceNo)}/inquiry`, {
    method: "POST",
    cache: "no-store"
  });
  if (!response.ok) {
    throw new Error(await readError(response));
  }
  return response.json();
}

export async function checkout(input: {
  customerId: number;
  productId: number;
  customerCardId: number | null;
  idempotencyKey: string;
}): Promise<CheckoutResult> {
  const response = await fetch(`${apiBase}/api/checkout`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "Idempotency-Key": input.idempotencyKey
    },
    body: JSON.stringify({
      customerId: input.customerId,
      productId: input.productId,
      customerCardId: input.customerCardId
    })
  });

  if (!response.ok) {
    throw new Error(await readError(response));
  }

  return response.json();
}

export function formatMoney(amount: number, currency: string) {
  return new Intl.NumberFormat("en-SG", { style: "currency", currency }).format(amount);
}

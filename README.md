# Payment checkout (2C2P sandbox)

A small Singapore checkout. Prices are in SGD. New payment methods open 2C2P's hosted page. Saved cards are charged with the Direct API (Do Payment), then confirmed with Payment Inquiry.

There is no login. Pick any seeded customer. "Recurring" only means the card is saved for later. This app does not schedule the next charge.

## Setup

You need the .NET 8 SDK and Node.js 22. Docker Compose is the easy way to get MySQL and Redis. This project was also run without Docker, because Docker Desktop would not fit on the machine. MySQL and Redis still have to be real programs. The API does not embed them.

### 1. Sandbox keys

Do not commit the merchant secret.

1. Open the [2C2P sandbox](https://developer.2c2p.com/docs/sandbox) and copy the merchant ID and secret key.
2. Copy `.env.example` to `.env` and fill in the two values.
3. For `dotnet run` outside Docker, copy `src/PaymentIntegration.Api/appsettings.Development.example.json` to `src/PaymentIntegration.Api/appsettings.Development.json` (that file is gitignored) and paste the same keys there.

`PublicApiBaseUrl` / `PUBLIC_API_BASE_URL` is the URL 2C2P can call. 2C2P cannot reach `localhost`, so that value is a tunnel.

```powershell
ngrok http 8080
```

Put the `https://....ngrok-free.app` URL in `.env` as `PUBLIC_API_BASE_URL`, then restart the API. The browser return URL stays `http://localhost:3000` because the shopper's browser can open localhost. Only the server-to-server notification needs the tunnel.

Singapore test cards, including a declined card: [2C2P test cards](https://developer.2c2p.com/docs/test-cards). Use the Singapore set.

### Without Docker

Install the .NET 8 SDK, Node.js, MySQL 8, and a Windows build of Redis, and put `dotnet` and `npm` on your PATH. Tell the start script where MySQL and Redis are, then start them:

```powershell
$env:MYSQL_HOME = '<folder that contains mysqld.exe>'
$env:REDIS_HOME = '<folder that contains redis-server.exe>'
powershell -ExecutionPolicy Bypass -File scripts\start-local.ps1
```

That opens MySQL on port 3306 and Redis on port 6379, both on localhost, with user `root` and password `root`. It creates the `payment` and `payment_test` databases. Running it again does not restart a program that is already listening.

In a second terminal, from this folder:

```powershell
dotnet run --project src/PaymentIntegration.Api
```

In a third terminal:

```powershell
cd frontend
npm install
npm run dev
```

`dotnet test` uses that same MySQL and Redis. Docker is not required for the test.

### With Docker

Install Docker Desktop. From this folder, Compose reads `.env` for the merchant id, the secret, and the tunnel URL.

```powershell
docker compose up --build
```

That builds and starts four containers:

- MySQL 8 on port 3306, database `payment`, user `root`, password `root`
- Redis on port 6379
- the API at http://localhost:8080, Swagger at http://localhost:8080/swagger
- the checkout page at http://localhost:3000

The API waits until MySQL and Redis are healthy, then applies the migrations and seeds 3 customers and 4 products (2 OneTime, 2 Recurring). Starting the stack again does not insert that seed a second time. Saved cards stay in the MySQL volume.

Stop the stack with `docker compose down`. `docker compose down -v` also deletes the database volume.

### Docker for MySQL and Redis only

The API and the page can still run on the host. Start only the databases, then the two apps:

```powershell
docker compose up mysql redis -d
dotnet run --project src/PaymentIntegration.Api
```

In another terminal:

```powershell
cd frontend
npm install
npm run dev
```

In this setup the host API reads the 2C2P keys from `appsettings.Development.json`. The containers read them from `.env`.

### Tests

MySQL and Redis must already be running on `127.0.0.1` ports 3306 and 6379. `scripts\start-local.ps1` does that without Docker. `docker compose up` does it too, because those ports are published. The test mocks only 2C2P. It uses a separate database named `payment_test`.

```powershell
dotnet test
```

`JwtCodecTests` does not need MySQL or Redis. The checkout tests do.

## What to click

1. **OneTime, new method.** Choose City Tour or Night Safari Ticket and "Pay with a new payment method". Checkout opens the hosted page with every method enabled for the merchant.
2. **Recurring, new method.** Choose Monthly Gym Pass or Weekly Meal Plan. The hosted page is card only. Tick the save-card box. If you do not tick it, the payment can still succeed and no card is stored.
3. **Saved card.** After a card exists, pick it. There is no hosted page. If 2C2P returns `1001`, the browser goes to the 3DS URL. Otherwise it goes to the result page.
4. **Failed payment.** Use a declined Singapore test card. The result page shows `respDesc` and **Go back**, with the same customer and product selected. Go back starts a new attempt, so the same customer and product can be charged again.

The result page reads your database, not the redirect query string. While the row is `Pending` it says "Processing payment" and polls every 3 seconds. Each poll also calls Payment Inquiry, so a missing notification (no tunnel) can still finish the payment. Saved-card payments always call Payment Inquiry during checkout when Do Payment returns `2000`. `2000` alone is not success.

## Project structure

```
src/PaymentIntegration.Domain          entities and enums, no NuGet packages
src/PaymentIntegration.Application     checkout rules, interfaces
src/PaymentIntegration.Infrastructure  MySQL, Redis, 2C2P HTTP client
src/PaymentIntegration.Api             controllers and Swagger
tests/PaymentIntegration.Tests         JWT test and the parallel checkout test
frontend                               Next.js, React, Tailwind
```

A request walks in a straight line:

1. The React page calls a controller.
2. The controller calls an application service (`CheckoutService`, `CatalogService`, `PaymentUpdateService`).
3. The service talks to interfaces (`IPaymentGateway`, `ITransactionRepository`, `IDistributedLock`, `IAppCache`).
4. Infrastructure implements those interfaces. Domain has the table shapes and nothing else.

2C2P, MySQL, and Redis are behind those interfaces. A second provider would be a new `IPaymentGateway` class. `CheckoutService` would stay the same.

## Idempotency, locking, and cache

A double-click, refresh, or retry must not create two charges.

- The page keeps one UUID per purchase (customer, product, and card) in `sessionStorage` and sends it as `Idempotency-Key`. A refresh reuses that key, so it does not start a second charge. Changing the selection creates a new key. After a failed payment, Go back adds a new attempt id so the customer can try again.
- Redis lock: `SET lock:checkout:{key} {owner} NX PX 45000`. Only the owner releases it, using a Lua script that deletes the key only if the value is still that owner.
- The other click waits up to 20 seconds, then gets the same invoice and redirect URL. If the first call is still unfinished after that, the response is HTTP 409.
- The first call inserts the `transaction` row **before** calling 2C2P. `idempotency_key` is unique. If the lock expires and a second request gets in, the insert fails and that request does not call 2C2P.
- The first HTTP response is stored on the row (`checkout_response_json`). The same key returns that JSON and does not call 2C2P again, even after the payment later succeeds.
- The same key with a different customer, product, or card is HTTP 422.
- The price is read from `product`. The browser cannot send an amount.

Notifications and inquiry update with `UPDATE ... WHERE status = 'Pending'`. A second notification cannot change a Succeeded or Failed row and cannot insert a second card. `(customer_id, customer_token)` is unique.

Cache-aside is only for customers, products, and saved cards (`cache:customers`, `cache:products`, `cache:cards:{id}`, 10 minutes). Saving a card deletes that customer's card key. Transaction status is not cached. `GET /api/transactions/{invoiceNo}` is read from MySQL and sends `Cache-Control: no-store`.

Redis is required for the lock. If Redis is down, checkout returns 503. The lists still try MySQL when the cache read fails. The unique key is the last guard if the lock expires.

## 2C2P calls

All token and inquiry calls are JWT HMAC-SHA256, signed in `JwtCodec` so the payload is exactly our JSON. Do Payment is plain JSON.

| Path | Payment Token body |
| --- | --- |
| OneTime, new method | no `paymentChannel`, so every enabled method is shown |
| Recurring, new method | `paymentChannel: ["CC"]`, `tokenize: true` |
| Saved card | `paymentChannel: ["CC"]`, `customerToken: ["..."]`, `customerTokenOnly: true`, `request3DS: "N"` |

The task text says `request3Ds`. The 2C2P field name is `request3DS`.

Every token request includes `invoiceNo`, `amount`, `currencyCode: "SGD"`, `frontendReturnUrl`, and `backendReturnUrl`.

- Token `respCode` other than `0000` marks the row Failed.
- Do Payment `2000` is followed by Payment Inquiry. Inquiry `0000` is Succeeded. Pending codes (`0001`, `1000`–`1005`, `2001`, `2002`) stay Pending. Anything else is Failed.
- Do Payment `1000`–`1004` with a URL is the 3DS redirect. The task names `1001`. The others are the same kind of redirect in 2C2P's response codes.
- A notification is final: `0000` is Succeeded, any other code is Failed. Signature failure and amount or currency mismatch return HTTP 400 and do not change the row. An unknown invoice is acknowledged with HTTP 200 and a warning log, so 2C2P does not retry forever. The handler does the database work and then returns 200. It does not call 2C2P, so it stays quick. A crash before the update means 2C2P retries, and the conditional update makes that retry safe.

`POST /api/transactions/{invoiceNo}/inquiry` is extra. The suggested list did not include it. The result page uses it so a saved-card 3DS return, or a hosted payment with no tunnel, can still be confirmed.

## Data model

Tables are `customer`, `product`, `transaction`, and `customer_card`. Money is `decimal(12,2)`. Status and payment type are MySQL enums. Full card numbers and CVV are never stored. The card API returns `maskedCardNo` only. The browser sends `customerCardId`, and the server looks up the token.

Extra columns, and why:

| Column | Why |
| --- | --- |
| `request_fingerprint` | Detects reuse of an idempotency key for a different purchase |
| `payment_token` | Stored for support. Not returned to the browser |
| `checkout_response_json` | The original checkout response, so a retry does not call 2C2P |
| `customer_token` is `varchar(255)` | 2C2P tokens can be longer than 100 characters |
| `resp_desc` is `varchar(255)` | Long enough for a configuration error as well as 2C2P's text |

Schema is the EF Core migration `InitialCreate` in `src/PaymentIntegration.Infrastructure/Persistence/Migrations`. Startup calls `Database.Migrate()`.

## Assumptions

- Anyone may select any customer.
- Country and currency are Singapore and SGD. A product in another currency is rejected.
- The public sandbox merchant settles in THB. A result can show `amount` in THB and the original SGD charge in `fxAmount` / `fxCurrencyCode`. Either pair matching the stored row is accepted.
- Inquiry can still be in progress. A notification cannot: any code other than `0000` is a failure.
- The first final status wins. A later notification does not flip Succeeded to Failed.
- A customer token on a notification is saved even when we also record the payment. A duplicate token is ignored.
- `frontendReturnUrl` is `/result/{invoiceNo}` so 2C2P can append its own query string without dropping the invoice number.
- Do Payment also sends `responseReturnUrl` to that same page so a 3DS challenge comes back to us.
- Swagger is on in every environment so `docker compose up` exposes it.
- Errors are RFC 7807 problem details (`application/problem+json`).

## What I would do with more time

- Add refunds as a new use case behind `IPaymentGateway`, still keyed so a refund click cannot run twice.
- Add a second payment provider as another `IPaymentGateway` class, without changing `CheckoutService`.
- Move notification handling to a queue if we ever need to return 200 before the database write. Doing the write first is safer today.
- Add a real auth boundary before this is used by more than one merchant.

## API

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/customers` | Seeded customers |
| GET | `/api/products` | Seeded products |
| GET | `/api/customers/{id}/cards` | Masked saved cards |
| POST | `/api/checkout` | Header `Idempotency-Key`. Body `customerId`, `productId`, optional `customerCardId` |
| POST | `/api/payments/2c2p/notification` | Backend notification |
| GET | `/api/transactions/{invoiceNo}` | Result page. Not cached |
| POST | `/api/transactions/{invoiceNo}/inquiry` | Payment Inquiry fallback |

Checkout response:

```json
{
  "invoiceNo": "INV20261007000123",
  "status": "Pending",
  "redirectUrl": "https://sandbox-pgw-ui.2c2p.com/payment/4.3/#/token/..."
}
```

`redirectUrl` is null when a saved-card payment needs no browser redirect.

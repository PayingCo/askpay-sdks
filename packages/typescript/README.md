# @paying/askpay

Official TypeScript / Node client for **AskPay by Paying.co** — talk to the
Paying.co REST API to store orders, generate hosted-payment URLs, read payment
status, and more.

Part of the [askpay-sdks](https://github.com/PayingCo/askpay-sdks) monorepo.

## Install

```bash
npm install @paying/askpay
```

Requires Node 18+ (uses the built-in `fetch`).

## Authentication

Get a Paying.co API token from your dashboard. The token is a Laravel Sanctum
bearer token whose abilities gate each endpoint. Provide it via the
`PAYING_API_TOKEN` environment variable (recommended) or pass it explicitly.

```bash
export PAYING_API_TOKEN="your-token"
```

## Quickstart

```ts
import { AskPayClient } from "@paying/askpay";

const askpay = new AskPayClient(); // reads PAYING_API_TOKEN
// or: new AskPayClient({ token: "…", baseUrl: "https://ask.paying.co/api" });

const me = await askpay.whoami();
console.log(`Authenticated as ${me.name} (${me.hash})`);

const { url } = await askpay.createPaymentUrl("INV-2041"); // existing order ref
console.log(url);

if (await askpay.isPaid("INV-2041")) {
  const payment = await askpay.readOrder("INV-2041");
  console.log(payment.amount, payment.card_type);
}
```

A runnable end-to-end script lives in [`examples/checkout.ts`](examples/checkout.ts):

```bash
PAYING_API_TOKEN="…" npx tsx examples/checkout.ts
```

## Methods

| Method | Endpoint | Token ability |
| ------ | -------- | ------------- |
| `storeOrder(order)` | `POST /order/store` | integrator token |
| `cancelOrder(ref)` | `POST /order/cancel` | `Order - Cancel` |
| `readOrder(ref)` | `POST /order/read` | `Order - Read` |
| `isPaid(ref)` | (uses `readOrder`) | `Order - Read` |
| `createPaymentUrl(ref)` | `POST /url/create` | `Generate QR Code` |
| `sendSms(ref, phone)` | `POST /smspay/send` | `Send SMS` |
| `whoami()` | `GET /store` | `Store - Read` |
| `getPolling()` | `GET /poll` | integrator token |

`storeOrder` takes an object whose shape follows your integrator's configured
field mappings. `createPaymentUrl` and `readOrder` operate on an order that
already exists — there is no "charge an arbitrary amount" call.

## Errors

All failures throw a subclass of `PayingError`:

- `PayingValidationError` — client-side checks (missing reference, bad phone).
- `PayingApiError` — the API returned HTTP 200 with an `{ error }` body (e.g.
  "A payment hasn't been applied to this order yet"). `isPaid` treats this as `false`.
- `PayingError` — transport/HTTP errors, carrying `status`, `code`, `requestId`.

```ts
import { PayingApiError } from "@paying/askpay";

try {
  await askpay.readOrder("INV-2041");
} catch (err) {
  if (err instanceof PayingApiError) console.log("not paid yet");
  else throw err;
}
```

## Configuration

```ts
new AskPayClient({
  token: "…",                          // else PAYING_API_TOKEN
  baseUrl: "https://ask.paying.co/api", // default
  timeoutMs: 15000,                     // default
  maxRetries: 2,                        // retries 429/5xx with backoff
});
```

## License

[MIT](../../LICENSE) © 2026 Paying.co (Mojave Payment Technologies, LLC)

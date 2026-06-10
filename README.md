# AskPay SDKs

Official client libraries for **AskPay by Paying.co** — generate payment links,
render QR codes, and check payment status from your own applications.

These SDKs wrap the live Paying.co REST API (Laravel + Sanctum bearer tokens).
Each method maps directly to an endpoint under `/api`:

| Method             | Endpoint            | Token ability      | Notes |
| ------------------ | ------------------- | ------------------ | ----- |
| `storeOrder`       | `POST /order/store` | integrator token   | Body shape follows your integrator field mappings. |
| `cancelOrder`      | `POST /order/cancel`| `Order - Cancel`   | Delete an order by `merchant_reference`. |
| `readOrder`        | `POST /order/read`  | `Order - Read`     | Returns the payment record, or an error until one exists. |
| `isPaid`           | (uses `readOrder`)  | `Order - Read`     | Convenience boolean. |
| `createPaymentUrl` | `POST /url/create`  | `Generate QR Code` | Returns `{ url }` for an existing order reference. |
| `sendSms`          | `POST /smspay/send` | `Send SMS`         | Texts the link via the merchant's Twilio config. |
| `whoami`           | `GET /store`        | `Store - Read`     | Integrator profile plus the Hashids `hash`. |
| `getPolling`       | `GET /poll`         | integrator token   | Polling configuration. |

> The REST API works off **existing orders**: store an order first, then
> generate a URL or read its payment status. There is no "charge an arbitrary
> amount" call — amounts come from the order. The API also commonly returns HTTP
> 200 with an `{ "error": ... }` body; all SDKs surface that as an API error.

## Packages

| Language          | Package / Module                              | Path                  | Status |
| ----------------- | --------------------------------------------- | --------------------- | ------ |
| TypeScript / Node | `@paying/askpay` (npm)                         | `packages/typescript` | ✅ Stable |
| Python            | `paying-askpay` (PyPI)                         | `packages/python`     | ✅ Stable |
| Go                | `github.com/PayingCo/askpay-sdks/packages/go` | `packages/go`         | ✅ Stable |
| PHP               | `paying/askpay` (Composer / Packagist)        | `packages/php`        | ✅ Stable |
| C# / .NET         | `Paying.AskPay` (NuGet)                        | `packages/csharp`     | ✅ Stable |
| Java              | `co.paying:askpay`                            | `packages/java`       | 🚧 Planned |

The OpenAPI source of truth lives in [`spec/openapi.yaml`](spec/openapi.yaml);
all clients mirror it.

## Quickstart

Get a Paying.co API token from your dashboard. For status lookups, the token
must carry the **Order — Read** ability. Export it:

```bash
export PAYING_API_TOKEN="sk_live_…"
```

### TypeScript

```bash
npm install @paying/askpay
```

```ts
import { AskPayClient } from "@paying/askpay";

const askpay = new AskPayClient(); // reads PAYING_API_TOKEN

await askpay.whoami(); // confirm the integrator account
const { url } = await askpay.createPaymentUrl("INV-2041"); // existing order ref
console.log(url);

if (await askpay.isPaid("INV-2041")) {
  const payment = await askpay.readOrder("INV-2041");
  console.log(payment.amount, payment.card_type);
}
```

### Python

```bash
pip install paying-askpay
```

```python
from askpay import AskPayClient

askpay = AskPayClient()  # reads PAYING_API_TOKEN

askpay.whoami()  # confirm the integrator account
result = askpay.create_payment_url("INV-2041")  # existing order ref
print(result["url"])

if askpay.is_paid("INV-2041"):
    payment = askpay.read_order("INV-2041")
    print(payment["amount"], payment.get("card_type"))
```

Runnable end-to-end examples live in each package's `examples/` folder.

## Authentication

All clients read `PAYING_API_TOKEN` from the environment by default, or accept a
token explicitly. Never hard-code tokens in source you commit. The token is a
Laravel Sanctum bearer token whose abilities gate each endpoint (e.g. reading an
order's payment needs `Order - Read`; generating a URL needs `Generate QR Code`).
Run `whoami` first to confirm the integrator account.

## Contributing

Issues and PRs welcome. Changes to the API surface should start in
`spec/openapi.yaml` so every client stays in lockstep.

## License

[MIT](LICENSE) © 2026 Paying.co (Mojave Payment Technologies, LLC)

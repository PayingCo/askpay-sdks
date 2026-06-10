# paying-askpay

Official Python client for **AskPay by Paying.co** — talk to the Paying.co REST
API to store orders, generate hosted-payment URLs, read payment status, and more.

Part of the [askpay-sdks](https://github.com/PayingCo/askpay-sdks) monorepo.

## Install

```bash
pip install paying-askpay
```

Requires Python 3.9+. No third-party dependencies (standard library only).

## Authentication

Get a Paying.co API token from your dashboard. The token is a Laravel Sanctum
bearer token whose abilities gate each endpoint. Provide it via the
`PAYING_API_TOKEN` environment variable (recommended) or pass it explicitly.

```bash
export PAYING_API_TOKEN="your-token"
```

## Quickstart

```python
from askpay import AskPayClient

askpay = AskPayClient()  # reads PAYING_API_TOKEN
# or: AskPayClient(token="…", base_url="https://ask.paying.co/api")

me = askpay.whoami()
print(f"Authenticated as {me['name']} ({me.get('hash')})")

result = askpay.create_payment_url("INV-2041")  # existing order ref
print(result["url"])

if askpay.is_paid("INV-2041"):
    payment = askpay.read_order("INV-2041")
    print(payment["amount"], payment.get("card_type"))
```

A runnable end-to-end script lives in [`examples/checkout.py`](examples/checkout.py):

```bash
PAYING_API_TOKEN="…" python examples/checkout.py
```

## Methods

| Method | Endpoint | Token ability |
| ------ | -------- | ------------- |
| `store_order(order)` | `POST /order/store` | integrator token |
| `cancel_order(ref)` | `POST /order/cancel` | `Order - Cancel` |
| `read_order(ref)` | `POST /order/read` | `Order - Read` |
| `is_paid(ref)` | (uses `read_order`) | `Order - Read` |
| `create_payment_url(ref)` | `POST /url/create` | `Generate QR Code` |
| `send_sms(ref, phone)` | `POST /smspay/send` | `Send SMS` |
| `whoami()` | `GET /store` | `Store - Read` |
| `get_polling()` | `GET /poll` | integrator token |

`store_order` takes a dict whose shape follows your integrator's configured
field mappings. `create_payment_url` and `read_order` operate on an order that
already exists — there is no "charge an arbitrary amount" call.

## Errors

All failures raise a subclass of `PayingError`:

- `PayingValidationError` — client-side checks (missing reference, bad phone).
- `PayingApiError` — the API returned HTTP 200 with an `{"error": ...}` body
  (e.g. "A payment hasn't been applied to this order yet"). `is_paid` treats
  this as `False`.
- `PayingError` — transport/HTTP errors, carrying `status`, `code`, `request_id`.

```python
from askpay import PayingApiError

try:
    askpay.read_order("INV-2041")
except PayingApiError:
    print("not paid yet")
```

## Configuration

```python
AskPayClient(
    token="…",                          # else PAYING_API_TOKEN
    base_url="https://ask.paying.co/api", # default
    timeout=15.0,                         # seconds, default
    max_retries=2,                        # retries 429/5xx with backoff
)
```

## License

[MIT](../../LICENSE) © 2026 Paying.co (Mojave Payment Technologies, LLC)

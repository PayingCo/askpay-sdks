# paying/askpay

Official PHP client for **AskPay by Paying.co** — talk to the Paying.co REST API
to store orders, generate hosted-payment URLs, read payment status, and more.

Part of the [askpay-sdks](https://github.com/PayingCo/askpay-sdks) monorepo.

## Install

```bash
composer require paying/askpay
```

Requires PHP 8.0+ with the `curl` and `json` extensions.

## Authentication

Get a Paying.co API token from your dashboard. The token is a Laravel Sanctum
bearer token whose abilities gate each endpoint. Provide it via the
`PAYING_API_TOKEN` environment variable (recommended) or pass it explicitly.

```bash
export PAYING_API_TOKEN="your-token"
```

## Quickstart

```php
<?php

use Paying\AskPay\AskPayClient;

$askpay = new AskPayClient(); // reads PAYING_API_TOKEN
// or: new AskPayClient('…', ['base_url' => 'https://ask.paying.co/api']);

$me = $askpay->whoami();
echo "Authenticated as {$me['name']} ({$me['hash']})\n";

$result = $askpay->createPaymentUrl('INV-2041'); // existing order ref
echo $result['url'] . "\n";

if ($askpay->isPaid('INV-2041')) {
    $payment = $askpay->readOrder('INV-2041');
    echo $payment['amount'] . ' ' . ($payment['card_type'] ?? '') . "\n";
}
```

A runnable end-to-end script lives in [`examples/checkout.php`](examples/checkout.php):

```bash
PAYING_API_TOKEN="…" php examples/checkout.php
```

## Methods

| Method | Endpoint | Token ability |
| ------ | -------- | ------------- |
| `storeOrder(array $order)` | `POST /order/store` | integrator token |
| `cancelOrder(string $ref)` | `POST /order/cancel` | `Order - Cancel` |
| `readOrder(string $ref)` | `POST /order/read` | `Order - Read` |
| `isPaid(string $ref)` | (uses `readOrder`) | `Order - Read` |
| `createPaymentUrl(string $ref)` | `POST /url/create` | `Generate QR Code` |
| `sendSms(string $ref, string $phone)` | `POST /smspay/send` | `Send SMS` |
| `whoami()` | `GET /store` | `Store - Read` |
| `getPolling()` | `GET /poll` | integrator token |

`storeOrder` takes an array whose shape follows your integrator's configured
field mappings. `createPaymentUrl` and `readOrder` operate on an order that
already exists — there is no "charge an arbitrary amount" call.

## Errors

All failures throw a subclass of `Paying\AskPay\PayingException`:

- `ValidationException` — client-side checks (missing reference, bad phone).
- `ApiException` — the API returned HTTP 200 with an `{ "error": ... }` body
  (e.g. "A payment hasn't been applied to this order yet"). `isPaid` treats
  this as `false`.
- `PayingException` — transport/HTTP errors, carrying `$status`, `$code`, `$requestId`.

```php
use Paying\AskPay\ApiException;

try {
    $askpay->readOrder('INV-2041');
} catch (ApiException $e) {
    echo "not paid yet\n";
}
```

## Configuration

```php
new AskPayClient('…', [
    'base_url'    => 'https://ask.paying.co/api', // default
    'timeout'     => 15,                          // seconds, default
    'max_retries' => 2,                           // retries 429/5xx with backoff
]);
```

If you are not using Composer's autoloader, `require` the class directly:

```php
require __DIR__ . '/src/AskPayClient.php';
```

## License

[MIT](../../LICENSE) © 2026 Paying.co (Mojave Payment Technologies, LLC)

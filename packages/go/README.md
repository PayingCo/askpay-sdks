# AskPay Go SDK

Official Go client for **AskPay by Paying.co** — talk to the Paying.co REST API
to store orders, generate hosted-payment URLs, read payment status, and more.

Part of the [askpay-sdks](https://github.com/PayingCo/askpay-sdks) monorepo.

## Install

```bash
go get github.com/PayingCo/askpay-sdks/packages/go
```

Requires Go 1.21+. No third-party dependencies (standard library only).

## Authentication

Get a Paying.co API token from your dashboard. The token is a Laravel Sanctum
bearer token whose abilities gate each endpoint. Provide it via the
`PAYING_API_TOKEN` environment variable (recommended) or pass it explicitly.

```bash
export PAYING_API_TOKEN="your-token"
```

## Quickstart

```go
package main

import (
	"context"
	"fmt"

	"github.com/PayingCo/askpay-sdks/packages/go/askpay"
)

func main() {
	ctx := context.Background()

	client, err := askpay.New() // reads PAYING_API_TOKEN
	// or: askpay.New(askpay.WithToken("…"), askpay.WithBaseURL("https://ask.paying.co/api"))
	if err != nil {
		panic(err)
	}

	me, _ := client.WhoAmI(ctx)
	fmt.Printf("Authenticated as %s (%s)\n", me.Name, me.Hash)

	res, _ := client.CreatePaymentURL(ctx, "INV-2041") // existing order ref
	fmt.Println(res.URL)

	if paid, _ := client.IsPaid(ctx, "INV-2041"); paid {
		p, _ := client.ReadOrder(ctx, "INV-2041")
		fmt.Println(p.Amount, p.CardType)
	}
}
```

A runnable end-to-end program lives in [`examples/main.go`](examples/main.go):

```bash
PAYING_API_TOKEN="…" go run ./examples
```

## Methods

| Method | Endpoint | Token ability |
| ------ | -------- | ------------- |
| `StoreOrder(ctx, order)` | `POST /order/store` | integrator token |
| `CancelOrder(ctx, ref)` | `POST /order/cancel` | `Order - Cancel` |
| `ReadOrder(ctx, ref)` | `POST /order/read` | `Order - Read` |
| `IsPaid(ctx, ref)` | (uses `ReadOrder`) | `Order - Read` |
| `CreatePaymentURL(ctx, ref)` | `POST /url/create` | `Generate QR Code` |
| `SendSMS(ctx, ref, phone)` | `POST /smspay/send` | `Send SMS` |
| `WhoAmI(ctx)` | `GET /store` | `Store - Read` |
| `GetPolling(ctx)` | `GET /poll` | integrator token |

`StoreOrder` takes a `map[string]any` whose shape follows your integrator's
configured field mappings. `CreatePaymentURL` and `ReadOrder` operate on an
order that already exists — there is no "charge an arbitrary amount" call.

## Errors

Methods return a `*askpay.Error` carrying `Status`, `Code`, and `RequestID`.
Two helpers classify them:

- `askpay.IsValidation(err)` — client-side checks (missing reference, bad phone).
- `askpay.IsAPIError(err)` — the API returned HTTP 200 with an `{"error": ...}`
  body (e.g. "A payment hasn't been applied to this order yet"). `IsPaid`
  treats this as `false`.

```go
p, err := client.ReadOrder(ctx, "INV-2041")
if askpay.IsAPIError(err) {
	fmt.Println("not paid yet")
}
```

## Configuration

```go
askpay.New(
	askpay.WithToken("…"),                          // else PAYING_API_TOKEN
	askpay.WithBaseURL("https://ask.paying.co/api"), // default
	askpay.WithHTTPClient(customClient),             // custom timeouts/transport
	askpay.WithMaxRetries(2),                        // retries 429/5xx with backoff
)
```

## License

[MIT](../../LICENSE) © 2026 Paying.co (Mojave Payment Technologies, LLC)

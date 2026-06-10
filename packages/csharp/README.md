# Paying.AskPay

Official .NET client for **AskPay by Paying.co** — talk to the Paying.co REST API
to store orders, generate hosted-payment URLs, read payment status, and more.

Part of the [askpay-sdks](https://github.com/PayingCo/askpay-sdks) monorepo.

## Install

```bash
dotnet add package Paying.AskPay
```

Targets .NET 8 and .NET Standard 2.1.

## Authentication

Get a Paying.co API token from your dashboard. The token is a Laravel Sanctum
bearer token whose abilities gate each endpoint. Provide it via the
`PAYING_API_TOKEN` environment variable (recommended) or pass it explicitly.

```bash
export PAYING_API_TOKEN="your-token"
```

## Quickstart

```csharp
using Paying.AskPay;

using var askpay = new AskPayClient(); // reads PAYING_API_TOKEN
// or: new AskPayClient(new AskPayClientOptions { Token = "…", BaseUrl = "https://ask.paying.co/api" });

var me = await askpay.WhoAmIAsync();
Console.WriteLine($"Authenticated as {me.Name} ({me.Hash})");

var result = await askpay.CreatePaymentUrlAsync("INV-2041"); // existing order ref
Console.WriteLine(result.Url);

if (await askpay.IsPaidAsync("INV-2041"))
{
    var payment = await askpay.ReadOrderAsync("INV-2041");
    Console.WriteLine($"{payment.Amount} {payment.CardType}");
}
```

A runnable end-to-end program lives in [`examples/Checkout.cs`](examples/Checkout.cs):

```bash
PAYING_API_TOKEN="…" dotnet run --project examples
```

## Methods

| Method | Endpoint | Token ability |
| ------ | -------- | ------------- |
| `StoreOrderAsync(order)` | `POST /order/store` | integrator token |
| `CancelOrderAsync(ref)` | `POST /order/cancel` | `Order - Cancel` |
| `ReadOrderAsync(ref)` | `POST /order/read` | `Order - Read` |
| `IsPaidAsync(ref)` | (uses `ReadOrderAsync`) | `Order - Read` |
| `CreatePaymentUrlAsync(ref)` | `POST /url/create` | `Generate QR Code` |
| `SendSmsAsync(ref, phone)` | `POST /smspay/send` | `Send SMS` |
| `WhoAmIAsync()` | `GET /store` | `Store - Read` |
| `GetPollingAsync()` | `GET /poll` | integrator token |

`StoreOrderAsync` takes an `IDictionary<string, object?>` whose shape follows
your integrator's configured field mappings. `CreatePaymentUrlAsync` and
`ReadOrderAsync` operate on an order that already exists — there is no "charge
an arbitrary amount" call. All methods accept an optional `CancellationToken`.

## Errors

All failures throw a subclass of `PayingException`:

- `PayingValidationException` — client-side checks (missing reference, bad phone).
- `PayingApiException` — the API returned HTTP 200 with an `{ "error": ... }`
  body (e.g. "A payment hasn't been applied to this order yet"). `IsPaidAsync`
  treats this as `false`.
- `PayingException` — transport/HTTP errors, carrying `Status`, `Code`, `RequestId`.

```csharp
try
{
    await askpay.ReadOrderAsync("INV-2041");
}
catch (PayingApiException)
{
    Console.WriteLine("not paid yet");
}
```

## Configuration

```csharp
new AskPayClient(new AskPayClientOptions
{
    Token = "…",                          // else PAYING_API_TOKEN
    BaseUrl = "https://ask.paying.co/api", // default
    Timeout = TimeSpan.FromSeconds(15),    // default
    MaxRetries = 2,                        // retries 429/5xx with backoff
});
```

You can also pass your own `HttpClient` as the second constructor argument
(recommended for dependency-injection / `IHttpClientFactory` scenarios).

## License

[MIT](../../LICENSE) © 2026 Paying.co (Mojave Payment Technologies, LLC)

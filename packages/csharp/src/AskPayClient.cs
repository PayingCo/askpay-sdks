// AskPay SDK — C# client for the AskPay by Paying.co REST API.
//
// Copyright (c) 2026 Paying.co (Mojave Payment Technologies, LLC)
// Released under the MIT License. See LICENSE for details.
//
// Mirrors the live Laravel REST API (routes/api.php), authenticated with a
// Laravel Sanctum bearer token. Each method maps to a real endpoint:
//
//   StoreOrderAsync        POST /api/order/store     (integrator token)
//   CancelOrderAsync       POST /api/order/cancel    ability: "Order - Cancel"
//   ReadOrderAsync         POST /api/order/read      ability: "Order - Read"
//   CreatePaymentUrlAsync  POST /api/url/create      ability: "Generate QR Code"
//   SendSmsAsync           POST /api/smspay/send     ability: "Send SMS"
//   WhoAmIAsync            GET  /api/store           ability: "Store - Read"
//   GetPollingAsync        GET  /api/poll            (integrator token)
//
// Auth is a Paying.co API token. Never hard-code tokens in committed source —
// read from the environment (PAYING_API_TOKEN) or pass to the constructor.
//
// Note: the API frequently returns HTTP 200 with an { "error": ... } body
// rather than a non-2xx status, so this client inspects the payload and throws
// PayingApiException when an "error" key is present.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Paying.AskPay;

// ── Result types ────────────────────────────────────────────────────────────

public sealed record OrderMutationResult
{
    [JsonPropertyName("success")] public string Success { get; init; } = "";
    [JsonPropertyName("merchant_reference")] public string MerchantReference { get; init; } = "";
}

public sealed record PaymentRecord
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("user_id")] public long UserId { get; init; }
    [JsonPropertyName("order_id")] public string OrderId { get; init; } = "";
    [JsonPropertyName("amount")] public decimal? Amount { get; init; }
    [JsonPropertyName("tip")] public decimal? Tip { get; init; }
    [JsonPropertyName("terminal_id")] public long? TerminalId { get; init; }
    [JsonPropertyName("terminal_type")] public int TerminalType { get; init; }
    [JsonPropertyName("transaction_type")] public int TransactionType { get; init; }
    [JsonPropertyName("region")] public string? Region { get; init; }
    [JsonPropertyName("city")] public string? City { get; init; }
    [JsonPropertyName("response_code")] public string? ResponseCode { get; init; }
    [JsonPropertyName("unique_reference")] public string? UniqueReference { get; init; }
    [JsonPropertyName("card_number")] public string? CardNumber { get; init; }
    [JsonPropertyName("card_type")] public string? CardType { get; init; }
    [JsonPropertyName("date_time")] public string? DateTime { get; init; }
    [JsonPropertyName("created_at")] public string? CreatedAt { get; init; }
    [JsonPropertyName("updated_at")] public string? UpdatedAt { get; init; }
}

public sealed record PaymentUrlResult
{
    [JsonPropertyName("url")] public string Url { get; init; } = "";
}

public sealed record WhoAmIResult
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("email")] public string Email { get; init; } = "";
    [JsonPropertyName("phone")] public string Phone { get; init; } = "";
    [JsonPropertyName("hash")] public string Hash { get; init; } = "";
}

// ── Errors ──────────────────────────────────────────────────────────────────

public class PayingException : Exception
{
    public int Status { get; }
    public string? Code { get; }
    public string? RequestId { get; }

    public PayingException(string message, int status = 0, string? code = null, string? requestId = null)
        : base(message)
    {
        Status = status;
        Code = code;
        RequestId = requestId;
    }
}

public sealed class PayingValidationException : PayingException
{
    public PayingValidationException(string message) : base(message, 0, "validation_error") { }
}

/// <summary>Thrown when the API returns 200 with an { "error": ... } body.</summary>
public sealed class PayingApiException : PayingException
{
    public PayingApiException(string message, string? requestId = null)
        : base(message, 200, "api_error", requestId) { }
}

// ── Options ─────────────────────────────────────────────────────────────────

public sealed class AskPayClientOptions
{
    public string? Token { get; set; }
    public string BaseUrl { get; set; } = "https://ask.paying.co/api";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);
    public int MaxRetries { get; set; } = 2;
}

// ── Client ──────────────────────────────────────────────────────────────────

public sealed class AskPayClient : IDisposable
{
    private static readonly Regex PhoneRe = new(@"^[0-9]{10}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _token;
    private readonly string _baseUrl;
    private readonly int _maxRetries;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public AskPayClient(AskPayClientOptions? options = null, HttpClient? httpClient = null)
    {
        options ??= new AskPayClientOptions();
        var token = options.Token ?? Environment.GetEnvironmentVariable("PAYING_API_TOKEN");
        if (string.IsNullOrEmpty(token))
            throw new PayingValidationException("Missing API token. Set options.Token or PAYING_API_TOKEN.");

        _token = token;
        _baseUrl = options.BaseUrl.TrimEnd('/');
        _maxRetries = options.MaxRetries;
        _http = httpClient ?? new HttpClient();
        _ownsHttp = httpClient is null;
        _http.Timeout = options.Timeout;
    }

    public AskPayClient(string token) : this(new AskPayClientOptions { Token = token }) { }

    // -- Orders ------------------------------------------------------------ //

    /// <summary>Store (create or update) an order. Body shape follows your integrator mappings.</summary>
    public Task<OrderMutationResult> StoreOrderAsync(
        IDictionary<string, object?> order, CancellationToken ct = default)
        => RequestAsync<OrderMutationResult>(HttpMethod.Post, "/order/store", order, ct);

    /// <summary>Cancel (delete) an order. Requires the "Order - Cancel" ability.</summary>
    public Task<OrderMutationResult> CancelOrderAsync(
        string merchantReference, CancellationToken ct = default)
    {
        RequireRef(merchantReference);
        return RequestAsync<OrderMutationResult>(HttpMethod.Post, "/order/cancel",
            new Dictionary<string, object?> { ["merchant_reference"] = merchantReference }, ct);
    }

    /// <summary>
    /// Read the payment applied to an order. Requires "Order - Read".
    /// Throws PayingApiException until a payment exists.
    /// </summary>
    public Task<PaymentRecord> ReadOrderAsync(
        string merchantReference, CancellationToken ct = default)
    {
        RequireRef(merchantReference);
        return RequestAsync<PaymentRecord>(HttpMethod.Post, "/order/read",
            new Dictionary<string, object?> { ["merchant_reference"] = merchantReference }, ct);
    }

    /// <summary>True if a payment has been applied to the order, false otherwise.</summary>
    public async Task<bool> IsPaidAsync(string merchantReference, CancellationToken ct = default)
    {
        try
        {
            await ReadOrderAsync(merchantReference, ct).ConfigureAwait(false);
            return true;
        }
        catch (PayingApiException)
        {
            return false;
        }
    }

    // -- Payment URL & delivery -------------------------------------------- //

    /// <summary>
    /// Generate a hosted-payment URL for an existing order reference.
    /// Requires the "Generate QR Code" ability.
    /// </summary>
    public Task<PaymentUrlResult> CreatePaymentUrlAsync(
        string merchantReference, CancellationToken ct = default)
    {
        RequireRef(merchantReference);
        return RequestAsync<PaymentUrlResult>(HttpMethod.Post, "/url/create",
            new Dictionary<string, object?> { ["merchant_reference"] = merchantReference }, ct);
    }

    /// <summary>
    /// Send an SMS payment link via the merchant's Twilio config.
    /// Requires the "Send SMS" ability. phoneNumber must be 10 digits.
    /// </summary>
    public Task<Dictionary<string, JsonElement>> SendSmsAsync(
        string merchantReference, string phoneNumber, CancellationToken ct = default)
    {
        RequireRef(merchantReference);
        if (!PhoneRe.IsMatch(phoneNumber))
            throw new PayingValidationException("phone_number must be 10 digits");
        return RequestAsync<Dictionary<string, JsonElement>>(HttpMethod.Post, "/smspay/send",
            new Dictionary<string, object?>
            {
                ["merchant_reference"] = merchantReference,
                ["phone_number"] = phoneNumber,
            }, ct);
    }

    // -- Account & polling ------------------------------------------------- //

    /// <summary>Return the integrator profile bound to this token. Requires "Store - Read".</summary>
    public Task<WhoAmIResult> WhoAmIAsync(CancellationToken ct = default)
        => RequestAsync<WhoAmIResult>(HttpMethod.Get, "/store", null, ct);

    /// <summary>Return the polling configuration for this integrator.</summary>
    public Task<JsonElement> GetPollingAsync(CancellationToken ct = default)
        => RequestAsync<JsonElement>(HttpMethod.Get, "/poll", null, ct);

    // -- Helpers ----------------------------------------------------------- //

    private static void RequireRef(string merchantReference)
    {
        if (string.IsNullOrEmpty(merchantReference))
            throw new PayingValidationException("merchant_reference is required");
    }

    private async Task<T> RequestAsync<T>(
        HttpMethod method, string path, object? body, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var req = new HttpRequestMessage(method, _baseUrl + path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (body is not null)
            {
                req.Content = new StringContent(
                    JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
            }

            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < _maxRetries)
            {
                await Task.Delay(Backoff(attempt), ct).ConfigureAwait(false);
                continue;
            }

            var content = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var requestId = resp.Headers.TryGetValues("x-request-id", out var ids)
                ? string.Join("", ids) : null;

            if ((resp.StatusCode == (HttpStatusCode)429 || (int)resp.StatusCode >= 500)
                && attempt < _maxRetries)
            {
                await Task.Delay(Backoff(attempt), ct).ConfigureAwait(false);
                continue;
            }

            if (!resp.IsSuccessStatusCode)
                throw ParseError(content, (int)resp.StatusCode, requestId);

            // The API may return 200 with an { "error": ... } body.
            if (!string.IsNullOrEmpty(content))
            {
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object
                        && doc.RootElement.TryGetProperty("error", out var errEl))
                    {
                        throw new PayingApiException(errEl.GetString() ?? "API error", requestId);
                    }
                }
                catch (JsonException)
                {
                    // Non-JSON 2xx body (e.g. "success"); fall through to deserialize.
                }
            }

            return string.IsNullOrEmpty(content)
                ? default!
                : JsonSerializer.Deserialize<T>(content, JsonOpts)!;
        }
    }

    private static PayingException ParseError(string content, int status, string? requestId)
    {
        string? message = null, code = null;
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("error", out var e)) message = e.GetString();
            else if (doc.RootElement.TryGetProperty("message", out var m)) message = m.GetString();
            if (doc.RootElement.TryGetProperty("code", out var c)) code = c.GetString();
        }
        catch (JsonException) { /* non-JSON error body */ }

        if (status == (int)HttpStatusCode.Forbidden)
        {
            code = "forbidden";
            message ??= "This token is not allowed to access this endpoint.";
        }
        return new PayingException(message ?? $"Request failed with {status}", status, code, requestId);
    }

    private static TimeSpan Backoff(int attempt)
        => TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt));

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}

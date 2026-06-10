/**
 * AskPay SDK — TypeScript client for the AskPay by Paying.co REST API
 *
 * Copyright (c) 2026 Paying.co (Mojave Payment Technologies, LLC)
 * Released under the MIT License. See LICENSE for details.
 *
 * Mirrors the live Laravel REST API (routes/api.php), authenticated with a
 * Laravel Sanctum bearer token. Each method maps to a real endpoint:
 *
 *   storeOrder        POST /api/order/store     (integrator token)
 *   cancelOrder       POST /api/order/cancel    ability: "Order - Cancel"
 *   readOrder         POST /api/order/read      ability: "Order - Read"
 *   createPaymentUrl  POST /api/url/create      ability: "Generate QR Code"
 *   sendSms           POST /api/smspay/send     ability: "Send SMS"
 *   whoami            GET  /api/store           ability: "Store - Read"
 *   getPolling        GET  /api/poll            (integrator token)
 *
 * Auth is a Paying.co API token. Never paste tokens inline in shared code —
 * read from the environment (PAYING_API_TOKEN) or pass to the constructor.
 *
 * Note: the API frequently returns HTTP 200 with an { error } body rather than
 * a non-2xx status, so this client inspects the payload and raises PayingError
 * when an "error" key is present.
 */

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface PayingClientOptions {
  /** Paying.co API token. Falls back to process.env.PAYING_API_TOKEN. */
  token?: string;
  /** API base URL, including the /api prefix. Defaults to production. */
  baseUrl?: string;
  /** Per-request timeout in ms. Default 15000. */
  timeoutMs?: number;
  /** Max automatic retries on 429/5xx. Default 2. */
  maxRetries?: number;
}

/** Result of storing or cancelling an order. */
export interface OrderMutationResult {
  success: string;
  merchant_reference: string;
}

/** A payment record as returned by /order/read once a payment has settled. */
export interface PaymentRecord {
  id: number;
  user_id: number;
  order_id: string;
  amount: number | null;
  tip: number | null;
  terminal_id: number | null;
  terminal_type: number;
  transaction_type: number;
  region: string | null;
  city: string | null;
  response_code: string | null;
  unique_reference: string | null;
  card_number: string | null;
  card_type: string | null;
  date_time: string | null;
  created_at: string | null;
  updated_at: string | null;
}

/** { url } returned by /url/create — a hosted quikpay.to payment page. */
export interface PaymentUrlResult {
  url: string;
}

/** The integrator profile returned by /store, plus the injected Hashids `hash`. */
export interface WhoAmIResult {
  id: number;
  name: string;
  email: string;
  phone: string;
  hash: string;
  active: boolean | number;
  api_order_url?: string | null;
  [key: string]: unknown;
}

// ─────────────────────────────────────────────────────────────────────────────
// Errors
// ─────────────────────────────────────────────────────────────────────────────

export class PayingError extends Error {
  readonly status: number;
  readonly code?: string;
  readonly requestId?: string;

  constructor(message: string, status: number, code?: string, requestId?: string) {
    super(message);
    this.name = "PayingError";
    this.status = status;
    this.code = code;
    this.requestId = requestId;
  }
}

export class PayingValidationError extends PayingError {
  constructor(message: string) {
    super(message, 0, "validation_error");
    this.name = "PayingValidationError";
  }
}

/** Thrown when the API returns 200 with an { error } body (its common pattern). */
export class PayingApiError extends PayingError {
  constructor(message: string, requestId?: string) {
    super(message, 200, "api_error", requestId);
    this.name = "PayingApiError";
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

const PHONE_RE = /^[0-9]{10}$/;

function requireRef(merchantReference: string): void {
  if (!merchantReference || typeof merchantReference !== "string") {
    throw new PayingValidationError("merchant_reference is required");
  }
}

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

// ─────────────────────────────────────────────────────────────────────────────
// Client
// ─────────────────────────────────────────────────────────────────────────────

export class AskPayClient {
  private readonly token: string;
  private readonly baseUrl: string;
  private readonly timeoutMs: number;
  private readonly maxRetries: number;

  constructor(opts: PayingClientOptions = {}) {
    const token = opts.token ?? process.env.PAYING_API_TOKEN;
    if (!token) {
      throw new PayingValidationError(
        "Missing API token. Pass { token } or set PAYING_API_TOKEN.",
      );
    }
    this.token = token;
    this.baseUrl = (opts.baseUrl ?? "https://ask.paying.co/api").replace(/\/$/, "");
    this.timeoutMs = opts.timeoutMs ?? 15_000;
    this.maxRetries = opts.maxRetries ?? 2;
  }

  // ── Orders ──────────────────────────────────────────────────────────────

  /**
   * Store (create or update) an order. The body shape is defined by your
   * integrator's configured request/response field mappings; the API resolves
   * a merchant_reference from it. Returns { success, merchant_reference }.
   */
  async storeOrder(order: Record<string, unknown>): Promise<OrderMutationResult> {
    return this.request<OrderMutationResult>("POST", "/order/store", order);
  }

  /** Cancel (delete) an order by reference. Requires the "Order - Cancel" ability. */
  async cancelOrder(merchantReference: string): Promise<OrderMutationResult> {
    requireRef(merchantReference);
    return this.request<OrderMutationResult>("POST", "/order/cancel", {
      merchant_reference: merchantReference,
    });
  }

  /**
   * Read the payment applied to an order. Requires the "Order - Read" ability.
   * Returns the PaymentRecord once a payment exists; throws PayingApiError with
   * "A payment hasn't been applied to this order yet" until then.
   */
  async readOrder(merchantReference: string): Promise<PaymentRecord> {
    requireRef(merchantReference);
    return this.request<PaymentRecord>("POST", "/order/read", {
      merchant_reference: merchantReference,
    });
  }

  /** Convenience: true if a payment has been applied to the order, false otherwise. */
  async isPaid(merchantReference: string): Promise<boolean> {
    try {
      await this.readOrder(merchantReference);
      return true;
    } catch (err) {
      if (err instanceof PayingApiError) return false;
      throw err;
    }
  }

  // ── Payment URL & delivery ──────────────────────────────────────────────

  /**
   * Generate a hosted-payment URL for an existing order reference.
   * Requires the "Generate QR Code" ability. Returns { url }. Render your own
   * QR code from the URL if you need one.
   */
  async createPaymentUrl(merchantReference: string): Promise<PaymentUrlResult> {
    requireRef(merchantReference);
    return this.request<PaymentUrlResult>("POST", "/url/create", {
      merchant_reference: merchantReference,
    });
  }

  /**
   * Send an SMS payment link via the merchant's Twilio config.
   * Requires the "Send SMS" ability. phoneNumber must be 10 digits.
   */
  async sendSms(
    merchantReference: string,
    phoneNumber: string,
  ): Promise<{ success: string }> {
    requireRef(merchantReference);
    if (!PHONE_RE.test(phoneNumber)) {
      throw new PayingValidationError("phone_number must be 10 digits");
    }
    return this.request<{ success: string }>("POST", "/smspay/send", {
      merchant_reference: merchantReference,
      phone_number: phoneNumber,
    });
  }

  // ── Account & polling ───────────────────────────────────────────────────

  /** Return the integrator profile bound to this token. Requires "Store - Read". */
  async whoami(): Promise<WhoAmIResult> {
    return this.request<WhoAmIResult>("GET", "/store");
  }

  /** Return the polling configuration for this integrator. */
  async getPolling(): Promise<unknown> {
    return this.request<unknown>("GET", "/poll");
  }

  // ── Transport ───────────────────────────────────────────────────────────

  private async request<T>(method: string, path: string, body?: unknown): Promise<T> {
    const url = `${this.baseUrl}${path}`;
    let attempt = 0;

    while (true) {
      const controller = new AbortController();
      const timer = setTimeout(() => controller.abort(), this.timeoutMs);

      try {
        const res = await fetch(url, {
          method,
          headers: {
            Authorization: `Bearer ${this.token}`,
            "Content-Type": "application/json",
            Accept: "application/json",
          },
          body: body !== undefined ? JSON.stringify(body) : undefined,
          signal: controller.signal,
        });

        const requestId = res.headers.get("x-request-id") ?? undefined;

        if ((res.status === 429 || res.status >= 500) && attempt < this.maxRetries) {
          attempt++;
          await sleep(250 * 2 ** (attempt - 1));
          continue;
        }

        const text = await res.text();
        let payload: unknown;
        try {
          payload = text ? JSON.parse(text) : {};
        } catch {
          // Non-JSON 2xx bodies (e.g. /poll/order returns the string "success").
          if (res.ok) return text as unknown as T;
          throw new PayingError(text || `Request failed with ${res.status}`, res.status, undefined, requestId);
        }

        if (!res.ok) {
          const msg =
            (payload as { error?: string; message?: string }).error ??
            (payload as { message?: string }).message ??
            `Request failed with ${res.status}`;
          if (res.status === 403) {
            throw new PayingError(
              msg || "This token is not allowed to access this endpoint.",
              403,
              "forbidden",
              requestId,
            );
          }
          throw new PayingError(msg, res.status, undefined, requestId);
        }

        // The API returns 200 with an { error } body for many failure modes.
        if (payload && typeof payload === "object" && "error" in payload) {
          throw new PayingApiError(String((payload as { error: unknown }).error), requestId);
        }

        return payload as T;
      } catch (err) {
        if (err instanceof PayingError) throw err;
        if ((err as Error).name === "AbortError" && attempt < this.maxRetries) {
          attempt++;
          continue;
        }
        throw new PayingError((err as Error).message, 0, "network_error");
      } finally {
        clearTimeout(timer);
      }
    }
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Usage
// ─────────────────────────────────────────────────────────────────────────────
//
//   const askpay = new AskPayClient(); // reads PAYING_API_TOKEN
//
//   const me  = await askpay.whoami();
//   const ref = "INV-2041"; // an existing order reference
//   const { url } = await askpay.createPaymentUrl(ref);
//
//   if (await askpay.isPaid(ref)) {
//     const payment = await askpay.readOrder(ref);
//     console.log(payment.amount, payment.card_type);
//   }

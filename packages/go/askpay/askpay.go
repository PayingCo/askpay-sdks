// Package askpay is the official Go client for the AskPay by Paying.co REST API.
//
// Copyright (c) 2026 Paying.co (Mojave Payment Technologies, LLC)
// Released under the MIT License. See LICENSE for details.
//
// It mirrors the live Laravel REST API (routes/api.php), authenticated with a
// Laravel Sanctum bearer token. Each method maps to a real endpoint:
//
//	StoreOrder       POST /api/order/store     (integrator token)
//	CancelOrder      POST /api/order/cancel    ability: "Order - Cancel"
//	ReadOrder        POST /api/order/read      ability: "Order - Read"
//	CreatePaymentURL POST /api/url/create      ability: "Generate QR Code"
//	SendSMS          POST /api/smspay/send     ability: "Send SMS"
//	WhoAmI           GET  /api/store           ability: "Store - Read"
//	GetPolling       GET  /api/poll            (integrator token)
//
// Auth is a Paying.co API token. Never hard-code tokens in committed source —
// read from the environment (PAYING_API_TOKEN) or pass via Option.
//
// Note: the API frequently returns HTTP 200 with an {"error": ...} body rather
// than a non-2xx status, so this client inspects the payload and returns an
// APIError when an "error" key is present.
package askpay

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"math"
	"net/http"
	"os"
	"regexp"
	"time"
)

var phoneRE = regexp.MustCompile(`^[0-9]{10}$`)

// ── Result types ────────────────────────────────────────────────────────────

// OrderMutationResult is returned by StoreOrder and CancelOrder.
type OrderMutationResult struct {
	Success           string `json:"success"`
	MerchantReference string `json:"merchant_reference"`
}

// PaymentRecord is returned by ReadOrder once a payment has been applied.
type PaymentRecord struct {
	ID              int64    `json:"id"`
	UserID          int64    `json:"user_id"`
	OrderID         string   `json:"order_id"`
	Amount          *float64 `json:"amount"`
	Tip             *float64 `json:"tip"`
	TerminalID      *int64   `json:"terminal_id"`
	TerminalType    int      `json:"terminal_type"`
	TransactionType int      `json:"transaction_type"`
	Region          *string  `json:"region"`
	City            *string  `json:"city"`
	ResponseCode    *string  `json:"response_code"`
	UniqueReference *string  `json:"unique_reference"`
	CardNumber      *string  `json:"card_number"`
	CardType        *string  `json:"card_type"`
	DateTime        *string  `json:"date_time"`
	CreatedAt       *string  `json:"created_at"`
	UpdatedAt       *string  `json:"updated_at"`
}

// PaymentURLResult is returned by CreatePaymentURL.
type PaymentURLResult struct {
	URL string `json:"url"`
}

// WhoAmIResult is the integrator profile returned by /store, including the
// injected Hashids "hash". Extra fields are captured in Raw.
type WhoAmIResult struct {
	ID     int64           `json:"id"`
	Name   string          `json:"name"`
	Email  string          `json:"email"`
	Phone  string          `json:"phone"`
	Hash   string          `json:"hash"`
	Active json.RawMessage `json:"active"`
	Raw    map[string]any  `json:"-"`
}

// ── Errors ──────────────────────────────────────────────────────────────────

type Error struct {
	Message   string
	Status    int
	Code      string
	RequestID string
}

func (e *Error) Error() string {
	return fmt.Sprintf("askpay: [%d] %s: %s", e.Status, e.Code, e.Message)
}

// IsValidation reports whether err is a client-side validation error.
func IsValidation(err error) bool {
	var e *Error
	return errors.As(err, &e) && e.Code == "validation_error"
}

// IsAPIError reports whether err is an API-level error (200 with {"error":...}).
func IsAPIError(err error) bool {
	var e *Error
	return errors.As(err, &e) && e.Code == "api_error"
}

func validationError(msg string) *Error {
	return &Error{Message: msg, Status: 0, Code: "validation_error"}
}

// ── Client ──────────────────────────────────────────────────────────────────

type Client struct {
	token      string
	baseURL    string
	httpClient *http.Client
	maxRetries int
}

type Option func(*Client)

func WithToken(token string) Option        { return func(c *Client) { c.token = token } }
func WithBaseURL(u string) Option          { return func(c *Client) { c.baseURL = u } }
func WithHTTPClient(h *http.Client) Option { return func(c *Client) { c.httpClient = h } }
func WithMaxRetries(n int) Option          { return func(c *Client) { c.maxRetries = n } }

// New constructs a Client. Returns an error if no token is available.
func New(opts ...Option) (*Client, error) {
	c := &Client{
		token:      os.Getenv("PAYING_API_TOKEN"),
		baseURL:    "https://ask.paying.co/api",
		httpClient: &http.Client{Timeout: 15 * time.Second},
		maxRetries: 2,
	}
	for _, opt := range opts {
		opt(c)
	}
	if c.token == "" {
		return nil, validationError("missing API token: use WithToken or set PAYING_API_TOKEN")
	}
	return c, nil
}

// ── Orders ────────────────────────────────────────────────────────────────

// StoreOrder stores (creates or updates) an order. The body shape is defined
// by your integrator's configured field mappings.
func (c *Client) StoreOrder(ctx context.Context, order map[string]any) (*OrderMutationResult, error) {
	var out OrderMutationResult
	if err := c.do(ctx, http.MethodPost, "/order/store", order, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// CancelOrder cancels (deletes) an order. Requires the "Order - Cancel" ability.
func (c *Client) CancelOrder(ctx context.Context, merchantReference string) (*OrderMutationResult, error) {
	if merchantReference == "" {
		return nil, validationError("merchant_reference is required")
	}
	var out OrderMutationResult
	if err := c.do(ctx, http.MethodPost, "/order/cancel",
		map[string]any{"merchant_reference": merchantReference}, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// ReadOrder reads the payment applied to an order. Requires "Order - Read".
// Returns an APIError until a payment exists.
func (c *Client) ReadOrder(ctx context.Context, merchantReference string) (*PaymentRecord, error) {
	if merchantReference == "" {
		return nil, validationError("merchant_reference is required")
	}
	var out PaymentRecord
	if err := c.do(ctx, http.MethodPost, "/order/read",
		map[string]any{"merchant_reference": merchantReference}, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// IsPaid reports whether a payment has been applied to the order.
func (c *Client) IsPaid(ctx context.Context, merchantReference string) (bool, error) {
	_, err := c.ReadOrder(ctx, merchantReference)
	if err == nil {
		return true, nil
	}
	if IsAPIError(err) {
		return false, nil
	}
	return false, err
}

// ── Payment URL & delivery ──────────────────────────────────────────────────

// CreatePaymentURL generates a hosted-payment URL for an existing order
// reference. Requires the "Generate QR Code" ability.
func (c *Client) CreatePaymentURL(ctx context.Context, merchantReference string) (*PaymentURLResult, error) {
	if merchantReference == "" {
		return nil, validationError("merchant_reference is required")
	}
	var out PaymentURLResult
	if err := c.do(ctx, http.MethodPost, "/url/create",
		map[string]any{"merchant_reference": merchantReference}, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// SendSMS texts a payment link via the merchant's Twilio config. Requires the
// "Send SMS" ability. phoneNumber must be 10 digits.
func (c *Client) SendSMS(ctx context.Context, merchantReference, phoneNumber string) (map[string]any, error) {
	if merchantReference == "" {
		return nil, validationError("merchant_reference is required")
	}
	if !phoneRE.MatchString(phoneNumber) {
		return nil, validationError("phone_number must be 10 digits")
	}
	var out map[string]any
	if err := c.do(ctx, http.MethodPost, "/smspay/send",
		map[string]any{"merchant_reference": merchantReference, "phone_number": phoneNumber}, &out); err != nil {
		return nil, err
	}
	return out, nil
}

// ── Account & polling ────────────────────────────────────────────────────────

// WhoAmI returns the integrator profile bound to this token. Requires "Store - Read".
func (c *Client) WhoAmI(ctx context.Context) (*WhoAmIResult, error) {
	var raw map[string]any
	if err := c.do(ctx, http.MethodGet, "/store", nil, &raw); err != nil {
		return nil, err
	}
	// Re-marshal into the typed struct, keeping the full map in Raw.
	b, _ := json.Marshal(raw)
	var out WhoAmIResult
	_ = json.Unmarshal(b, &out)
	out.Raw = raw
	return &out, nil
}

// GetPolling returns the polling configuration for this integrator.
func (c *Client) GetPolling(ctx context.Context) (any, error) {
	var out any
	if err := c.do(ctx, http.MethodGet, "/poll", nil, &out); err != nil {
		return nil, err
	}
	return out, nil
}

// ── Transport ─────────────────────────────────────────────────────────────

func (c *Client) do(ctx context.Context, method, path string, body, out any) error {
	var payload []byte
	if body != nil {
		var err error
		if payload, err = json.Marshal(body); err != nil {
			return &Error{Message: err.Error(), Code: "encode_error"}
		}
	}

	for attempt := 0; ; attempt++ {
		req, err := http.NewRequestWithContext(ctx, method, c.baseURL+path, bytes.NewReader(payload))
		if err != nil {
			return &Error{Message: err.Error(), Code: "request_error"}
		}
		req.Header.Set("Authorization", "Bearer "+c.token)
		req.Header.Set("Content-Type", "application/json")
		req.Header.Set("Accept", "application/json")

		resp, err := c.httpClient.Do(req)
		if err != nil {
			if attempt < c.maxRetries {
				time.Sleep(backoff(attempt))
				continue
			}
			return &Error{Message: err.Error(), Code: "network_error"}
		}

		data, _ := io.ReadAll(resp.Body)
		resp.Body.Close()
		reqID := resp.Header.Get("x-request-id")

		if (resp.StatusCode == http.StatusTooManyRequests || resp.StatusCode >= 500) && attempt < c.maxRetries {
			time.Sleep(backoff(attempt))
			continue
		}

		if resp.StatusCode < 200 || resp.StatusCode >= 300 {
			return parseErr(data, resp.StatusCode, reqID)
		}

		// 2xx: the API may still embed {"error": ...}.
		var probe map[string]json.RawMessage
		if json.Unmarshal(data, &probe) == nil {
			if e, ok := probe["error"]; ok {
				var msg string
				_ = json.Unmarshal(e, &msg)
				return &Error{Message: msg, Status: 200, Code: "api_error", RequestID: reqID}
			}
		}

		if out != nil && len(data) > 0 {
			if err := json.Unmarshal(data, out); err != nil {
				// Non-JSON 2xx body (e.g. "success"); store as string if possible.
				if sp, ok := out.(*any); ok {
					*sp = string(data)
					return nil
				}
				return &Error{Message: err.Error(), Status: resp.StatusCode, Code: "decode_error", RequestID: reqID}
			}
		}
		return nil
	}
}

func parseErr(data []byte, status int, reqID string) *Error {
	var detail struct {
		Error   string `json:"error"`
		Message string `json:"message"`
		Code    string `json:"code"`
	}
	_ = json.Unmarshal(data, &detail)
	msg := detail.Error
	if msg == "" {
		msg = detail.Message
	}
	code := detail.Code
	if status == http.StatusForbidden {
		code = "forbidden"
		if msg == "" {
			msg = "this token is not allowed to access this endpoint"
		}
	}
	if msg == "" {
		msg = fmt.Sprintf("request failed with %d", status)
	}
	return &Error{Message: msg, Status: status, Code: code, RequestID: reqID}
}

func backoff(attempt int) time.Duration {
	return time.Duration(250*math.Pow(2, float64(attempt))) * time.Millisecond
}

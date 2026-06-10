"""AskPay SDK — Python client for the AskPay by Paying.co REST API.

Copyright (c) 2026 Paying.co (Mojave Payment Technologies, LLC)
Released under the MIT License. See LICENSE for details.

Mirrors the live Laravel REST API (routes/api.php), authenticated with a
Laravel Sanctum bearer token. Each method maps to a real endpoint:

    store_order        POST /api/order/store     (integrator token)
    cancel_order       POST /api/order/cancel    ability: "Order - Cancel"
    read_order         POST /api/order/read      ability: "Order - Read"
    create_payment_url POST /api/url/create      ability: "Generate QR Code"
    send_sms           POST /api/smspay/send     ability: "Send SMS"
    whoami             GET  /api/store           ability: "Store - Read"
    get_polling        GET  /api/poll            (integrator token)

Auth is a Paying.co API token. Never hard-code tokens in committed source —
read from the environment (PAYING_API_TOKEN) or pass to the constructor.

Note: the API frequently returns HTTP 200 with an {"error": ...} body rather
than a non-2xx status, so this client inspects the payload and raises
PayingApiError when an "error" key is present.
"""

from __future__ import annotations

import json
import os
import re
import time
import urllib.error
import urllib.request
from typing import Any, Optional

__all__ = [
    "AskPayClient",
    "PayingError",
    "PayingValidationError",
    "PayingApiError",
]

_PHONE_RE = re.compile(r"^[0-9]{10}$")


# --------------------------------------------------------------------------- #
# Errors
# --------------------------------------------------------------------------- #
class PayingError(Exception):
    def __init__(self, message, status=0, code=None, request_id=None):
        super().__init__(message)
        self.status = status
        self.code = code
        self.request_id = request_id


class PayingValidationError(PayingError):
    def __init__(self, message):
        super().__init__(message, status=0, code="validation_error")


class PayingApiError(PayingError):
    """Raised when the API returns 200 with an {"error": ...} body."""

    def __init__(self, message, request_id=None):
        super().__init__(message, status=200, code="api_error", request_id=request_id)


# --------------------------------------------------------------------------- #
# Helpers
# --------------------------------------------------------------------------- #
def _require_ref(merchant_reference):
    if not merchant_reference or not isinstance(merchant_reference, str):
        raise PayingValidationError("merchant_reference is required")


# --------------------------------------------------------------------------- #
# Client
# --------------------------------------------------------------------------- #
class AskPayClient:
    """Client for the AskPay by Paying.co REST API."""

    def __init__(
        self,
        token: Optional[str] = None,
        *,
        base_url: str = "https://ask.paying.co/api",
        timeout: float = 15.0,
        max_retries: int = 2,
    ):
        token = token or os.environ.get("PAYING_API_TOKEN")
        if not token:
            raise PayingValidationError(
                "Missing API token. Pass token=... or set PAYING_API_TOKEN."
            )
        self._token = token
        self._base_url = base_url.rstrip("/")
        self._timeout = timeout
        self._max_retries = max_retries

    # -- Orders ------------------------------------------------------------- #
    def store_order(self, order: dict) -> dict:
        """Store (create or update) an order.

        The body shape is defined by your integrator's configured request/
        response field mappings; the API resolves a merchant_reference from it.
        Returns {"success": ..., "merchant_reference": ...}.
        """
        return self._request("POST", "/order/store", order)

    def cancel_order(self, merchant_reference: str) -> dict:
        """Cancel (delete) an order. Requires the "Order - Cancel" ability."""
        _require_ref(merchant_reference)
        return self._request("POST", "/order/cancel", {"merchant_reference": merchant_reference})

    def read_order(self, merchant_reference: str) -> dict:
        """Read the payment applied to an order. Requires "Order - Read".

        Returns the payment record once one exists; raises PayingApiError with
        "A payment hasn't been applied to this order yet" until then.
        """
        _require_ref(merchant_reference)
        return self._request("POST", "/order/read", {"merchant_reference": merchant_reference})

    def is_paid(self, merchant_reference: str) -> bool:
        """True if a payment has been applied to the order, False otherwise."""
        try:
            self.read_order(merchant_reference)
            return True
        except PayingApiError:
            return False

    # -- Payment URL & delivery -------------------------------------------- #
    def create_payment_url(self, merchant_reference: str) -> dict:
        """Generate a hosted-payment URL for an existing order reference.

        Requires the "Generate QR Code" ability. Returns {"url": ...}.
        Render your own QR code from the URL if you need one.
        """
        _require_ref(merchant_reference)
        return self._request("POST", "/url/create", {"merchant_reference": merchant_reference})

    def send_sms(self, merchant_reference: str, phone_number: str) -> dict:
        """Send an SMS payment link via the merchant's Twilio config.

        Requires the "Send SMS" ability. phone_number must be 10 digits.
        """
        _require_ref(merchant_reference)
        if not _PHONE_RE.match(phone_number):
            raise PayingValidationError("phone_number must be 10 digits")
        return self._request(
            "POST",
            "/smspay/send",
            {"merchant_reference": merchant_reference, "phone_number": phone_number},
        )

    # -- Account & polling -------------------------------------------------- #
    def whoami(self) -> dict:
        """Return the integrator profile bound to this token. Requires "Store - Read"."""
        return self._request("GET", "/store")

    def get_polling(self) -> Any:
        """Return the polling configuration for this integrator."""
        return self._request("GET", "/poll")

    # -- Transport ---------------------------------------------------------- #
    def _request(self, method: str, path: str, body: Optional[dict] = None) -> Any:
        url = f"{self._base_url}{path}"
        payload = json.dumps(body).encode() if body is not None else None
        attempt = 0

        while True:
            req = urllib.request.Request(url, data=payload, method=method)
            req.add_header("Authorization", f"Bearer {self._token}")
            req.add_header("Content-Type", "application/json")
            req.add_header("Accept", "application/json")
            try:
                with urllib.request.urlopen(req, timeout=self._timeout) as resp:
                    raw = resp.read().decode()
                    request_id = resp.headers.get("x-request-id")
                    return self._parse_ok(raw, request_id)
            except urllib.error.HTTPError as err:
                status = err.code
                request_id = err.headers.get("x-request-id")
                if (status == 429 or status >= 500) and attempt < self._max_retries:
                    attempt += 1
                    time.sleep(0.25 * (2 ** (attempt - 1)))
                    continue
                raw = err.read().decode() if err.fp else ""
                detail = {}
                try:
                    detail = json.loads(raw) if raw else {}
                except json.JSONDecodeError:
                    pass
                msg = detail.get("error") or detail.get("message") or f"Request failed with {status}"
                if status == 403:
                    raise PayingError(msg, status, "forbidden", request_id) from None
                raise PayingError(msg, status, None, request_id) from None
            except urllib.error.URLError as err:
                if attempt < self._max_retries:
                    attempt += 1
                    time.sleep(0.25 * (2 ** (attempt - 1)))
                    continue
                raise PayingError(str(err.reason), 0, "network_error") from None

    @staticmethod
    def _parse_ok(raw: str, request_id: Optional[str]) -> Any:
        if not raw:
            return {}
        try:
            data = json.loads(raw)
        except json.JSONDecodeError:
            # Non-JSON 2xx bodies (e.g. /poll/order returns the string "success").
            return raw
        # The API returns 200 with an {"error": ...} body for many failure modes.
        if isinstance(data, dict) and "error" in data:
            raise PayingApiError(str(data["error"]), request_id)
        return data

"""AskPay SDK — Example: create a payment URL & poll an existing order.

Copyright (c) 2026 Paying.co (Mojave Payment Technologies, LLC)
Released under the MIT License. See LICENSE for details.

Reflects the real REST API: orders are created/stored first (their shape is
driven by your integrator field mappings), then you generate a hosted URL,
optionally text it, and poll until a payment is applied.

Run:
    PAYING_API_TOKEN=sk_live_… python examples/checkout.py
"""

import sys
import time

from askpay import AskPayClient, PayingApiError, PayingError, PayingValidationError

MERCHANT_REFERENCE = "INV-2041"  # an order reference that already exists


def wait_for_payment(client, ref, attempts=6, interval=5.0):
    for i in range(attempts):
        if client.is_paid(ref):
            p = client.read_order(ref)
            print(f"  ✓ PAID — amount {p.get('amount')}, card {p.get('card_type', 'n/a')}, {p.get('date_time', '')}")
            return True
        print(f"  … no payment yet (check {i + 1}/{attempts})")
        if i < attempts - 1:
            time.sleep(interval)
    return False


def main():
    askpay = AskPayClient()  # reads PAYING_API_TOKEN

    # 1. Confirm the token's integrator account.
    me = askpay.whoami()
    print(f"◑ Authenticated as {me['name']} <{me['email']}> (hash: {me.get('hash')})")

    # 2. Generate a hosted-payment URL for an existing order reference.
    print(f"\n🔗 Generating payment URL for {MERCHANT_REFERENCE}…")
    result = askpay.create_payment_url(MERCHANT_REFERENCE)
    print(f"  URL: {result['url']}")

    # 3. Optionally text the link (needs the "Send SMS" ability).
    # askpay.send_sms(MERCHANT_REFERENCE, "7025551234")

    # 4. Poll until a payment is applied.
    print("\n◉ Polling for settlement…")
    if not wait_for_payment(askpay, MERCHANT_REFERENCE):
        print("  Still unpaid after polling window — follow up later.")


if __name__ == "__main__":
    try:
        main()
    except PayingValidationError as e:
        print(f"Validation error: {e}", file=sys.stderr)
        sys.exit(1)
    except PayingApiError as e:
        print(f"API error: {e}", file=sys.stderr)
        sys.exit(1)
    except PayingError as e:
        print(f"AskPay error [{e.status}] {e.code or ''}: {e}", file=sys.stderr)
        if e.request_id:
            print(f"  request id: {e.request_id}", file=sys.stderr)
        sys.exit(1)

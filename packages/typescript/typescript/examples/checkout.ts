/**
 * AskPay SDK — Example: create a payment URL & poll an existing order.
 *
 * Copyright (c) 2026 Paying.co (Mojave Payment Technologies, LLC)
 * Released under the MIT License. See LICENSE for details.
 *
 * Reflects the real REST API: orders are created/stored first (their shape is
 * driven by your integrator field mappings), then you generate a hosted URL,
 * optionally text it, and poll until a payment is applied.
 *
 * Run:
 *   PAYING_API_TOKEN=sk_live_… npx tsx examples/checkout.ts
 */

import { AskPayClient, PayingApiError, PayingError, PayingValidationError } from "../src/askpay-sdk";

const MERCHANT_REFERENCE = "INV-2041"; // an order reference that already exists

async function waitForPayment(client: AskPayClient, ref: string, attempts = 6, intervalMs = 5_000) {
  for (let i = 0; i < attempts; i++) {
    if (await client.isPaid(ref)) {
      const p = await client.readOrder(ref);
      console.log(`  ✓ PAID — amount ${p.amount}, card ${p.card_type ?? "n/a"}, ${p.date_time ?? ""}`);
      return true;
    }
    console.log(`  … no payment yet (check ${i + 1}/${attempts})`);
    if (i < attempts - 1) await new Promise((r) => setTimeout(r, intervalMs));
  }
  return false;
}

async function main() {
  const askpay = new AskPayClient(); // reads PAYING_API_TOKEN

  // 1. Confirm the token's integrator account.
  const me = await askpay.whoami();
  console.log(`◑ Authenticated as ${me.name} <${me.email}> (hash: ${me.hash})`);

  // 2. Generate a hosted-payment URL for an existing order reference.
  console.log(`\n🔗 Generating payment URL for ${MERCHANT_REFERENCE}…`);
  const { url } = await askpay.createPaymentUrl(MERCHANT_REFERENCE);
  console.log(`  URL: ${url}`);

  // 3. Optionally text the link to the customer (needs the "Send SMS" ability).
  // await askpay.sendSms(MERCHANT_REFERENCE, "7025551234");

  // 4. Poll until a payment is applied to the order.
  console.log("\n◉ Polling for settlement…");
  if (!await waitForPayment(askpay, MERCHANT_REFERENCE)) {
    console.log("  Still unpaid after polling window — follow up later.");
  }
}

main().catch((err) => {
  if (err instanceof PayingValidationError) console.error(`Validation error: ${err.message}`);
  else if (err instanceof PayingApiError) console.error(`API error: ${err.message}`);
  else if (err instanceof PayingError) console.error(`AskPay error [${err.status}] ${err.code ?? ""}: ${err.message}`);
  else console.error("Unexpected error:", err);
  process.exit(1);
});

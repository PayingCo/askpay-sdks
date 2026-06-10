<?php

/**
 * AskPay SDK — Example: create a payment URL & poll an existing order.
 *
 * Copyright (c) 2026 Paying.co (Mojave Payment Technologies, LLC)
 * Released under the MIT License. See LICENSE for details.
 *
 * Run:
 *   PAYING_API_TOKEN=sk_live_… php examples/checkout.php
 */

declare(strict_types=1);

require __DIR__ . '/../src/AskPayClient.php';

use Paying\AskPay\AskPayClient;
use Paying\AskPay\ApiException;
use Paying\AskPay\PayingException;
use Paying\AskPay\ValidationException;

const MERCHANT_REFERENCE = 'INV-2041'; // an order reference that already exists

function waitForPayment(AskPayClient $client, string $ref, int $attempts = 6, int $interval = 5): bool
{
    for ($i = 0; $i < $attempts; $i++) {
        if ($client->isPaid($ref)) {
            $p = $client->readOrder($ref);
            $amount = $p['amount'] ?? '?';
            $card = $p['card_type'] ?? 'n/a';
            echo "  ✓ PAID — amount {$amount}, card {$card}\n";
            return true;
        }
        echo "  … no payment yet (check " . ($i + 1) . "/{$attempts})\n";
        if ($i < $attempts - 1) {
            sleep($interval);
        }
    }
    return false;
}

try {
    $askpay = new AskPayClient(); // reads PAYING_API_TOKEN

    // 1. Confirm the token's integrator account.
    $me = $askpay->whoami();
    echo "◑ Authenticated as {$me['name']} <{$me['email']}> (hash: " . ($me['hash'] ?? '') . ")\n";

    // 2. Generate a hosted-payment URL for an existing order reference.
    echo "\n🔗 Generating payment URL for " . MERCHANT_REFERENCE . "…\n";
    $result = $askpay->createPaymentUrl(MERCHANT_REFERENCE);
    echo "  URL: {$result['url']}\n";

    // 3. Optionally text the link (needs the "Send SMS" ability).
    // $askpay->sendSms(MERCHANT_REFERENCE, '7025551234');

    // 4. Poll until a payment is applied.
    echo "\n◉ Polling for settlement…\n";
    if (!waitForPayment($askpay, MERCHANT_REFERENCE)) {
        echo "  Still unpaid after polling window — follow up later.\n";
    }
} catch (ValidationException $e) {
    fwrite(STDERR, "Validation error: {$e->getMessage()}\n");
    exit(1);
} catch (ApiException $e) {
    fwrite(STDERR, "API error: {$e->getMessage()}\n");
    exit(1);
} catch (PayingException $e) {
    fwrite(STDERR, "AskPay error [{$e->status}] {$e->code}: {$e->getMessage()}\n");
    exit(1);
}

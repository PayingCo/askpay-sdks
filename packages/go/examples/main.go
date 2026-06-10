// AskPay SDK — Example: create a payment URL & poll an existing order.
//
// Copyright (c) 2026 Paying.co (Mojave Payment Technologies, LLC)
// Released under the MIT License. See LICENSE for details.
//
// Run:
//
//	PAYING_API_TOKEN=sk_live_… go run ./examples
package main

import (
	"context"
	"errors"
	"fmt"
	"os"
	"time"

	"github.com/PayingCo/askpay-sdks/packages/go/askpay"
)

const merchantReference = "INV-2041" // an order reference that already exists

func waitForPayment(ctx context.Context, c *askpay.Client, ref string) bool {
	const attempts, interval = 6, 5 * time.Second
	for i := 0; i < attempts; i++ {
		paid, err := c.IsPaid(ctx, ref)
		if err == nil && paid {
			p, _ := c.ReadOrder(ctx, ref)
			amount, card := "?", "n/a"
			if p != nil {
				if p.Amount != nil {
					amount = fmt.Sprintf("%.2f", *p.Amount)
				}
				if p.CardType != nil {
					card = *p.CardType
				}
			}
			fmt.Printf("  ✓ PAID — amount %s, card %s\n", amount, card)
			return true
		}
		fmt.Printf("  … no payment yet (check %d/%d)\n", i+1, attempts)
		if i < attempts-1 {
			time.Sleep(interval)
		}
	}
	return false
}

func run() error {
	ctx := context.Background()

	client, err := askpay.New() // reads PAYING_API_TOKEN
	if err != nil {
		return err
	}

	// 1. Confirm the token's integrator account.
	me, err := client.WhoAmI(ctx)
	if err != nil {
		return err
	}
	fmt.Printf("◑ Authenticated as %s <%s> (hash: %s)\n", me.Name, me.Email, me.Hash)

	// 2. Generate a hosted-payment URL for an existing order reference.
	fmt.Printf("\n🔗 Generating payment URL for %s…\n", merchantReference)
	res, err := client.CreatePaymentURL(ctx, merchantReference)
	if err != nil {
		return err
	}
	fmt.Printf("  URL: %s\n", res.URL)

	// 3. Optionally text the link (needs the "Send SMS" ability).
	// _, _ = client.SendSMS(ctx, merchantReference, "7025551234")

	// 4. Poll until a payment is applied.
	fmt.Println("\n◉ Polling for settlement…")
	if !waitForPayment(ctx, client, merchantReference) {
		fmt.Println("  Still unpaid after polling window — follow up later.")
	}
	return nil
}

func main() {
	if err := run(); err != nil {
		var apiErr *askpay.Error
		if errors.As(err, &apiErr) {
			fmt.Fprintf(os.Stderr, "AskPay error [%d] %s: %s\n", apiErr.Status, apiErr.Code, apiErr.Message)
		} else {
			fmt.Fprintln(os.Stderr, "unexpected error:", err)
		}
		os.Exit(1)
	}
}

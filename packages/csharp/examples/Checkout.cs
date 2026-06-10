// AskPay SDK — Example: create a payment URL & poll an existing order.
//
// Copyright (c) 2026 Paying.co (Mojave Payment Technologies, LLC)
// Released under the MIT License. See LICENSE for details.
//
// Run:
//   PAYING_API_TOKEN=sk_live_… dotnet run --project examples

using System;
using System.Threading.Tasks;
using Paying.AskPay;

internal static class Program
{
    private const string MerchantReference = "INV-2041"; // an existing order reference

    private static async Task<bool> WaitForPaymentAsync(AskPayClient client, string reference)
    {
        const int attempts = 6;
        var interval = TimeSpan.FromSeconds(5);
        for (var i = 0; i < attempts; i++)
        {
            if (await client.IsPaidAsync(reference))
            {
                var p = await client.ReadOrderAsync(reference);
                Console.WriteLine($"  ✓ PAID — amount {p.Amount?.ToString("0.00") ?? "?"}, card {p.CardType ?? "n/a"}");
                return true;
            }
            Console.WriteLine($"  … no payment yet (check {i + 1}/{attempts})");
            if (i < attempts - 1) await Task.Delay(interval);
        }
        return false;
    }

    private static async Task<int> Main()
    {
        try
        {
            using var askpay = new AskPayClient(); // reads PAYING_API_TOKEN

            // 1. Confirm the token's integrator account.
            var me = await askpay.WhoAmIAsync();
            Console.WriteLine($"◑ Authenticated as {me.Name} <{me.Email}> (hash: {me.Hash})");

            // 2. Generate a hosted-payment URL for an existing order reference.
            Console.WriteLine($"\n🔗 Generating payment URL for {MerchantReference}…");
            var result = await askpay.CreatePaymentUrlAsync(MerchantReference);
            Console.WriteLine($"  URL: {result.Url}");

            // 3. Optionally text the link (needs the "Send SMS" ability).
            // await askpay.SendSmsAsync(MerchantReference, "7025551234");

            // 4. Poll until a payment is applied.
            Console.WriteLine("\n◉ Polling for settlement…");
            if (!await WaitForPaymentAsync(askpay, MerchantReference))
                Console.WriteLine("  Still unpaid after polling window — follow up later.");

            return 0;
        }
        catch (PayingValidationException ex)
        {
            Console.Error.WriteLine($"Validation error: {ex.Message}");
            return 1;
        }
        catch (PayingApiException ex)
        {
            Console.Error.WriteLine($"API error: {ex.Message}");
            return 1;
        }
        catch (PayingException ex)
        {
            Console.Error.WriteLine($"AskPay error [{ex.Status}] {ex.Code}: {ex.Message}");
            return 1;
        }
    }
}

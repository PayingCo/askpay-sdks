<?php

/**
 * AskPay SDK — PHP client for the AskPay by Paying.co REST API.
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
 * Auth is a Paying.co API token. Never hard-code tokens in committed source —
 * read from the environment (PAYING_API_TOKEN) or pass to the constructor.
 *
 * Note: the API frequently returns HTTP 200 with an { "error": ... } body
 * rather than a non-2xx status, so this client inspects the payload and throws
 * ApiException when an "error" key is present.
 */

declare(strict_types=1);

namespace Paying\AskPay;

class PayingException extends \Exception
{
    public int $status;
    public ?string $code;
    public ?string $requestId;

    public function __construct(string $message, int $status = 0, ?string $code = null, ?string $requestId = null)
    {
        parent::__construct($message);
        $this->status = $status;
        $this->code = $code;
        $this->requestId = $requestId;
    }
}

class ValidationException extends PayingException
{
    public function __construct(string $message)
    {
        parent::__construct($message, 0, 'validation_error');
    }
}

/** Thrown when the API returns 200 with an { "error": ... } body. */
class ApiException extends PayingException
{
    public function __construct(string $message, ?string $requestId = null)
    {
        parent::__construct($message, 200, 'api_error', $requestId);
    }
}

class AskPayClient
{
    private string $token;
    private string $baseUrl;
    private int $timeout;
    private int $maxRetries;

    private const PHONE_RE = '/^[0-9]{10}$/';

    /**
     * @param array{base_url?: string, timeout?: int, max_retries?: int} $options
     */
    public function __construct(?string $token = null, array $options = [])
    {
        $token = $token ?? (getenv('PAYING_API_TOKEN') ?: null);
        if (!$token) {
            throw new ValidationException('Missing API token. Pass it in or set PAYING_API_TOKEN.');
        }
        $this->token = $token;
        $this->baseUrl = rtrim($options['base_url'] ?? 'https://ask.paying.co/api', '/');
        $this->timeout = $options['timeout'] ?? 15;
        $this->maxRetries = $options['max_retries'] ?? 2;
    }

    // -- Orders ------------------------------------------------------------ //

    /**
     * Store (create or update) an order. The body shape is defined by your
     * integrator's configured field mappings.
     *
     * @param array<string, mixed> $order
     * @return array{success: string, merchant_reference: string}
     */
    public function storeOrder(array $order): array
    {
        return $this->request('POST', '/order/store', $order);
    }

    /**
     * Cancel (delete) an order. Requires the "Order - Cancel" ability.
     *
     * @return array{success: string, merchant_reference: string}
     */
    public function cancelOrder(string $merchantReference): array
    {
        $this->requireRef($merchantReference);
        return $this->request('POST', '/order/cancel', ['merchant_reference' => $merchantReference]);
    }

    /**
     * Read the payment applied to an order. Requires "Order - Read".
     * Throws ApiException until a payment exists.
     *
     * @return array<string, mixed>
     */
    public function readOrder(string $merchantReference): array
    {
        $this->requireRef($merchantReference);
        return $this->request('POST', '/order/read', ['merchant_reference' => $merchantReference]);
    }

    /** True if a payment has been applied to the order, false otherwise. */
    public function isPaid(string $merchantReference): bool
    {
        try {
            $this->readOrder($merchantReference);
            return true;
        } catch (ApiException $e) {
            return false;
        }
    }

    // -- Payment URL & delivery -------------------------------------------- //

    /**
     * Generate a hosted-payment URL for an existing order reference.
     * Requires the "Generate QR Code" ability.
     *
     * @return array{url: string}
     */
    public function createPaymentUrl(string $merchantReference): array
    {
        $this->requireRef($merchantReference);
        return $this->request('POST', '/url/create', ['merchant_reference' => $merchantReference]);
    }

    /**
     * Send an SMS payment link via the merchant's Twilio config.
     * Requires the "Send SMS" ability. $phoneNumber must be 10 digits.
     *
     * @return array<string, mixed>
     */
    public function sendSms(string $merchantReference, string $phoneNumber): array
    {
        $this->requireRef($merchantReference);
        if (!preg_match(self::PHONE_RE, $phoneNumber)) {
            throw new ValidationException('phone_number must be 10 digits');
        }
        return $this->request('POST', '/smspay/send', [
            'merchant_reference' => $merchantReference,
            'phone_number' => $phoneNumber,
        ]);
    }

    // -- Account & polling -------------------------------------------------- //

    /**
     * Return the integrator profile bound to this token. Requires "Store - Read".
     *
     * @return array<string, mixed>
     */
    public function whoami(): array
    {
        return $this->request('GET', '/store');
    }

    /**
     * Return the polling configuration for this integrator.
     *
     * @return mixed
     */
    public function getPolling()
    {
        return $this->request('GET', '/poll');
    }

    // -- Helpers ------------------------------------------------------------ //

    private function requireRef(string $merchantReference): void
    {
        if ($merchantReference === '') {
            throw new ValidationException('merchant_reference is required');
        }
    }

    /**
     * @param array<string, mixed>|null $body
     * @return mixed
     */
    private function request(string $method, string $path, ?array $body = null)
    {
        $url = $this->baseUrl . $path;
        $attempt = 0;

        while (true) {
            $ch = curl_init($url);
            curl_setopt_array($ch, [
                CURLOPT_CUSTOMREQUEST => $method,
                CURLOPT_RETURNTRANSFER => true,
                CURLOPT_HEADER => true,
                CURLOPT_TIMEOUT => $this->timeout,
                CURLOPT_HTTPHEADER => [
                    'Authorization: Bearer ' . $this->token,
                    'Content-Type: application/json',
                    'Accept: application/json',
                ],
            ]);
            if ($body !== null) {
                curl_setopt($ch, CURLOPT_POSTFIELDS, json_encode($body));
            }

            $response = curl_exec($ch);
            if ($response === false) {
                $err = curl_error($ch);
                curl_close($ch);
                if ($attempt < $this->maxRetries) {
                    $attempt++;
                    usleep((int) (250000 * (2 ** ($attempt - 1))));
                    continue;
                }
                throw new PayingException($err, 0, 'network_error');
            }

            $status = (int) curl_getinfo($ch, CURLINFO_RESPONSE_CODE);
            $headerSize = (int) curl_getinfo($ch, CURLINFO_HEADER_SIZE);
            $rawHeaders = substr($response, 0, $headerSize);
            $rawBody = substr($response, $headerSize);
            curl_close($ch);

            $requestId = null;
            if (preg_match('/x-request-id:\s*(\S+)/i', $rawHeaders, $m)) {
                $requestId = $m[1];
            }

            if (($status === 429 || $status >= 500) && $attempt < $this->maxRetries) {
                $attempt++;
                usleep((int) (250000 * (2 ** ($attempt - 1))));
                continue;
            }

            $decoded = json_decode($rawBody, true);

            if ($status < 200 || $status >= 300) {
                $detail = is_array($decoded) ? $decoded : [];
                $message = $detail['error'] ?? $detail['message'] ?? "Request failed with {$status}";
                $code = $status === 403 ? 'forbidden' : ($detail['code'] ?? null);
                throw new PayingException($message, $status, $code, $requestId);
            }

            // Non-JSON 2xx body (e.g. /poll/order returns "success").
            if ($decoded === null && $rawBody !== 'null') {
                return $rawBody;
            }

            // The API returns 200 with an { "error": ... } body for many cases.
            if (is_array($decoded) && isset($decoded['error'])) {
                throw new ApiException((string) $decoded['error'], $requestId);
            }

            return $decoded;
        }
    }
}

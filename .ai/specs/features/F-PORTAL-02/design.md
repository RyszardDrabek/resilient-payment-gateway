# F-PORTAL-02: Technical Design — Merchant Webhook Self-Service & Event Simulator

## Overview

`F-PORTAL-02` delivers the merchant-facing webhook self-service management and diagnostic console within the Resilient Payment Gateway portal. It empowers merchants to configure and update their destination endpoint URLs, view and securely rotate HMAC-SHA256 signing secret keys, and interactively test their webhook receivers with simulated payment lifecycle events.

---

## Architectural Mapping

- **ADR-004:** JWT Bearer authentication; enforces `role: merchant` (and permits `ops` for debugging/impersonation). Resolves `partyId` from claims (`sub` or `party_id`).
- **ADR-007:** Adheres to EDGE HTTP problem details (`application/problem+json`).
- **ADR-013:** Integrated into the React 19 + TypeScript SPA in `src/PaymentGateway.Portal` consuming backend REST endpoints.
- **ADR-014:** Follows design system specifications: dark-mode financial theme, split-panel layout, tokenized colors, monospace code blocks, status badges, and accessible modal dialogs.
- **F-EDGE-05:** Extends the existing webhook subsystem (`MerchantWebhookSubscription`, `IMerchantWebhookSigner`, `HmacSha256WebhookSigner`).

---

## Backend Design (`PaymentGateway.Api` & `PaymentGateway.Application`)

### 1. Public API Surface

Endpoints registered under `/api/v1/webhooks/merchants` (with `/webhooks/merchants` alias):

1. **`GET /api/v1/webhooks/merchants/subscription`**
   - **Authorization:** `MerchantOrOpsPolicy` (`role: merchant` or `role: ops`).
   - **Party Resolution:** Extracted from authenticated user `sub` (e.g. `merch_acme`), or explicit `?partyId=` query param for `ops`.
   - **Responses:**
     - `200 OK`: `MerchantWebhookSubscriptionDto`
     - `404 Not Found`: When no subscription has been registered yet for the merchant.

2. **`PUT /api/v1/webhooks/merchants/subscription`**
   - **Authorization:** `MerchantOrOpsPolicy`.
   - **Request Body:**
     ```json
     {
       "endpointUrl": "https://api.merchant.com/webhook",
       "isActive": true
     }
     ```
   - **Validation:** `endpointUrl` must be a valid, absolute HTTPS or HTTP URL.
   - **Behavior:** Upserts the subscription for the party. If none exists, auto-generates a secure 32-byte (64 hex char) `secretKey`.
   - **Response:** `200 OK` with `MerchantWebhookSubscriptionDto`.

3. **`POST /api/v1/webhooks/merchants/subscription/rotate-secret`**
   - **Authorization:** `MerchantOrOpsPolicy`.
   - **Behavior:** Validates that an active or existing webhook subscription exists for the merchant. If no subscription exists, returns `400 Bad Request` with problem details instructing the merchant to configure an endpoint URL first. Generates a new cryptographically secure random key (`whsec_` + 32-byte hex), updates the subscription record, and returns the updated subscription.
   - **Response:** `200 OK` with `MerchantWebhookSubscriptionDto` (or `400 Bad Request` if unconfigured).

4. **`POST /api/v1/webhooks/merchants/test-dispatch`**
   - **Authorization:** `MerchantOrOpsPolicy`.
   - **SSRF Protection:** Rejects requests targeting cloud metadata / link-local addresses (e.g. `169.254.169.254`, `metadata.google.internal`, `instance-data`) with a clean diagnostic response. Allows localhost, Docker host, and external domains.
   - **Request Body:**
     ```json
     {
       "endpointUrl": "https://api.merchant.com/webhook",
       "eventType": "payment.settled"
     }
     ```
     *(Both fields optional: `endpointUrl` falls back to the saved subscription endpoint; `eventType` defaults to `payment.settled`).*
   - **Behavior:**
     - Generates simulated payload with sample payment metadata.
     - Signs payload using `HmacSha256WebhookSigner` and the merchant's `secretKey`.
     - Executes HTTP POST with headers:
       - `X-Signature-SHA256: <computed_signature>`
       - `X-Event-Id: <generated_id>`
       - `X-Timestamp: <unix_seconds>`
       - `Content-Type: application/json`
     - Measures elapsed latency in milliseconds.
     - Captures HTTP status code, response body (truncated if long), or socket/timeout error details.
   - **Response:** `200 OK` with `MerchantWebhookTestDispatchResponseDto`:
     ```json
     {
       "success": true,
       "statusCode": 200,
       "latencyMs": 85,
       "endpointUrl": "https://api.merchant.com/webhook",
       "eventId": "evt_test_01928374",
       "timestamp": "1773400000",
       "signature": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
       "requestHeaders": {
         "X-Signature-SHA256": "...",
         "X-Event-Id": "...",
         "X-Timestamp": "...",
         "Content-Type": "application/json"
       },
       "requestPayload": "{...}",
       "responseBody": "{\"received\": true}",
       "errorMessage": null
     }
     ```

### 2. DTOs & Models

- `MerchantWebhookSubscriptionDto(Guid Id, string PartyId, string EndpointUrl, string SecretKey, bool IsActive)`
- `UpdateWebhookSubscriptionRequest(string EndpointUrl, bool IsActive)`
- `TestWebhookDispatchRequest(string? EndpointUrl, string? EventType)`
- `TestWebhookDispatchResultDto(bool Success, int? StatusCode, long LatencyMs, string EndpointUrl, string EventId, string Timestamp, string Signature, Dictionary<string, string> RequestHeaders, string RequestPayload, string? ResponseBody, string? ErrorMessage)`

### 3. Repository Enhancements

- Extend `IMerchantWebhookSubscriptionRepository`:
  - `Task<MerchantWebhookSubscription?> GetByPartyIdAsync(string partyId, CancellationToken ct = default);`
  - `Task UpsertAsync(MerchantWebhookSubscription subscription, CancellationToken ct = default);`
- Update `MerchantWebhookSubscription` entity with mutation methods:
  - `UpdateEndpoint(string endpointUrl, bool isActive)`
  - `RotateSecretKey(string newSecretKey)`

---

## Frontend Design (`src/PaymentGateway.Portal`)

### 1. Component Hierarchy

```
src/PaymentGateway.Portal/src/components/webhooks/
├── WebhooksConsole.tsx              # Main console container: coordinates state, load, tabs
├── WebhookConfigCard.tsx            # Left Panel Top: URL input, Active toggle, Save button
├── SecretKeyCard.tsx                # Left Panel Bottom: Masked key, copy to clipboard, rotate button & modal
├── EventSimulatorPanel.tsx          # Right Panel: Event selector, dispatch button, inspector
└── SimulatorResultCard.tsx          # Diagnostics: status pill, latency badge, signature, payload, headers
```

### 2. User Interactions & States

- **Loading State:** Skeleton loaders for subscription form and simulator while fetching `GET /api/v1/webhooks/merchants/subscription`.
- **Masked Secret Key:** Displays `••••••••••••••••` by default with a "Show / Hide" toggle icon and a "Copy" button with toast / copied feedback.
- **Rotate Key Confirmation:** Accessible modal with clear warning that changing keys immediately invalidates prior signatures. Requires confirmation button before invoking rotation.
- **Simulator Dispatch:**
  - Loading spinner / disabled state on "Dispatch Test Event" button during transmission.
  - Success banner with green `200 OK` badge, round-trip latency (e.g. `112 ms`), and request payload inspector.
  - Failure banner with red status badge (e.g. `500 Internal Server Error`, `Connection Refused`, or `Timeout`), diagnostic error explanation, and computed `X-Signature-SHA256` for merchant debugging.

---

## Verification & Testing Strategy

1. **Unit Tests:**
   - MediatR handler tests for get, update, rotate secret, and test dispatch.
   - `MerchantWebhookSubscription` entity method validation.
2. **Integration Tests (`PaymentGateway.IntegrationTests`):**
   - Endpoints tests in `MerchantWebhookEndpointsTests.cs`:
     - Authorized access for `merchant` and `ops`.
     - 401 Unauthorized for missing token, 403 Forbidden for non-permitted roles.
     - Subscription CRUD lifecycle and secret rotation.
     - Test dispatch with WireMock / local TestServer handler.
3. **Frontend Vitest Tests:**
   - `WebhooksConsole.test.tsx` verifying render, copy secret, rotation modal, and test dispatch simulation.

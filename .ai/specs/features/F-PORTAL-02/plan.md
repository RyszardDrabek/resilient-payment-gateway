# F-PORTAL-02: Implementation Plan — Merchant Webhook Self-Service & Event Simulator

## Overview

Step-by-step TDD plan to implement the Merchant Webhook Management & Diagnostics Console across `PaymentGateway.Api` (endpoints, queries/commands, test dispatcher) and `PaymentGateway.Portal` (React 19 + TypeScript components).

---

## Task Steps

### Step 1: Domain & Infrastructure Webhook Persistence Enhancements
1. Update `MerchantWebhookSubscription` entity:
   - Add `UpdateEndpoint(string endpointUrl, bool isActive)` method.
   - Add `RotateSecretKey(string newSecretKey)` method.
2. Update `IMerchantWebhookSubscriptionRepository` interface and implementation:
   - Add `GetByPartyIdAsync(string partyId, CancellationToken ct = default)` to retrieve any subscription regardless of active flag.
   - Add `UpsertAsync(MerchantWebhookSubscription subscription, CancellationToken ct = default)` to insert or update.
3. Unit test entity mutation logic and repository query/upsert methods.

### Step 2: Application Layer (MediatR Commands, Queries & Test Dispatcher)
1. Define DTOs in `PaymentGateway.Application.Webhooks`:
   - `MerchantWebhookSubscriptionDto`
   - `TestWebhookDispatchResultDto`
2. Implement MediatR handlers:
   - `GetMerchantWebhookSubscriptionQuery` + Handler.
   - `UpdateMerchantWebhookSubscriptionCommand` + Handler (generates initial secret key if creating new).
   - `RotateMerchantWebhookSecretCommand` + Handler (generates cryptographically secure key `whsec_...`).
   - `TestMerchantWebhookDispatchCommand` + Handler:
     - Dispatches simulated payload via `HttpClient` using `HmacSha256WebhookSigner`.
     - Measures latency and captures status code, headers, and error diagnostics.
3. Add Unit Tests for all MediatR handlers.

### Step 3: API Layer (`PaymentGateway.Api`)
1. Create `MerchantWebhookEndpoints.cs`:
   - `GET /api/v1/webhooks/merchants/subscription`
   - `PUT /api/v1/webhooks/merchants/subscription`
   - `POST /api/v1/webhooks/merchants/subscription/rotate-secret`
   - `POST /api/v1/webhooks/merchants/test-dispatch`
2. Add authorization policy in `Program.cs` (`MerchantOrOpsPolicy`) allowing `merchant` and `ops` roles, with party resolution from `sub` claim or query parameter.
3. Map endpoints in `Program.cs`.
4. Integration tests in `MerchantWebhookEndpointsIntegrationTests.cs`:
   - Verify authentication / role enforcement (401 / 403).
   - Verify subscription lifecycle (Get, Update, Rotate Secret).
   - Verify test dispatch with WireMock / local test server.

### Step 4: Frontend API Contracts & Client (`src/PaymentGateway.Portal`)
1. Update `src/types/api.ts` with:
   - `MerchantWebhookSubscription`
   - `UpdateWebhookSubscriptionPayload`
   - `TestWebhookDispatchPayload`
   - `TestWebhookDispatchResult`
2. Update `src/services/apiClient.ts`:
   - `fetchWebhookSubscription()`
   - `updateWebhookSubscription(data)`
   - `rotateWebhookSecret()`
   - `dispatchTestWebhook(data)`
3. Add Vitest unit tests in `src/services/apiClient.test.ts`.

### Step 5: Frontend UI Components (`src/PaymentGateway.Portal`)
1. Create `src/components/webhooks/WebhookConfigCard.tsx`:
   - Endpoint URL text input with validation.
   - Active/Inactive toggle switch with status badge.
   - Save / Update button with feedback state.
2. Create `src/components/webhooks/SecretKeyCard.tsx`:
   - Monospace secret key display with show/hide toggle.
   - One-click copy-to-clipboard button with toast feedback.
   - Rotate Secret Key button with confirmation modal (warning about invalidating prior signatures).
3. Create `src/components/webhooks/EventSimulatorPanel.tsx` & `SimulatorResultCard.tsx`:
   - Event type selector (`payment.authorized`, `payment.settled`, `payment.failed`).
   - "Dispatch Test Event" action button with loading spinner.
   - Diagnostic result container:
     - HTTP status pill (`200 OK`, `400 Bad Request`, `500 Server Error`, or `Connection Error`).
     - Latency badge (`XX ms`).
     - Computed `X-Signature-SHA256` HMAC signature.
     - Formatted request payload & headers viewer.
4. Create `src/components/webhooks/WebhooksConsole.tsx`:
   - Main split-panel container binding left configuration cards and right simulator panel.
   - Role guard: displays prompt/switch button if logged-in user is not `merchant` or `ops`.
5. Connect `WebhooksConsole` to `App.tsx` replacing the current placeholder.

### Step 6: Verification, Reports & Commit
1. Verify `npm test` and `npm run build` in `src/PaymentGateway.Portal`.
2. Verify `dotnet format --verify-no-changes`.
3. Verify `dotnet test`.
4. Generate `coder-report.md`, `acceptance-matrix.md`, and `build-gate-report.md`.
5. Update `spec.md` status to `done`.
6. Commit on `feature/F-PORTAL-02`.

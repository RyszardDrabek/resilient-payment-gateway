# Simplifier Report — F-EDGE-07: Webhook DLQ inspection & manual replay API

## Simplification & Clean Code Review

### 1. Domain Clarity & Cohesion
- `MerchantWebhookDeadLetter` follows clean entity conventions with private parameterless EF constructor, explicit factory method validating invariants, and encapsulated state transitions (`MarkReplayed()`).

### 2. CQRS Separation & Handler Scope
- Handlers in `PaymentGateway.Application.Webhooks` adhere strictly to Single Responsibility.
- Query handler uses `AsNoTracking()` in repository for maximum read efficiency.
- Replay command re-uses domain port `IMerchantWebhookSigner` to ensure identical HMAC hashing mechanics as primary dispatcher without duplicating signing logic.

### 3. Redundancy & Dependency Hygiene
- Endpoint routing utilizes minimal APIs in `PaymentGateway.Api` matching existing conventions in `MerchantApiKeyEndpoints.cs`.
- Authorization checks reuse the same claims resolution pattern (`sub`, `merchant_id`, `merchantId`, and `ops` role bypass).
- Typed `HttpClient` registered directly via `services.AddHttpClient<ReplayMerchantWebhookCommandHandler>()` ensuring proper connection pooling and lifecycle management through `IHttpClientFactory`.

### 4. Spec Conformance
- DTO exposes properties matching both naming styles (`attemptCount`/`deliveryAttempts`, `lastError`/`lastErrorMessage`, `lastAttemptedAt`/`failedAt`) preventing client integration friction.

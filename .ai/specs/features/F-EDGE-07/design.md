# F-EDGE-07: Technical Design — Webhook DLQ Inspection & Manual Replay API

## Overview

`F-EDGE-07` provides operational observability and recovery mechanisms for failed outbound merchant webhook deliveries. When delivery attempts are exhausted after exponential retry, failed webhook notifications are stored in a dedicated Dead-Letter Queue (DLQ) database table. REST query and re-dispatch endpoints enable merchants and operators to diagnose failures and trigger manual redelivery with fresh HMAC signatures.

---

## Architectural Mapping

- **ADR-001:** Modular monolith, Clean Architecture, CQRS with MediatR.
- **ADR-004:** Secured via dual authentication (JWT Bearer or API Key). Enforces role/merchant identity isolation.
- **ADR-007:** RFC 7807 problem details for errors (403 Forbidden, 404 Not Found, 400 Bad Request).
- **ADR-012:** Operational hardening — extends merchant outbound webhook notification architecture with persistent DLQ storage and manual redelivery.

---

## Domain Model (`PaymentGateway.Domain.Entities`)

### `MerchantWebhookDeadLetter`
- `Id: Guid`
- `EventId: string` (Payment lifecycle event ID)
- `PaymentId: string`
- `MerchantId: string` (Party identifier, e.g. `merch_acme`)
- `DestinationUrl: string`
- `PayloadJson: string`
- `DeliveryAttempts: int`
- `LastErrorMessage: string`
- `LastStatusCode: int?`
- `Status: string` (`exhausted` | `replayed`)
- `FailedAt: DateTimeOffset`
- `ReplayedAt: DateTimeOffset?`

### Domain Methods
- `MarkReplayed()`: Sets `Status = "replayed"` and `ReplayedAt = DateTimeOffset.UtcNow`.
- Factory `Create(...)`: Validates required fields, sets `Status = "exhausted"`.

---

## Application Layer (`PaymentGateway.Application`)

### CQRS Commands & Queries
1. **`ListMerchantWebhookDlqQuery`**
   - Inputs: `MerchantId: string`, `Status: string?`, `Page: int = 1`, `PageSize: int = 50`
   - Returns: `IReadOnlyList<MerchantWebhookDeadLetterDto>`

2. **`ReplayMerchantWebhookCommand`**
   - Inputs: `MerchantId: string`, `DlqEntryId: Guid`
   - Returns: `ReplayMerchantWebhookResult(bool Success, string Message, int? StatusCode, DateTimeOffset? ReplayedAt)`
   - Handler: Loads DLQ record, fetches active subscription/secret for merchant, computes fresh HMAC-SHA256 signature and timestamp, dispatches HTTP POST to destination, and updates DLQ status to `replayed`.

3. **`RecordMerchantWebhookDeadLetterCommand`**
   - Inputs: `EventId`, `PaymentId`, `MerchantId`, `DestinationUrl`, `PayloadJson`, `DeliveryAttempts`, `LastErrorMessage`, `LastStatusCode?`
   - Returns: `Guid` (new DLQ entry ID)

---

## Infrastructure Layer (`PaymentGateway.Infrastructure`)

### Persistence & MassTransit
- Table: `pay.MerchantWebhookDeadLetters` via `PaymentDbContext`.
- Repository: `IMerchantWebhookDeadLetterRepository`:
  - `Task AddAsync(MerchantWebhookDeadLetter deadLetter, CancellationToken ct)`
  - `Task<MerchantWebhookDeadLetter?> GetByIdAsync(Guid id, CancellationToken ct)`
  - `Task<IReadOnlyList<MerchantWebhookDeadLetter>> ListByMerchantAsync(string merchantId, string? status, int page, int pageSize, CancellationToken ct)`
  - `Task UpdateAsync(MerchantWebhookDeadLetter deadLetter, CancellationToken ct)`
- MassTransit Consumer:
  - `MerchantWebhookDeadLetterConsumer`: Consumes `MerchantWebhookDeadLetterMessage` and persists `MerchantWebhookDeadLetter` to DB.

---

## Edge & API Layer (`PaymentGateway.Api`)

### REST Endpoints (`MerchantWebhookDlqEndpoints`)
- Group: `/api/v1/merchants/{merchantId}/webhooks/dlq`
- Authorization: Requires `role: ops` OR matching `merchant` principal.
- Routes:
  - `GET /` — query DLQ entries with optional `status` filter (`exhausted`, `replayed`).
  - `POST /{dlqEntryId:guid}/replay` — triggers redelivery and marks DLQ entry as `replayed`.

---

## Architectural Notes & Future Enhancements (Non-blocking)

1. **Webhook Signing Key Fallback Warning:** In `ReplayMerchantWebhookCommandHandler`, if no active subscription or secret key exists for the target merchant, an explicit warning log is recorded (`logger.LogWarning`) before falling back to the default signing key, ensuring operational visibility during manual replays.
2. **Replay Attempt Counter & Diagnostics:** In the current phase, failed re-dispatches leave the record in `exhausted` state and return the failure reason to the caller. A future enhancement can add `ReplayAttemptCount`, `LastReplayedAt`, and `LastReplayError` directly to the `pay.MerchantWebhookDeadLetters` schema to track repeated replay attempts over time.

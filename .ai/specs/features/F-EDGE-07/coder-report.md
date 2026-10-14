# Coder Report — F-EDGE-07: Webhook DLQ inspection & manual replay API

## Summary of Changes
Implemented dead-letter queue persistence, query, and manual redelivery API for outbound merchant webhooks:

1. **Domain Layer (`PaymentGateway.Domain`):**
   - Implemented `MerchantWebhookDeadLetter` entity tracking event ID, payment ID, merchant ID, destination URL, payload JSON, delivery attempt count, failure error message, last HTTP status code, status (`exhausted` / `replayed`), failure timestamp, and replay timestamp.
   - Added domain methods `MarkReplayed()` and validated factory `Create(...)`.
   - Defined `IMerchantWebhookDeadLetterRepository` port interface.
   - Added 6 unit tests in `MerchantWebhookDeadLetterTests.cs`.

2. **Application Layer (`PaymentGateway.Application`):**
   - Implemented `MerchantWebhookDeadLetterDto` (with spec-compliant property aliases: `AttemptCount`, `LastError`, `LastAttemptedAt`), `ReplayMerchantWebhookResult`.
   - Implemented `ListMerchantWebhookDlqQuery` & `ListMerchantWebhookDlqQueryHandler` supporting merchant filtering and status filtering.
   - Implemented `RecordMerchantWebhookDeadLetterCommand` & `RecordMerchantWebhookDeadLetterCommandHandler`.
   - Implemented `ReplayMerchantWebhookCommand` & `ReplayMerchantWebhookCommandHandler` retrieving the dead letter entry, recomputing HMAC-SHA256 signature and fresh timestamp, dispatching HTTP POST redelivery, and marking record status as `replayed`.
   - Added 4 unit tests in `MerchantWebhookDlqHandlersTests.cs`.

3. **Infrastructure Layer (`PaymentGateway.Infrastructure`):**
   - Configured `MerchantWebhookDeadLetters` DbSet and entity mapping in `PaymentDbContext` (`pay.MerchantWebhookDeadLetters` table) with composite index `(MerchantId, Status)` and index on `FailedAt`.
   - Added EF Core migration `20261014083641_AddMerchantWebhookDeadLetters`.
   - Implemented `MerchantWebhookDeadLetterRepository`.
   - Implemented `MerchantWebhookDeadLetterConsumer` consuming MassTransit `MerchantWebhookDeadLetterMessage` to persist dead letter records into database.
   - Registered repository, typed HTTP client, and consumer in `DependencyInjection.cs`.

4. **API Layer (`PaymentGateway.Api`):**
   - Implemented `MerchantWebhookDlqEndpoints` mapped at `/api/v1/merchants/{merchantId}/webhooks/dlq`:
     - `GET /` — paginated queries with optional status filter (`exhausted`, `replayed`).
     - `POST /{dlqEntryId:guid}/replay` — manual redelivery endpoint returning redelivery diagnostics and marking status as replayed.
   - Enforced merchant identity matching or `role: ops` authorization with RFC 7807 problem details (403 Forbidden, 404 Not Found).
   - Registered endpoints in `Program.cs`.
   - Added 5 end-to-end integration tests in `MerchantWebhookDlqIntegrationTests.cs`.

## Verification
- Unit Tests: 368 passed (+10 new tests).
- Integration Tests: 123 passed (+5 new end-to-end tests).
- Architecture Tests: 3 passed (0 violations).
- Portal Vitest: 10 passed.
- Format: `dotnet format --verify-no-changes` clean.

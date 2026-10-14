---
id: F-EDGE-07
topic: EDGE
phase: merchant-api-keys-and-webhook-dlq
status: new
---

# F-EDGE-07: Webhook DLQ inspection & manual replay API

- **Topic:** [EDGE](../../topics/edge.md)
- **Type:** Explicit
- **Value class:** USER-VISIBLE
- **Source:** Phase 7 intake: merchant-api-keys-and-webhook-dlq

## Description

Persists failed webhook delivery attempts in a Dead-Letter Queue (DLQ) table upon retry exhaustion and exposes REST query and re-dispatch endpoints allowing merchants and operators to inspect failure diagnostics and manually trigger redelivery.

## Business context

Merchants whose systems experience outages need operational observability into missed webhooks and the ability to re-trigger notifications once their endpoints recover, without losing transaction event integrity.

## Acceptance criteria

- AC-1 WHEN outbound webhook delivery attempts for an event are exhausted by `MerchantWebhookDispatcherConsumer`, THE system SHALL persist a dead-letter record containing event ID, merchant ID, destination URL, failure status code/error message, payload, attempt count, and last attempted timestamp.
- AC-2 THE system SHALL expose a query endpoint `GET /api/v1/merchants/{merchantId}/webhooks/dlq` (with optional status filtering: `exhausted`, `replayed`) returning paginated dead-letter records with failure diagnostics.
- AC-3 WHEN an authorized caller requests `POST /api/v1/merchants/{merchantId}/webhooks/dlq/{dlqEntryId}/replay`, THE system SHALL immediately re-queue or re-dispatch the webhook with fresh HMAC signing and update the DLQ record status to `replayed`.
- AC-4 IF the requested DLQ record does not exist or belongs to another merchant without operator credentials, THEN THE system SHALL return an RFC 7807 problem document (404 Not Found or 403 Forbidden).

## Interface contract

- **Resource / surface:** `GET /api/v1/merchants/{merchantId}/webhooks/dlq`, `POST /api/v1/merchants/{merchantId}/webhooks/dlq/{dlqEntryId}/replay`
- **Operation:** REST queries & commands
- **Response shape category:** collection | resource | problem
- **Terminal status classes:** 200 OK | 202 Accepted | 400 Bad Request | 401 Unauthorized | 403 Forbidden | 404 Not Found
- **Load-bearing fields:** `id`, `eventId`, `merchantId`, `destinationUrl`, `lastStatusCode`, `lastError`, `attemptCount`, `lastAttemptedAt`, `status`, `replayedAt`

## Out of scope

- Automated recurring scheduled replay of DLQ records (manual or operator-triggered only in this phase).

## Dependencies

F-EDGE-05

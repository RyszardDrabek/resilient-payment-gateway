---
id: F-PORTAL-05
topic: PORTAL
phase: merchant-api-keys-and-webhook-dlq
status: new
---

# F-PORTAL-05: Webhook DLQ delivery logs & manual replay console

- **Topic:** [PORTAL](../../topics/portal.md)
- **Type:** Explicit
- **Value class:** USER-VISIBLE
- **Source:** Phase 7 intake: merchant-api-keys-and-webhook-dlq

## Description

Delivers an interactive console in the web portal for merchants and operators to view failed webhook delivery attempts in the DLQ, inspect diagnostics (HTTP status, error message, event payload), and trigger one-click manual redelivery replay.

## Business context

Enables merchants and support operators to quickly diagnose why webhooks were not received, view delivery failure traces, and replay notifications after fixing recipient endpoints.

## Acceptance criteria

- AC-1 THE portal SHALL provide a "Webhook DLQ / Delivery Failures" view in the Webhooks console displaying a list of dead-letter records with timestamp, event ID, destination URL, last HTTP status code, and attempt count.
- AC-2 WHEN a user selects a DLQ entry, THE portal SHALL display a details modal or drawer with full delivery diagnostics (failure reason, raw error, and JSON payload).
- AC-3 WHEN a user clicks the "Replay" action button on an exhausted DLQ record, THE portal SHALL send a replay request to `F-EDGE-07`, display a success toast, and reflect the updated replay state in the table.
- AC-4 THE console SHALL provide filtering by status (`exhausted`, `replayed`) and search by event ID.

## Interface contract

- **Resource / surface:** `/portal/webhooks` (DLQ Tab / Subview), `WebhookDlqConsole.tsx`
- **Operation:** SPA user interaction consuming `F-EDGE-07` REST endpoints
- **Response shape category:** interactive UI
- **Terminal status classes:** UI rendered | Success feedback | Error toast
- **Load-bearing fields:** `eventId`, `destinationUrl`, `lastStatusCode`, `lastError`, `payload`, `status`, `lastAttemptedAt`

## Out of scope

- Automated cron-based bulk replay from UI.

## Dependencies

F-EDGE-07, F-PORTAL-02

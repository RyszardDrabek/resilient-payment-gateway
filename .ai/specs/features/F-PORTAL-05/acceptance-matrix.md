# Acceptance Matrix — F-PORTAL-05: Webhook DLQ delivery logs & manual replay console

| AC | Acceptance Criteria Summary | Test Suite & Method | Status |
|---|---|---|---|
| AC-1 | Portal provides "Webhook DLQ / Delivery Failures" view in Webhooks console with timestamp, event ID, destination URL, last HTTP status code, and attempt count | `WebhookDlqConsole.tsx`, `apiClient.test.ts: fetches merchant webhook DLQ records and parses collection` | PASS |
| AC-2 | Selecting a DLQ entry displays details modal/drawer with full delivery diagnostics (failure reason, raw error, formatted JSON payload) | `DlqDetailsModal.tsx`, `WebhookDlqConsole.tsx` | PASS |
| AC-3 | Clicking "Replay" on an exhausted record calls `F-EDGE-07`, displays success notification, and reflects updated replay state in table | `apiClient.test.ts: replays a merchant webhook DLQ record via POST /replay`, `WebhookDlqConsole.tsx`, `DlqDetailsModal.tsx` | PASS |
| AC-4 | Console provides filtering by status (`exhausted`, `replayed`) and search by event ID / URL | `WebhookDlqConsole.tsx` (real-time query & status filter toggle) | PASS |

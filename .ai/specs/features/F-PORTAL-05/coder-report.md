# Coder Report — F-PORTAL-05: Webhook DLQ delivery logs & manual replay console

## Summary of Changes
Implemented an interactive self-service management console for inspecting Webhook Dead-Letter Queue (DLQ) entries and triggering manual delivery replays in `PaymentGateway.Portal`:

1. **API Client & TypeScript Types (`src/PaymentGateway.Portal`):**
   - Added interfaces `MerchantWebhookDeadLetterDto` and `ReplayMerchantWebhookResult` in `src/types/api.ts`.
   - Added `fetchMerchantWebhookDlq` and `replayMerchantWebhookDlqEntry` in `src/services/apiClient.ts` with parameter serialization, status filtering, error handling, and correlation ID propagation.
   - Added 2 new unit tests in `src/services/apiClient.test.ts` (15/15 vitest tests passing).

2. **UI Components & Modals (`src/PaymentGateway.Portal/src/components/webhooks/`):**
   - Implemented `DlqDetailsModal.tsx` for inspecting dead-letter entry diagnostics (failure reason, raw error, formatted JSON payload with copy button, and modal-level replay trigger).
   - Implemented `WebhookDlqConsole.tsx` featuring metric summary cards (Total DLQ, Exhausted, Replayed), real-time search by Event ID / URL, status filtering (`all`, `exhausted`, `replayed`), failure logs table with copy chips, inline Replay buttons with loading state, and modal trigger.
   - Enhanced `WebhooksConsole.tsx` with a sub-navigation tab switcher between "Configuration & Simulator" and "Dead-Letter Queue (DLQ)".

3. **Application Shell & Navigation:**
   - Added `'webhook-dlq'` navigation tab to `AppShell.tsx` navigation rail.
   - Integrated `WebhookDlqConsole` view into `App.tsx`.

## Verification
- Vitest: 15 passed (+2 new tests).
- Vite Build: `tsc -b && vite build` passed (0 errors).
- Unit Tests: 368 passed.
- Architecture Tests: 3 passed.
- Integration Tests: Webhook and ApiKey suites passed.
- Format: `dotnet format --verify-no-changes` clean.

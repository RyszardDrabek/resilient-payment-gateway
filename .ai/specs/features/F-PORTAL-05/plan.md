# F-PORTAL-05: TDD Implementation Plan

## Tasks & Phases

### Phase 1: API Client & Types (TDD)
- [x] Task 1.1: Add `MerchantWebhookDeadLetterDto` and `ReplayMerchantWebhookResult` interfaces to `types/api.ts`.
- [x] Task 1.2: Add unit tests for DLQ API client methods (`fetchMerchantWebhookDlq`, `replayMerchantWebhookDlqEntry`) in `apiClient.test.ts`.
- [x] Task 1.3: Implement DLQ client methods in `services/apiClient.ts`. Verify vitest tests pass.

### Phase 2: UI Components & Modals
- [x] Task 2.1: Implement `DlqDetailsModal.tsx` displaying failure diagnostics, error reason, and formatted payload with copy.
- [x] Task 2.2: Implement `WebhookDlqConsole.tsx` with summary cards, search by event ID/URL, status filtering (`all`, `exhausted`, `replayed`), table rows, and replay action.

### Phase 3: Webhooks Console & Shell Integration
- [x] Task 3.1: Integrate `WebhookDlqConsole` into `WebhooksConsole.tsx` with tab switching ("Configuration & Simulator" vs "Delivery Failures / DLQ").

### Phase 4: Verification & Reports
- [x] Task 4.1: Run portal vitest tests, `npm run build`, dotnet tests, and formatting checks.
- [x] Task 4.2: Produce evidence artifacts (`coder-report.md`, `simplifier-report.md`, `review-report.md`, `acceptance-matrix.md`, `build-gate-report.md`).
- [x] Task 4.3: Update `F-PORTAL-05/spec.md` (`status: done`) and `.ai/README.md`.

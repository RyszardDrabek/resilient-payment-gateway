# F-PORTAL-04: TDD Implementation Plan

## Tasks & Phases

### Phase 1: API Client & Types (TDD)
- [x] Task 1.1: Add `MerchantApiKeyDto`, `CreateApiKeyRequest`, and `MerchantApiKeyCreatedDto` to `types/api.ts`.
- [x] Task 1.2: Add unit tests for API key client methods (`fetchMerchantApiKeys`, `createMerchantApiKey`, `revokeMerchantApiKey`) in `apiClient.test.ts`.
- [x] Task 1.3: Implement API key client methods in `services/apiClient.ts`. Verify unit tests pass.

### Phase 2: UI Components & Modals
- [x] Task 2.1: Implement `GenerateApiKeyModal.tsx` with name validation and scope checkboxes (`payments:write`, `payments:read`).
- [x] Task 2.2: Implement `KeyCreatedModal.tsx` with raw key reveal, copy button, and warning banner.
- [x] Task 2.3: Implement `RevokeKeyModal.tsx` with confirmation prompt.
- [x] Task 2.4: Implement `ApiKeysConsole.tsx` with active/revoked filter, summary statistics, search/table, and modal triggers.

### Phase 3: Shell & Navigation Integration
- [x] Task 3.1: Add `'api-keys'` tab to `AppShell.tsx` navigation rail and `App.tsx` router/tab switcher.

### Phase 4: Verification & Reports
- [x] Task 4.1: Run portal vitest tests, dotnet tests, and formatting checks.
- [x] Task 4.2: Produce evidence artifacts (`coder-report.md`, `simplifier-report.md`, `review-report.md`, `acceptance-matrix.md`, `build-gate-report.md`).
- [x] Task 4.3: Update `F-PORTAL-04/spec.md` (`status: done`) and `.ai/README.md`.

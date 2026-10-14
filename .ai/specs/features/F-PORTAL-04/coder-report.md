# Coder Report — F-PORTAL-04: Merchant API Keys management console

## Summary of Changes
Implemented an interactive self-service management console for merchant API keys in `PaymentGateway.Portal`:

1. **API Client & TypeScript Types (`src/PaymentGateway.Portal`):**
   - Added interfaces `MerchantApiKeyDto`, `CreateApiKeyRequest`, and `MerchantApiKeyCreatedDto` to `types/api.ts`.
   - Added `fetchMerchantApiKeys`, `createMerchantApiKey`, and `revokeMerchantApiKey` to `services/apiClient.ts` with error handling and correlation ID propagation.
   - Added 3 unit tests in `services/apiClient.test.ts` (13/13 vitest tests passing).

2. **UI Components & Modals (`src/PaymentGateway.Portal/src/components/apikeys/`):**
   - Implemented `GenerateApiKeyModal.tsx` allowing merchants to specify key name, environment (`rpg_live_...` vs `rpg_test_...`), and granular scopes (`payments:write`, `payments:read`).
   - Implemented `KeyCreatedModal.tsx` revealing the raw secret key once with a one-click copy button, visual confirmation, and prominent security warning.
   - Implemented `RevokeKeyModal.tsx` requiring confirmation before invalidating an active API key with clear operational warnings.
   - Implemented `ApiKeysConsole.tsx` featuring metric summary cards (Total, Active, Revoked), real-time search, status filtering, table with prefix copy chips, and status badges.

3. **Application Shell & Navigation:**
   - Added `'api-keys'` navigation tab to `AppShell.tsx` navigation rail.
   - Integrated `ApiKeysConsole` view into `App.tsx`.

## Verification
- Vitest: 13 passed (+3 new tests).
- Vite Build: `tsc -b && vite build` passed (0 errors).
- Unit Tests: 368 passed.
- Architecture Tests: 3 passed.
- Integration Tests: 5 passed (ApiKey integration suite).
- Format: `dotnet format --verify-no-changes` clean.

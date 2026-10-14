# Acceptance Matrix — F-PORTAL-04: Merchant API Keys management console

| AC | Acceptance Criteria Summary | Test Suite & Method | Status |
|---|---|---|---|
| AC-1 | Portal provides "Merchant API Keys" console displaying keys with name, prefix, scopes, creation date, and status | `apiClient.test.ts: fetches merchant API keys and parses collection`, `ApiKeysConsole.tsx` | PASS |
| AC-2 | "Generate New Key" button displays modal with key name and scope checkboxes (`payments:write`, `payments:read`) | `GenerateApiKeyModal.tsx` | PASS |
| AC-3 | Successful key generation displays raw key in modal with one-click copy and prominent warning | `apiClient.test.ts: creates a new merchant API key with scopes and receives raw key`, `KeyCreatedModal.tsx` | PASS |
| AC-4 | Revocation requires confirmation and updates key status to revoked | `apiClient.test.ts: revokes an API key via POST /revoke`, `RevokeKeyModal.tsx` | PASS |

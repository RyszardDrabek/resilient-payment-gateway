# Acceptance Matrix — F-EDGE-06: Merchant API Key authentication & scoped permissions

| AC | Acceptance Criteria Summary | Test Suite & Method | Status |
|---|---|---|---|
| AC-1 | Authenticate HTTP requests bearing `X-Api-Key` header against active merchant API keys in DB | `MerchantApiKeyIntegrationTests.AuthenticateWithApiKey_WhenValid_AllowsPaymentOperation` | PASS |
| AC-2 | Generate secure key with prefix (`rpg_live_`/`rpg_test_`), return raw key once, persist salted SHA-256 hash | `MerchantApiKeyTests.Create_WithValidParameters_ReturnsEntityAndRawKey`, `MerchantApiKeyIntegrationTests.CreateApiKey_AsAuthorizedMerchant_ReturnsCreatedWithRawKey` | PASS |
| AC-3 | Reject revoked keys or keys lacking required scope with 401 Unauthorized or 403 Forbidden problem document | `MerchantApiKeyIntegrationTests.AuthenticateWithApiKey_WhenMissingRequiredScope_ReturnsForbidden`, `MerchantApiKeyIntegrationTests.AuthenticateWithApiKey_WhenRevoked_ReturnsUnauthorized` | PASS |
| AC-4 | Management endpoints (`GET`, `POST`, `POST /{keyId}/revoke`) restricted to operators or matching merchant principals | `MerchantApiKeyIntegrationTests.AccessApiKeysOfDifferentMerchant_WithoutOpsRole_ReturnsForbidden`, `MerchantApiKeyIntegrationTests.CreateApiKey_AsAuthorizedMerchant_ReturnsCreatedWithRawKey` | PASS |

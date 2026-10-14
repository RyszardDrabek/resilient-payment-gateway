# Coder Report — F-EDGE-06: Merchant API Key authentication & scoped permissions

## Summary of Changes
Implemented end-to-end merchant API key generation, authentication, permission scoping, and lifecycle management:

1. **Domain (`PaymentGateway.Domain`):**
   - Implemented `MerchantApiKey` aggregate entity with prefix discovery (`rpg_live_` / `rpg_test_`), raw key creation with 32 bytes high entropy, random salt generation, and salted SHA-256 hash persistence.
   - Added `VerifyKey(rawKey)` with constant-time equality comparison (`CryptographicOperations.FixedTimeEquals`).
   - Added `Revoke()` and `HasScope(scope)` domain methods.
   - Defined `IMerchantApiKeyRepository` port interface.

2. **Application (`PaymentGateway.Application`):**
   - Added `CreateMerchantApiKeyCommand`, `ListMerchantApiKeysQuery`, `RevokeMerchantApiKeyCommand`, and `ValidateMerchantApiKeyQuery` with MediatR handlers.
   - Handled scoped permissions (`payments:write`, `payments:read`).

3. **Infrastructure (`PaymentGateway.Infrastructure`):**
   - Configured `MerchantApiKeys` entity in `PaymentDbContext` (`pay.MerchantApiKeys` table).
   - Created and applied EF Core migration `AddMerchantApiKeys`.
   - Implemented `MerchantApiKeyRepository`.
   - Registered `IMerchantApiKeyRepository` in `DependencyInjection.cs`.

4. **API & Edge (`PaymentGateway.Api`):**
   - Created `ApiKeyAuthenticationHandler` supporting `X-Api-Key` headers, emitting claims (`sub`, `role: merchant`, `scope`, `auth_method: api_key`).
   - Configured dual authentication in `Program.cs` via ASP.NET Core `PolicyScheme` ("SmartScheme").
   - Implemented `MerchantApiKeyEndpoints` (`GET /api/v1/merchants/{merchantId}/api-keys`, `POST /api/v1/merchants/{merchantId}/api-keys`, `POST /api/v1/merchants/{merchantId}/api-keys/{keyId}/revoke`) with operator and merchant identity authorization.
   - Enforced fine-grained scope authorization (`payments:write` for payment commands, `payments:read` for payment queries) with RFC 7807 403 Forbidden problem documents.

## Verification
- Unit Tests: 358 passed (+14 new tests in `MerchantApiKeyTests` and `MerchantApiKeyHandlersTests`).
- Integration Tests: 118 passed (+5 new end-to-end integration tests in `MerchantApiKeyIntegrationTests`).
- Architecture Tests: 3 passed (0 violations).
- Vitest: 10 passed.
- Format: `dotnet format --verify-no-changes` clean.

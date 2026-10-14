# F-EDGE-06: Technical Design — Merchant API Key Authentication & Scoped Permissions

## Overview

`F-EDGE-06` delivers secure server-to-server API key authentication for merchants interacting with the Resilient Payment Gateway. It introduces high-entropy API keys with identifiable prefixes (`rpg_live_` or `rpg_test_`), one-time secret revelation, salted SHA-256 hash persistence, fine-grained scopes (`payments:write`, `payments:read`), an ASP.NET Core custom authentication scheme handling `X-Api-Key` headers, and management REST endpoints.

---

## Architectural Mapping

- **ADR-001:** Clean Architecture & CQRS. Entity in `Domain`, commands/queries in `Application`, EF mapping in `Infrastructure`, endpoints and authentication handler in `Api`/`Edge`.
- **ADR-004:** Complements JWT Bearer authentication. When `X-Api-Key` is present, `ApiKeyAuthenticationHandler` validates the key and constructs a `ClaimsPrincipal` with `role: merchant`, `merchant_id: {id}`, and `scope: {scope}` claims.
- **ADR-007:** Adheres to RFC 7807 problem details (`PaymentProblemTypes.Unauthorized` and `PaymentProblemTypes.Forbidden`).
- **Clean DB Schema:** Persisted in `pay.MerchantApiKeys` table via `PaymentDbContext`.

---

## Domain Model (`PaymentGateway.Domain.Entities`)

### `MerchantApiKey`
- `Id: Guid`
- `MerchantId: string` (party identifier, e.g. `merch_acme`)
- `Name: string` (human label, e.g. "Production Backend")
- `Prefix: string` (e.g. `rpg_live_` + first 6 characters, e.g. `rpg_live_a1b2c3`)
- `KeyHash: string` (Base64-encoded salted SHA-256 hash)
- `Salt: string` (Base64-encoded cryptographically secure random salt)
- `Scopes: List<string>` (`payments:write`, `payments:read`)
- `CreatedAt: DateTimeOffset`
- `IsRevoked: bool`
- `RevokedAt: DateTimeOffset?`

### Key Generation & Cryptography
- Raw key format: `{prefix}_{random32BytesHex}` (e.g., `rpg_live_8f3d1a2b3c...`)
- Prefix entropy: Environment prefix (`rpg_live_` or `rpg_test_`) + 10 hex characters (40 bits of entropy ~ 1.1 trillion combinations) ensuring collision-free fast indexed database lookups.
- Salt: 16 bytes random `RandomNumberGenerator.GetBytes(16)`
- Hash: `SHA256.HashData(salt + Encoding.UTF8.GetBytes(rawKey))`
- Verification: Constant-time byte equality comparison via `CryptographicOperations.FixedTimeEquals` to prevent timing attacks.
- Revocation Audit: `RevokedAt` timestamp is recorded upon revocation. (Future audit enhancement: capture `RevokedBy` operator/user identity if full user-level compliance lineage is required).

---

## Application Layer (`PaymentGateway.Application`)

### CQRS Commands & Queries
1. **`CreateMerchantApiKeyCommand`**
   - Inputs: `MerchantId: string`, `Name: string`, `Scopes: IReadOnlyList<string>`, `Environment: string`
   - Returns: `MerchantApiKeyCreatedResult(Guid Id, string Name, string Prefix, IReadOnlyList<string> Scopes, DateTimeOffset CreatedAt, string RawKey)`
   - Validation: `MerchantId` required, `Name` not empty, `Scopes` contains valid scopes (`payments:write`, `payments:read`).

2. **`ListMerchantApiKeysQuery`**
   - Inputs: `MerchantId: string`
   - Returns: `IReadOnlyList<MerchantApiKeySummaryDto(Guid Id, string Name, string Prefix, IReadOnlyList<string> Scopes, DateTimeOffset CreatedAt, bool IsRevoked, DateTimeOffset? RevokedAt)>`

3. **`RevokeMerchantApiKeyCommand`**
   - Inputs: `MerchantId: string`, `KeyId: Guid`
   - Returns: `bool` (or NotFound / Unauthorized)

4. **`ValidateMerchantApiKeyQuery`**
   - Inputs: `RawKey: string`
   - Returns: `ValidatedApiKeyResult(bool IsValid, string? MerchantId, IReadOnlyList<string>? Scopes)`

---

## Infrastructure Layer (`PaymentGateway.Infrastructure`)

### Persistence & Repository
- `IMerchantApiKeyRepository`:
  - `Task AddAsync(MerchantApiKey apiKey, CancellationToken ct)`
  - `Task<IReadOnlyList<MerchantApiKey>> ListByMerchantAsync(string merchantId, CancellationToken ct)`
  - `Task<MerchantApiKey?> GetByIdAsync(Guid id, CancellationToken ct)`
  - `Task<MerchantApiKey?> FindByPrefixAsync(string prefix, CancellationToken ct)`
  - `Task UpdateAsync(MerchantApiKey apiKey, CancellationToken ct)`
- EF Core Entity Configuration in `PaymentDbContext`:
  - Table: `pay.MerchantApiKeys`
  - Index on `Prefix` and `MerchantId`.

---

## Edge & API Layer (`PaymentGateway.Api` & `PaymentGateway.Edge`)

### 1. Authentication Handler: `ApiKeyAuthenticationHandler`
- Scheme Name: `ApiKeyDefaults.AuthenticationScheme` ("ApiKey")
- Inspects header `X-Api-Key`:
  - If header not present → `AuthenticateResult.NoResult()` (allows fallback to JWT Bearer or public endpoints).
  - If present → validates format and queries active key by prefix and hash.
  - If invalid / revoked → `AuthenticateResult.Fail("Invalid or revoked API key")`.
  - If valid → returns `AuthenticateResult.Success(ticket)` with claims:
    - `ClaimTypes.NameIdentifier`: `merchantId`
    - `merchant_id`: `merchantId`
    - `ClaimTypes.Role`: `merchant`
    - `role`: `merchant`
    - `auth_method`: `api_key`
    - `scope`: `payments:write`, `payments:read`
- Authentication events / Challenge / Forbidden: outputs RFC 7807 `PaymentProblemDetails`.

### 2. Composite / Smart Authentication
- Update ASP.NET Core `AddAuthentication`:
  - Register `ApiKey` scheme.
  - Set default scheme or policy scheme with dual support: check `X-Api-Key` first, else use `JwtBearer`.
  - Secure `/payments` endpoints to enforce scope requirement when authenticated via API Key (`payments:write` for commands, `payments:read` for queries).

### 3. Management REST Endpoints (`MerchantApiKeyEndpoints`)
- Group: `/api/v1/merchants/{merchantId}/api-keys`
- Authorization: Requires `role: ops` OR matching `merchant` principal.
- Routes:
  - `GET /` → returns list of active/revoked keys for the merchant.
  - `POST /` → creates new API key and returns DTO with `rawKey` (one-time).
  - `POST /{keyId}/revoke` → revokes the API key immediately.

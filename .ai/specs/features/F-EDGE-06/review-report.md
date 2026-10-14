# Review Report — F-EDGE-06

## Verdict: APPROVED

### Findings & Analysis
- **Security & Cryptography:** 32-byte cryptographically secure random entropy generated via `RandomNumberGenerator`. 16-byte random salt per key. Hashing executed with SHA-256. Constant-time byte verification via `CryptographicOperations.FixedTimeEquals` prevents timing oracle attacks.
- **Principle of Least Privilege:** Fine-grained scopes (`payments:write`, `payments:read`) enforced at endpoint level. Callers using keys without `payments:write` are barred from triggering financial mutations.
- **Tenancy & Authorization Isolation:** `MerchantApiKeyEndpoints` verifies that callers belong to `role: ops` or have matching merchant principal before listing, creating, or revoking keys.
- **Backward Compatibility:** Default authentication seamlessly delegates to JWT Bearer when `X-Api-Key` is not provided. Existing test suites, portal tokens, and webhooks operate unchanged.
- **Prefix Entropy (Addressed):** Prefix length increased to 10 hex characters (40 bits of entropy ~ 1.1 trillion combinations) to avoid any lookup collision in the indexed database query while remaining within `Prefix` column size.
- **Revocation Audit Lineage (Noted):** `RevokedAt` timestamp is actively persisted upon revocation; `RevokedBy` operator/user identity is noted as a prospective non-blocking enhancement when full multi-user audit logging is specified.


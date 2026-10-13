# F-EDGE-06: TDD Implementation Plan

## Tasks & Phases

### Phase 1: Domain & Cryptography (TDD)
- [ ] Task 1.1: Unit tests for `MerchantApiKey` entity creation, prefix extraction, key generation, and hash verification using constant-time comparison in `PaymentGateway.Domain.Tests`.
- [ ] Task 1.2: Implement `MerchantApiKey` in `PaymentGateway.Domain.Entities`.

### Phase 2: Application Layer (TDD)
- [ ] Task 2.1: Unit tests for `CreateMerchantApiKeyCommandHandler`, `ListMerchantApiKeysQueryHandler`, and `RevokeMerchantApiKeyCommandHandler` in `PaymentGateway.Application.Tests`.
- [ ] Task 2.2: Implement CQRS commands, queries, and handlers in `PaymentGateway.Application.Merchants`.

### Phase 3: Infrastructure Persistence
- [ ] Task 3.1: Define `IMerchantApiKeyRepository` and implement EF Core repository in `PaymentGateway.Infrastructure.Repositories`.
- [ ] Task 3.2: Configure entity in `PaymentDbContext` and add migration or schema mapping.

### Phase 4: Authentication Handler & Scope Authorization (TDD)
- [ ] Task 4.1: Unit tests for `ApiKeyAuthenticationHandler` (missing header, invalid key, revoked key, valid key with claims) in `PaymentGateway.Api.Tests` / `PaymentGateway.Edge.Tests`.
- [ ] Task 4.2: Implement `ApiKeyAuthenticationHandler` and register authentication scheme in `PaymentGateway.Api`.
- [ ] Task 4.3: Add scope authorization checks on `/payments` endpoints for API key authenticated callers.

### Phase 5: Management REST Endpoints (TDD)
- [ ] Task 5.1: Integration tests for `GET`, `POST`, `POST /{keyId}/revoke` endpoints in `PaymentGateway.Api.Tests`.
- [ ] Task 5.2: Implement `MerchantApiKeyEndpoints` and map endpoints in `Program.cs`.

### Phase 6: Verification & Test Suite
- [ ] Task 6.1: Run all unit, architecture, and integration tests to ensure 100% green pass.
- [ ] Task 6.2: Produce evidence artifacts (`coder-report.md`, `simplifier-report.md`, `review-report.md`, `acceptance-matrix.md`, `build-gate-report.md`).

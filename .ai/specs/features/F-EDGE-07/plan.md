# F-EDGE-07: TDD Implementation Plan

## Tasks & Phases

### Phase 1: Domain Model (TDD)
- [x] Task 1.1: Unit tests for `MerchantWebhookDeadLetter` entity (creation, status tracking, `MarkReplayed`) in `PaymentGateway.UnitTests/Domain/MerchantWebhookDeadLetterTests.cs`.
- [x] Task 1.2: Implement `MerchantWebhookDeadLetter` in `PaymentGateway.Domain.Entities` and `IMerchantWebhookDeadLetterRepository` port interface.

### Phase 2: Application Layer (TDD)
- [x] Task 2.1: Unit tests for `ListMerchantWebhookDlqQueryHandler`, `ReplayMerchantWebhookCommandHandler`, and `RecordMerchantWebhookDeadLetterCommandHandler` in `PaymentGateway.UnitTests/Application/MerchantWebhookDlqHandlersTests.cs`.
- [x] Task 2.2: Implement CQRS commands, queries, and DTOs in `PaymentGateway.Application.Webhooks`.

### Phase 3: Infrastructure Persistence & Consumer
- [x] Task 3.1: Configure `MerchantWebhookDeadLetter` entity in `PaymentDbContext` and add EF Core migration.
- [x] Task 3.2: Implement `MerchantWebhookDeadLetterRepository` in `PaymentGateway.Infrastructure.Repositories`.
- [x] Task 3.3: Implement `MerchantWebhookDeadLetterConsumer` to persist MassTransit `MerchantWebhookDeadLetterMessage` to database. Register in `DependencyInjection.cs`.

### Phase 4: API Endpoints (TDD)
- [x] Task 4.1: Integration tests for DLQ query and replay endpoints in `PaymentGateway.IntegrationTests/MerchantWebhookDlqIntegrationTests.cs`.
- [x] Task 4.2: Implement `MerchantWebhookDlqEndpoints` in `PaymentGateway.Api.Endpoints` and register in `Program.cs`.

### Phase 5: Verification & Reports
- [x] Task 5.1: Run all test suites (unit, architecture, integration, vitest) and ensure 100% pass.
- [x] Task 5.2: Produce evidence artifacts (`coder-report.md`, `simplifier-report.md`, `review-report.md`, `acceptance-matrix.md`, `build-gate-report.md`).

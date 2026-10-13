# F-PORTAL-02: Coder Report

## Summary of Implementation

Implemented the merchant webhook self-service console and interactive event simulator across the backend .NET API and the React 19 Single Page Application.

### Changes Made

#### 1. Domain & Infrastructure
- `src/PaymentGateway.Domain/Entities/MerchantWebhookSubscription.cs`: Added `UpdateEndpoint(string endpointUrl, bool isActive)` and `RotateSecretKey(string newSecretKey)`.
- `src/PaymentGateway.Domain/Ports/IMerchantWebhookSubscriptionRepository.cs`: Added `GetByPartyIdAsync` and `UpsertAsync`.
- `src/PaymentGateway.Infrastructure/Repositories/MerchantWebhookSubscriptionRepository.cs`: Implemented database query and upsert logic.
- `src/PaymentGateway.Infrastructure/Webhooks/MerchantWebhookSimulator.cs`: Implemented simulator service delivering signed simulated test events via `HttpClient`, computing `X-Signature-SHA256` HMAC signatures, measuring latency in ms, and trapping failure diagnostics.
- `src/PaymentGateway.Infrastructure/DependencyInjection.cs`: Registered `IMerchantWebhookSimulator` with typed `HttpClient`.

#### 2. Application Layer
- `src/PaymentGateway.Application/Webhooks/MerchantWebhookSubscriptionDto.cs`
- `src/PaymentGateway.Application/Webhooks/GetMerchantWebhookSubscriptionQuery.cs`
- `src/PaymentGateway.Application/Webhooks/GetMerchantWebhookSubscriptionQueryHandler.cs`
- `src/PaymentGateway.Application/Webhooks/UpdateMerchantWebhookSubscriptionCommand.cs`
- `src/PaymentGateway.Application/Webhooks/UpdateMerchantWebhookSubscriptionCommandHandler.cs`
- `src/PaymentGateway.Application/Webhooks/RotateMerchantWebhookSecretCommand.cs`
- `src/PaymentGateway.Application/Webhooks/RotateMerchantWebhookSecretCommandHandler.cs`
- `src/PaymentGateway.Application/Webhooks/TestMerchantWebhookDispatchCommand.cs`
- `src/PaymentGateway.Application/Webhooks/TestMerchantWebhookDispatchCommandHandler.cs`
- `src/PaymentGateway.Application/Webhooks/TestMerchantWebhookDispatchResultDto.cs`
- `src/PaymentGateway.Application/Webhooks/IMerchantWebhookSimulator.cs`

#### 3. API Layer
- `src/PaymentGateway.Api/Endpoints/UpdateWebhookSubscriptionRequest.cs`
- `src/PaymentGateway.Api/Endpoints/TestWebhookDispatchRequest.cs`
- `src/PaymentGateway.Api/Endpoints/MerchantWebhookEndpoints.cs`: Exposed `GET /subscription`, `PUT /subscription`, `POST /subscription/rotate-secret`, and `POST /test-dispatch` under `/api/v1/webhooks/merchants`.
- `src/PaymentGateway.Api/Program.cs`: Configured `MerchantWebhookPolicy` and mapped endpoints.

#### 4. Frontend Single Page Application (`src/PaymentGateway.Portal`)
- `src/types/api.ts`: Added TypeScript interfaces for webhook subscriptions, requests, and simulator diagnostics.
- `src/services/apiClient.ts`: Added typed API methods for webhook operations (`fetchWebhookSubscription`, `updateWebhookSubscription`, `rotateWebhookSecret`, `dispatchTestWebhook`).
- `src/components/webhooks/WebhookConfigCard.tsx`: Form for endpoint URL and active/inactive toggle with status badges.
- `src/components/webhooks/SecretKeyCard.tsx`: Masked HMAC secret key viewer with copy-to-clipboard and confirmation modal for secret rotation.
- `src/components/webhooks/SimulatorResultCard.tsx`: Diagnostic inspector rendering HTTP status pill, round-trip latency, computed HMAC signature, payload viewer, and headers.
- `src/components/webhooks/EventSimulatorPanel.tsx`: Interactive simulator form supporting event type selection and custom destination targets.
- `src/components/webhooks/WebhooksConsole.tsx`: Main split-panel container with role gating (`Merchant` / `Ops`).
- `src/App.tsx`: Wired `WebhooksConsole` to the Webhooks tab.

#### 5. Automated Tests
- Unit Tests: `MerchantWebhookHandlersTests.cs` (7 test cases).
- Integration Tests: `MerchantWebhookEndpointsIntegrationTests.cs` (8 test cases with PostgreSQL Testcontainers and WireMock).
- Frontend Tests: `apiClient.test.ts` (expanded to 5 test cases, 7 total Vitest tests).

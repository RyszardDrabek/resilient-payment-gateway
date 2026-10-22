# Release Notes

## Release v1.1.0 - Operational Hardening & External FX Rates Feed

**Resilient Payment Gateway v1.1.0** introduces cluster operational hardening, single-leader distributed locking, live foreign exchange rates feed integration, and standardized Kubernetes lifecycle probes.

### Highlights in v1.1.0

#### 1. Cluster Scalability & Distributed Locking (Advisory Locks)
- **Cluster Single-Leader Scheduling**: Background workers now utilize PostgreSQL Advisory Locks (`IDistributedLockProvider`) to safely scale worker pods (`HorizontalPodAutoscaler` > 1) without concurrent cycle overlaps:
  - `IdempotencyRetentionCleanupWorker`
  - `OutboxRetentionCleanupWorker`
  - `MerchantWebhookRetentionCleanupWorker`
  - `FxQuoteExpirationWorker`
  - `FxRateRefreshWorker`
  - `DailyReconciliationWorker`
  - `SubscriptionBillingWorker`
- **Clean Worker Separation**: Clear separation of responsibilities between API and Worker pods (`Outbox:Enabled = false` on API, `true` on Worker), removing database outbox polling contention.

#### 2. Live Foreign Exchange (FX) Engine & Synchronized Caching
- **NBP Live Feed Integration**: Added `NbpFxFeedClient` consuming public NBP Table A API with automatic normalization to USD baseline and dedicated `User-Agent` protection.
- **Fail-Open Resilience**: Automatic fallback to normalized baseline rates on network degradation.
- **Cross-Node Cache Synchronization**: `FxRateRefreshWorker` broadcasts `FxRatesRefreshedEvent` via MassTransit, ensuring all stateless API instances immediately synchronize their local in-memory rates (`CachedFxRateProvider`).
- **Guaranteed FX Quoting API**: `POST /fx/quotes` and `GET /fx/rates` endpoints for multi-currency settlement.

#### 3. Kubernetes Probes & Operational Standards
- **Standard Probes**: Added `/health/live` (liveness) and `/health/ready` (readiness with outbox queue lag inspection).
- **Backward Compatibility**: Fully preserved existing `/healthz` and `/health` aliases.
- **Rate Limiting Hardening**: Added RFC 7807 compliant 429 Too Many Requests responses including `Retry-After: 60` HTTP headers.

#### 4. Expanded Verification & Quality Metrics
- **Unit Tests**: 539 / 539 passing tests (0 failures, 0 skipped).
- **Architecture Tests**: 3 / 3 Clean Architecture boundary tests passed.
- **Portal E2E Tests**: 22 / 22 Playwright tests passing across all operator consoles.
- **k6 Load Tests**: Rate limiting suite updated to assert `Retry-After` header presence.
- **PowerShell E2E Suite**: Added Kubernetes health probe and live FX quoting verification scenarios.

---

# Release v1.0.0 - Resilient Payment Gateway

**Resilient Payment Gateway v1.0.0** marks the first stable production release of an enterprise-grade FinTech payment processing engine built with .NET 10 (C# 13), React 19, PostgreSQL (with pgvector), RabbitMQ, and full OpenTelemetry distributed observability.

---

## Key Highlights & Architecture

### 1. Dual-Channel Payment Routing (FIAT & Web3)
- **Unified Domain Model**: Clean Architecture and CQRS decoupling domain rules from external settlement details.
- **FIAT (Adyen v71)**: Integration with Adyen Checkout API, supporting embedded WireMock for zero-secret development/CI and live sandbox configurations.
- **Web3 (USDC / Stablecoin)**: Direct blockchain settlement port with multi-block confirmation and finality tracking.

### 2. Comprehensive Financial Lifecycle
- **Verbs**: `Authorize`, `Capture` (full & partial), `Refund` (full & partial), and `Cancel`.
- **3D Secure 2 / SCA Flow (Phase 10)**: Compliance with EU PSD2 / Strong Customer Authentication:
  - `RequiresAction` state handling challenge redirections/iframes.
  - Interactive challenge resolution (`POST /payments/{id}/3ds-result`).
  - Automated background TTL challenge sweeper worker (`ThreeDsExpirationWorker`).

### 3. Bank-Grade Reliability & Resilience
- **Strict Idempotency**: `Idempotency-Key` tracking across commands to prevent duplicate charges or operations.
- **Transactional Outbox Pattern**: Financial state changes and domain events commit atomically to PostgreSQL, dispatched via RabbitMQ + MassTransit.
- **Outbox Retention Worker**: Automatic background pruning of historical dispatched outbox records.
- **Resilient Webhook Delivery**: Outbound merchant webhooks with HMAC-SHA256 signatures, exponential backoff retries, and dead-letter queue (DLQ) tracking with manual/automatic replay capabilities.

### 4. Background AML & AI Risk Scoring
- Asynchronous risk evaluation powered by `pgvector` user context embeddings and rule heuristics.
- Non-blocking *fail-open* architecture ensuring payment path latency remains unaffected if risk services degrade.

### 5. Operator & Merchant Portal
- Modern **React 19 / TypeScript / Tailwind CSS** portal with six operational consoles:
  - **Operator Overview & Unresolved Payments**: Status breakdown, age filters, manual resolution actions.
  - **Merchant Webhook Self-Service**: Webhook registration, secret rotation, and test simulation.
  - **Compliance Risk Review**: AML flag inspections and dispositions (true/false positive).
  - **Merchant API Keys**: Self-service scoped key generation (`payments:read`, `payments:write`) with one-time raw secret reveal.
  - **Webhook DLQ Console**: Failure diagnostics and one-click manual replay.
  - **3DS2 Simulator Console**: Live interactive challenge simulator with preset scenarios (frictionless, challenge, decline).

### 6. Production Observability & Ops
- **Prometheus Metrics**: Detailed business volumes, decline counters, channel latencies, and outbox/DLQ telemetry.
- **Grafana Cockpits**: Pre-configured dashboards for business volume, latency percentiles (p50, p95, p99), and DLQ health.
- **Alertmanager**: Prometheus alerting rules for SLA breaches (p95 > 2s), high decline rates (>5%), and DLQ spikes.
- **W3C Distributed Tracing**: Seamless `traceparent` propagation across HTTP, MediatR, Outbox, RabbitMQ, and external webhooks to Jaeger.

### 7. Deployment & DevOps
- **Docker Compose**: One-command complete local environment (`compose/docker-compose.yml`).
- **Kubernetes & Helm**: Production-ready Helm chart (`deploy/helm/payment-gateway`) with health probes, autoscaling (HPA), and config templates.
- **CI/CD Automation**: GitHub Actions workflows for continuous build, format verification, unit testing, integration testing, and Helm linting.

---

## Verification & Test Metrics

- **Unit Tests**: 416 / 416 passing tests (xUnit, FluentAssertions).
- **Integration Tests**: End-to-end API tests with real PostgreSQL and RabbitMQ via Testcontainers.
- **Playwright E2E Tests**: 22 / 22 passing tests covering all portal workflows and 3DS2 simulation.
- **k6 Load Benchmarks**:
  - `01-smoke.js`: Baseline sanity check.
  - `02-load-sla.js`: Multi-tenant load verification under SLA thresholds.
  - `03-rate-limiting.js`: Gateway token-bucket rate limiting enforcement.
  - `04-spike-and-recovery.js`: Extreme traffic spikes and recovery validation.
  - `05-3ds2-challenge-flow.js`: 1200+ 2-step 3DS2 flows with 0% errors and p95 latency < 35ms.

---

## Quick Start

```bash
# Clone the repository
git clone https://github.com/RyszardDrabek/resilient-payment-gateway.git
cd resilient-payment-gateway

# Launch complete ecosystem (API, Worker, Portal, Postgres, RabbitMQ, Jaeger, Prometheus, Grafana)
docker compose -f compose/docker-compose.yml up -d --build
```

- **Portal**: http://localhost:3000
- **Gateway API & Swagger**: http://localhost:5080/swagger / http://localhost:5080/health
- **Grafana**: http://localhost:3001 (admin/admin)
- **Jaeger Tracing**: http://localhost:16686
- **RabbitMQ Management**: http://localhost:15672 (guest/guest)

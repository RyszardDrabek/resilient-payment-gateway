# ADR-016: Real-time Operational Observability and Alerting Console

## Status

Accepted — 2026-10-19

## Context

Phase 9 completed Prometheus Alerting Rules, Alertmanager configuration, and distributed tracing. However, operations engineers, incident responders, and compliance officers lacked a unified operational cockpit within the web management portal (`PaymentGateway.Portal`).

Specifically:
- Accessing Prometheus (port 9090), Alertmanager (9093), and Jaeger (16686) requires navigating multiple separate web UIs and exposing internal infrastructure ports outside secure network boundaries.
- Support teams need immediate at-a-glance status of cluster infrastructure (API health, PostgreSQL connectivity with latency, RabbitMQ broker status), active firing alerts, latency SLA percentiles, and queue backlogs (Transactional Outbox and Webhook DLQ).
- When issues occur (such as a spike in dead-lettered webhooks or unresolved pending payments), operators need deep-links directly into the remediation consoles (`F-PORTAL-01`, `F-PORTAL-05`).

## Decision

The project implements an in-portal System Observability & Reliability cockpit backed by a high-performance aggregation endpoint:

1. **Backend Aggregation Endpoint (`GET /api/v1/ops/observability`):**
   - Implemented `IObservabilityService` and `ObservabilityService` in `PaymentGateway.Infrastructure`.
   - Exposed under `OpsObservabilityEndpoints` and protected by `ObservabilityPolicy` (accessible to `ops`, `merchant`, and `risk` roles).
   - Collects core component health:
     - `PaymentGateway.Api`: Status (`Healthy`/`Degraded`), version, environment, process uptime.
     - `Database`: Concurrent `CanConnectAsync` checks against `PaymentDbContext` and `RiskDbContext` with measured round-trip response time in milliseconds.
     - `MessageBroker`: Status, provider type (`RabbitMQ` vs `InMemory`), host connection.
     - `MonitoringServices`: Reachability checks for Prometheus and Alertmanager.
   - Queries Alertmanager v2 API (`/api/v2/alerts`) for active firing alerts, categorizing them by severity (`critical`, `warning`), summary, description, and PromQL labels.
   - Evaluates latency SLAs against targets:
     - Acquirer settlement p95 latency (< 2.0s SLA).
     - Outbox dispatch duration p95 (< 500ms).
     - Payment decline rate (< 5.0%).
   - Aggregates queue telemetry:
     - Transactional Outbox: pending messages count, dispatched count, failed/retrying count, oldest pending message age.
     - Webhook DLQ: exhausted count, replayed count, total count.
     - Unresolved Payments: pending and unknown status counts.

2. **Parallel Resource Evaluation (`Task.WhenAll`):**
   - Independent checks run concurrently:
     - `paymentDb.Database.CanConnectAsync` and `riskDb.Database.CanConnectAsync` run in parallel.
     - Alertmanager HTTP check and Prometheus HTTP check run in parallel.
     - Network monitoring checks execute concurrently with database queries, cutting endpoint response times in half without violating EF Core single-context concurrency constraints.

3. **React Portal Observability Console (`ObservabilityConsole.tsx`):**
   - Added `observability` tab to the portal's navigation rail (`AppShell.tsx`).
   - Component features:
     - Cluster health cards with status badges and live latency metrics.
     - Alertmanager live alert feed with severity pill filters (`All`, `Critical`, `Warning`) and empty-state nominal confirmation.
     - Latency SLA & QoS visual gauges with SLA breach indicators.
     - Queue backlog telemetry with direct navigation buttons to Webhook DLQ and Unresolved console tabs.
     - Configurable auto-refresh interval selector (`Off`, `5s`, `10s`, `30s`), manual refresh button with spinner, and last-refreshed timestamp.
     - Configurable external tooling quick-launch links (Grafana, Prometheus, Alertmanager, Jaeger) driven by Vite environment variables (`VITE_GRAFANA_URL`, `VITE_PROMETHEUS_URL`, `VITE_ALERTMANAGER_URL`, `VITE_JAEGER_URL`).

## Consequences

- (+) **Unified Operational Cockpit:** Operators can monitor health, detect firing alerts, track SLAs, and triage queue backlogs from a single interface.
- (+) **Secure Boundary:** Internal monitoring infrastructure ports (9090, 9093) remain shielded behind the gateway API; the browser interacts solely with the authenticated gateway API.
- (+) **Graceful Degradation:** If Alertmanager or Prometheus is unreachable (e.g. in standalone local testing), the endpoint flags them as `Unreachable` and falls back to database-derived telemetry without crashing the console.
- (+) **Cross-Console Deep-linking:** One-click transitions from queue telemetry cards directly to DLQ replay or unresolved payment reconciliation.
- (-) **API Aggregation Load:** Polling `/api/v1/ops/observability` executes database count queries and health checks; bounded by configurable client-side polling intervals (default 10s).

## Alternatives Considered

1. **Direct Frontend AJAX Calls to Alertmanager & Prometheus:**
   - *Rejected:* Exposing internal monitoring ports (9090, 9093) to the public browser violates container isolation, introduces cross-origin CORS complexity, and bypasses portal JWT role authentication.
2. **Embedding Full Grafana Dashboards via iFrames Only:**
   - *Rejected:* [ADR-014](adr-014-design-system.md) establishes a cohesive portal design system. Iframes break styling consistency, require complex cookie/auth proxying, have poor mobile responsiveness, and prevent deep-linking to DLQ/unresolved actions. Grafana remains accessible as an external tool for deep-dive investigation.

## Related ADRs

- [ADR-012: Operational Hardening — Outbox, Webhooks, Query](adr-012-operational-hardening-outbox-webhooks-query.md)
- [ADR-013: Web Portal Architecture, Docker Hosting & CORS Integration](adr-013-web-portal-architecture-hosting-cors.md)
- [ADR-014: Design System](adr-014-design-system.md)
- [ADR-015: Distributed Tracing and W3C Trace Context Propagation](adr-015-distributed-tracing-w3c-context-propagation.md)

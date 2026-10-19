---
id: F-PORTAL-07
topic: PORTAL
phase: operational-alerting-and-distributed-tracing
status: done
---

# F-PORTAL-07: Operator Web Portal Observability Console

- **Topic:** [PORTAL](../../topics/portal.md)
- **Type:** Explicit
- **Value class:** USER-VISIBLE
- **Source:** Phase 9: operational-alerting-and-distributed-tracing

## Description

Provide an operations observability tab inside the React operator web portal (`PaymentGateway.Portal`), displaying real-time cluster health statuses (API, PostgreSQL, RabbitMQ broker, Prometheus, Alertmanager), active firing alerts from Alertmanager, p95 latency percentiles against SLA thresholds (< 2.0s), payment decline rates, and queue backlog telemetry (Transactional Outbox and Webhook DLQ).

## Business context

Operations engineers and support teams require a unified operational cockpit in the management portal to observe real-time gateway health, detect active Prometheus alerts, monitor latency SLA compliance, and jump directly into DLQ or Unresolved payment resolution consoles.

## Acceptance criteria

- AC-1 THE backend SHALL expose `GET /api/v1/ops/observability` requiring authenticated role authorization (`ObservabilityPolicy`), aggregating service health, active Alertmanager alerts, p95 latency metrics, and queue telemetry.
- AC-2 THE React portal SHALL provide an `Observability` tab in `AppShell` with live status indicators, auto-refresh interval configuration (5s/10s/30s/off), and manual refresh.
- AC-3 THE console SHALL display active Alertmanager alerts with severity badges (`critical`, `warning`), alert names, descriptions, timestamps, and filter pills.
- AC-4 THE console SHALL display p95 latency percentiles against SLA targets (< 2.0s for settlement, < 500ms for outbox dispatch) and payment decline rates (< 5.0%).
- AC-5 THE console SHALL display Transactional Outbox backlog metrics (pending, dispatched, failed, oldest message age) and Webhook DLQ stats with navigation shortcuts to DLQ and Unresolved consoles.

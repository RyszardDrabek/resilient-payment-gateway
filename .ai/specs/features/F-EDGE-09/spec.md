---
id: F-EDGE-09
topic: EDGE
phase: operational-alerting-and-distributed-tracing
status: done
---

# F-EDGE-09: Prometheus Alerting Rules & Alertmanager operational alerting

- **Topic:** [EDGE](../../topics/edge.md)
- **Type:** Explicit
- **Value class:** USER-VISIBLE
- **Source:** Phase 9: operational-alerting-and-distributed-tracing

## Description

Define production-ready PromQL alerting rules in `compose/alert.rules.yml` and integrate an `alertmanager` service within `compose/docker-compose.yml`, alerting SREs and operators to high payment decline rates, acquirer settlement latency SLA breaches, merchant webhook DLQ exhaustion spikes, and transactional outbox delays.

## Business context

Payment gateways cannot rely solely on passive dashboards. When acquirer latency spikes or webhook DLQ queues swell with repeated failures, operators must be actively notified via Alertmanager to avert customer drop-off and merchant settlement disruptions.

## Acceptance criteria

- AC-1 THE rule file `compose/alert.rules.yml` SHALL contain rule `HighPaymentDeclineRate` triggering when overall decline percentage exceeds 5% over a 5m sliding window (`(sum(rate(payment_declines_total[5m])) / (sum(rate(payment_transactions_total[5m])) > 0)) * 100 > 5`).
- AC-2 THE rule file `compose/alert.rules.yml` SHALL contain rule `AcquirerSettlementLatencySlaBreach` triggering when p95 acquirer settlement latency exceeds 2.0 seconds (`histogram_quantile(0.95, sum by (le, channel) (rate(payment_channel_duration_seconds_bucket[5m]))) > 2.0`).
- AC-3 THE rule file `compose/alert.rules.yml` SHALL contain rule `MerchantWebhookDlqSpike` triggering when outbound webhook delivery retries exhaust or manual DLQ replays encounter failure.
- AC-4 THE rule file `compose/alert.rules.yml` SHALL contain rule `OutboxDeliveryDelayOrFailure` triggering when transactional outbox message publishing encounters errors or messages are dead-lettered.
- AC-5 THE compose configuration SHALL define an `alertmanager` service (`compose/alertmanager.yml`) on port 9093, wired into `prometheus.yml` under `alerting.alertmanagers` and provisioned in `compose/grafana-datasources.yml`.

## Interface contract

- **Resource / surface:** Prometheus alert evaluation (`/etc/prometheus/alert.rules.yml`) & Alertmanager REST API (`http://localhost:9093/api/v2/alerts`)
- **Alert names:** `HighPaymentDeclineRate`, `AcquirerSettlementLatencySlaBreach`, `MerchantWebhookDlqSpike`, `OutboxDeliveryDelayOrFailure`

## Dependencies

F-EDGE-08

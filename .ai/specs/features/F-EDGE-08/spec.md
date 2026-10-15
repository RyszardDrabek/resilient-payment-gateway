---
id: F-EDGE-08
topic: EDGE
phase: operational-observability-and-metrics
status: done
---

# F-EDGE-08: Financial volume, channel latencies & webhook DLQ Prometheus metrics

- **Topic:** [EDGE](../../topics/edge.md)
- **Type:** Explicit
- **Value class:** USER-VISIBLE
- **Source:** Phase 8: operational-observability-and-metrics

## Description

Instrument domain-level metrics in `PaymentGateway.Application` and `PaymentGateway.Worker` via `System.Diagnostics.Metrics` / OpenTelemetry, exposing financial volume counters (EUR / USDC), acquirer/channel latency histograms, and outbound webhook delivery / DLQ retry telemetry on the `/metrics` endpoint.

## Business context

Operators and SREs need real-time insight into gross merchandise volume (GMV), per-channel settlement performance, drop-offs, and webhook notification delivery health across merchants and acquirers.

## Acceptance criteria

- AC-1 THE system SHALL expose a counter `payment_volume_total` recording cumulative monetary amounts partitioned by `currency`, `channel`, `operation`, and `status`.
- AC-2 THE system SHALL expose a histogram `payment_channel_duration_seconds` measuring external acquirer / channel settlement execution duration tagged by `channel` (`ADYEN`, `WEB3`), `operation` (`authorize`, `capture`, `refund`), and `status`.
- AC-3 THE system SHALL expose a counter `merchant_webhook_dispatched_total` measuring outbound webhook deliveries partitioned by `status` (`delivered`, `failed`, `exhausted`, `replayed`).
- AC-4 THE system SHALL expose a counter `merchant_webhook_replay_attempts_total` tracking manual DLQ replay attempts partitioned by `outcome` (`success`, `failure`) and `status_code`.
- AC-5 THE system SHALL expose a histogram `merchant_webhook_dispatch_duration_seconds` measuring latency of outbound webhook HTTP requests.
- AC-6 All metrics SHALL be discoverable and scrapeable on the anonymous `/metrics` endpoint formatted per Prometheus text exposition format.

## Interface contract

- **Resource / surface:** `GET /metrics`
- **Operation:** Prometheus scrape
- **Response shape category:** text/plain; version=0.0.4
- **Load-bearing metrics:** `payment_volume_total`, `payment_channel_duration_seconds`, `merchant_webhook_dispatched_total`, `merchant_webhook_replay_attempts_total`, `merchant_webhook_dispatch_duration_seconds`

## Dependencies

F-PAY-01, F-EDGE-05, F-EDGE-07

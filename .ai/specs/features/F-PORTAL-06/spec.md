---
id: F-PORTAL-06
topic: PORTAL
phase: operational-observability-and-metrics
status: done
---

# F-PORTAL-06: Operational Grafana cockpits for business volume, latency percentiles & DLQ health

- **Topic:** [PORTAL](../../topics/portal.md)
- **Type:** Explicit
- **Value class:** USER-VISIBLE
- **Source:** Phase 8: operational-observability-and-metrics

## Description

Provide production-grade Grafana dashboards pre-configured in `compose/dashboards/` and auto-provisioned via `compose/grafana-dashboards.yml`, visualizing business volume KPIs (EUR/USDC by channel), acquirer settlement latency percentiles (p50, p95, p99), outbound webhook delivery rates, and DLQ exhaustion vs replay trends.

## Business context

Operations teams and management require at-a-glance visualization of payment gateway financial throughput, provider performance SLAs, and alerting thresholds for delivery bottlenecks.

## Acceptance criteria

- AC-1 THE dashboard `compose/dashboards/payment-gateway-operations.json` SHALL include KPI stat cards for Total Transaction Count, Processed Volume (EUR & USDC), Decline Rate %, and Active DLQ Count.
- AC-2 THE dashboard SHALL display time-series charts for Financial Volume Throughput over time broken down by currency and channel.
- AC-3 THE dashboard SHALL display time-series charts for Settlement Channel Execution Latencies (p50, p95, p99) comparing ADYEN and WEB3 channels.
- AC-4 THE dashboard SHALL display Webhook Delivery Health charts comparing successful deliveries, retry attempts, DLQ exhaustions, and manual replay outcomes.
- AC-5 Grafana provisioning in `compose/grafana-dashboards.yml` SHALL auto-mount and discover the dashboard on container startup without manual import steps.

## Interface contract

- **Resource / surface:** Grafana dashboard JSON model (`compose/dashboards/payment-gateway-operations.json`)
- **Visual panels:** KPI Stat summary, Volume timeseries, Latency heatmap/percentiles, Webhook & DLQ lifecycle flow

## Dependencies

F-EDGE-08

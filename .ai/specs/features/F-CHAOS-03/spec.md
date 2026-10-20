---
id: F-CHAOS-03
topic: CHAOS
phase: chaos-engineering-and-resilience-verification
status: done
---

# F-CHAOS-03: Acquirer latency degradation & database transient failure resilience

- **Topic:** [CHAOS](../../topics/chaos.md)
- **Type:** Explicit
- **Value class:** RESILIENCE
- **Source:** ADR-024

## Description

Verifies the Edge HTTP API layer resilience under simulated upstream acquirer degradation (extreme latency >5s, socket resets) and transient database disconnects. Verifies that client callers receive standardized RFC 7807 problem details with correlation IDs without thread pool starvation, and that k6 load testing under chaos conditions demonstrates zero data corruption.

## Business context

Fulfills SLA and operational availability guarantees, ensuring downstream merchant clients never experience hanging HTTP sockets during upstream partner outages.

## Acceptance criteria

- AC-1 WHEN upstream settlement channels experience severe latency or network blackholes, the API SHALL enforce HTTP timeout limits and return RFC 7807 problem details without leaking connections.
- AC-2 WHEN PostgreSQL experiences transient connection drops, Polly retry policies SHALL transparently retry transient queries and preserve idempotency key leases.
- AC-3 A k6 chaos resilience scenario `06-chaos-resilience.js` SHALL execute sustained payment load under dependency degradation, asserting zero transaction loss and clean recovery.

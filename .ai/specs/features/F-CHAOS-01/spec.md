---
id: F-CHAOS-01
topic: CHAOS
phase: chaos-engineering-and-resilience-verification
status: done
---

# F-CHAOS-01: Toxiproxy fault injection orchestration & chaos drill harness

- **Topic:** [CHAOS](../../topics/chaos.md)
- **Type:** Explicit
- **Value class:** RESILIENCE
- **Source:** ADR-024

## Description

Configures Toxiproxy service integration in `docker-compose.yml` (under `chaos` profile) and provides automated chaos drill test runners capable of injecting controlled network degradation (latency, jitter, bandwidth throttling, socket reset, timeout/blackhole) into PostgreSQL, RabbitMQ, and external acquirer settlement channels.

## Business context

Fulfills operational reliability standards by validating that the payment gateway gracefully handles network partitions and dependency outages without thread exhaustion or crashed services.

## Acceptance criteria

- AC-1 THE Docker Compose configuration SHALL provision `toxiproxy` on ports 8474 (API), 5433 (Postgres proxy), and 5673 (RabbitMQ proxy) under the `chaos` profile.
- AC-2 THE chaos test harness SHALL support programmatic injection of latency, bandwidth limits, TCP resets, and full connection cuts.
- AC-3 AN automated PowerShell drill script `tests/chaos/run-chaos-suite.ps1` SHALL execute automated failure injection against running containers and verify recovery.

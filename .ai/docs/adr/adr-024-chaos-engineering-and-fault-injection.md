# ADR-024: Chaos Engineering, Automated Fault Injection & Zero-Data-Loss Resilience Verification

## Status

Accepted — 2026-10-20

## Context

Financial core processing systems must withstand real-world operational degradation—network partitions, database failovers, message broker restarts, and slow upstream acquirers—without losing transactions, double-charging customers, or leaving state in inconsistent limbo:

1. **Transactional Outbox Resilience**: If RabbitMQ crashes or network connectivity is severed, payments must continue to be captured in PostgreSQL and their lifecycle events must never be lost. When connectivity returns, the Outbox dispatcher must resume and deliver 100% of pending events.
2. **Transient Database Disconnects**: During an Aurora PostgreSQL failover or transient network blip, Polly retry policies and connection pool recovery must prevent transaction failure where possible and ensure idempotency leases remain intact without duplicate charges.
3. **Upstream Acquirer Degradation**: When payment processors (Adyen, card networks) experience severe latency spikes (>5 seconds) or connection resets, the gateway must avoid thread starvation and resource exhaustion, returning clean RFC 7807 problem details with correlation IDs and tripping circuit breakers when error rates exceed thresholds.
4. **Consumer Crashes & Redelivery**: If a background worker terminates abruptly midway through consuming an event (e.g. webhook delivery or reconciliation), redelivered messages must be processed idempotently without executing duplicate external side effects.

## Decision

The gateway establishes an automated **Chaos Engineering & Fault Injection Framework** leveraging **Toxiproxy** and dedicated test suites:

### 1. Network Fault Injection Architecture (Toxiproxy)
- **Toxiproxy service** added to `compose/docker-compose.yml` under the `chaos` profile for orchestrated network degradation.
- Supports programmatic injection of real-world "toxics":
  - **Latency & Jitter**: Injects controlled delays (e.g., +2000ms ± 500ms).
  - **Bandwidth Throttling**: Restricts data rate to simulate degraded WAN connections.
  - **Connection Reset (TCP RST)**: Closes sockets abruptly to test reconnection logic.
  - **Network Blackhole (Timeout)**: Drops all incoming packets to simulate a complete network partition.

### 2. Automated Integration Chaos Test Suite
Integration tests under `tests/PaymentGateway.IntegrationTests/Chaos/` verify core resilience invariants:
- **`BrokerOutageOutboxZeroLossTests`**: Simulates complete RabbitMQ unavailability during active payment authorization and capture. Proves that all transactions succeed, outbox records remain queued, and upon broker recovery, 100% of outbox events are dispatched in FIFO order.
- **`DatabaseTransientDropResilienceTests`**: Verifies Polly retry policies and idempotency lease safety during connection blips.
- **`AcquirerLatencyDegradationTests`**: Tests upstream acquirer degradation, verifying HTTP timeout boundaries, RFC 7807 problem details with correlation IDs, and non-blocking circuit breaking.
- **`ConsumerCrashIdempotencyTests`**: Simulates worker crash and redelivery of MassTransit events, proving duplicate delivery does not cause duplicate side effects.

### 3. Load Testing Chaos Drill (k6)
- Scenario `tests/load/scenarios/06-chaos-resilience.js`: Generates sustained 50 RPS payment load while simultaneously injecting network faults into dependencies.
- Metrics verified: Error budget adherence, outbox queue drain time, and 0 duplicate charges.

### 4. Chaos Drill Automation Script
- `tests/chaos/run-chaos-suite.ps1`: End-to-end PowerShell script automating environment startup, toxic configuration, test execution, metric collection, and teardown.

## Consequences

- **Verifiable Resilience**: Proves mathematical zero data loss and idempotency under adverse production failure modes.
- **Continuous Reliability Verification**: Chaos tests run as part of pre-release validation to prevent resilience regressions.
- **Clean Failure Boundaries**: Asserts that downstream clients always receive RFC 7807 error contracts rather than hanging HTTP connections.

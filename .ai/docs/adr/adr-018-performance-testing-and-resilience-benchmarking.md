# ADR-018: Performance Testing, Load Benchmarking & Rate Limiting Resilience

## Status

Accepted — 2026-10-19

## Context

Following the implementation of real-time operational observability (ADR-016), distributed tracing (ADR-015), and CI/CD Kubernetes deployment (ADR-017), the platform required automated verification of SLA thresholds, concurrency boundaries, and rate limiting resilience under load.

Specifically:
- ADR-007 and ADR-016 established concrete operational SLAs:
  - Acquirer settlement p95 latency < 2.0s, p99 < 3.0s.
  - Gateway API liveness/readiness probes < 500ms.
  - Rate limiting enforcement via `GatewayRateLimiter` returning HTTP 429 RFC 7807 problem details when exceeding 100 req/min per caller or 50 global concurrent in-flight requests.
- Manual testing cannot simulate multi-tenant concurrency, traffic spikes, or saturation of the transactional outbox and worker consumers.
- A standardized, scriptable load testing harness was needed for developers and performance engineers to run locally or containerized in orchestration environments.

## Decision

The project adopts **Grafana k6** as the standardized performance and resilience testing tool, organized under `tests/load/`:

1. **Modular k6 Architecture (`tests/load/helpers.js`):**
   - Cryptographic HS256 JWT generation using k6 native `k6/crypto` for zero-dependency test runs.
   - Dynamic `Idempotency-Key` generation ensuring each simulated payment has financial isolation.
   - Randomized payment payload generation across supported settlement channels (`mocked-adyen`, `web3`) and currencies (`EUR`, `USD`, `PLN`).

2. **Standardized Scenario Suite (`tests/load/scenarios/`):**
   - **`01-smoke.js` (Sanity & Contract Verification):**
     - Low-concurrency check (2 VUs) probing `/healthz`, `/health`, `/payments`, `/api/v1/ops/observability`, and `/metrics`.
   - **`02-load-sla.js` (Sustained Load & SLA Contract):**
     - Multi-tenant load ramped to 25 VUs across distinct merchant partitions (`merch_load_${__VU}`).
     - Thresholds: `http_req_failed < 1%`, `http_req_duration p(95) < 2000ms`, `p(99) < 3000ms`.
   - **`03-rate-limiting.js` (Overload Resilience & 429 RFC 7807):**
     - Floods a single merchant partition at 80 req/s to intentionally exhaust the 100 req/min token bucket.
     - Asserts that excess requests receive HTTP 429 with `Content-Type: application/problem+json` and valid RFC 7807 payload without crashing the API.
   - **`04-spike-and-recovery.js` (Spike & Recovery Dynamics):**
     - Sudden surge from baseline (5 VUs) to severe spike (75 VUs) for 15 seconds, followed by recovery.
     - Asserts zero 5xx server errors and rapid post-spike latency normalization (`recovery_phase_latency p(95) < 1000ms`).

3. **Container Orchestration (`compose/docker-compose.yml`):**
   - Added `k6` service under the `loadtest` compose profile (`grafana/k6:latest`), allowing execution inside the Docker network against the gateway cluster via `docker compose --profile loadtest run --rm k6 run /scripts/scenarios/02-load-sla.js`.

## Consequences

### Positive
- Formalizes SLA contracts into executable code with automated thresholds.
- Verifies that rate limiting protects upstream acquirers and downstream databases from denial of service.
- Enables telemetry verification during load tests across Prometheus, Grafana, Jaeger, and the Operator Portal.

### Negative / Trade-offs
- k6 scenarios require maintenance if authentication schemes or payment payload schemas change.

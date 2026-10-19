# Resilient Payment Gateway — Load & Resilience Testing (k6)

This directory contains automated performance, load, stress, and resilience scenarios implemented with [k6](https://k6.io/).

---

## Scenarios Overview

| Scenario | File | Purpose | SLA / Target Thresholds |
| -------- | ---- | ------- | ----------------------- |
| **01. Smoke** | `scenarios/01-smoke.js` | Fast health & functionality verification across endpoints | `http_req_failed < 1%`, `p95 < 500ms` |
| **02. Load & SLA** | `scenarios/02-load-sla.js` | Sustained multi-tenant load test against SLA targets | `p95 < 2000ms`, `p99 < 3000ms`, error rate `< 1%` |
| **03. Rate Limiting** | `scenarios/03-rate-limiting.js` | Overload resilience & RFC 7807 429 Too Many Requests verification | Rate limiter engages, 429 problem details verified |
| **04. Spike & Recovery** | `scenarios/04-spike-and-recovery.js` | Sudden severe traffic spike (5 → 75 VUs) and post-spike latency recovery | `p95 < 1000ms` post-recovery, zero 5xx server errors |
| **05. 3DS2 Challenge Flow** | `scenarios/05-3ds2-challenge-flow.js` | End-to-end 2-stage SCA challenge and resolution load test | `http_req_failed < 1%`, `p95 < 2000ms`, `count > 50` |

---

## Running Locally

Ensure `k6` is installed and the Payment Gateway is running:

```bash
# 1. Run Smoke Scenario
k6 run tests/load/scenarios/01-smoke.js

# 2. Run Load & SLA Verification
k6 run tests/load/scenarios/02-load-sla.js

# 3. Run Rate Limiting Stress Test
k6 run tests/load/scenarios/03-rate-limiting.js

# 4. Run Spike & Recovery Test
k6 run tests/load/scenarios/04-spike-and-recovery.js
```

### Environment Overrides

```bash
GATEWAY_URL="http://localhost:5080" \
JWT_SECRET="DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!" \
k6 run tests/load/scenarios/02-load-sla.js
```

---

## Running with Docker Compose

A dedicated `k6` service is available under the `loadtest` compose profile:

```bash
# Start background infrastructure (PostgreSQL, RabbitMQ, Jaeger, Prometheus, Gateway API)
docker compose up -d postgres rabbitmq jaeger prometheus api

# Execute load test in container
docker compose --profile loadtest run --rm k6 run /scripts/scenarios/02-load-sla.js
```

---

## Telemetry Correlation

While k6 runs, real-time metrics and distributed traces can be monitored across:
- **Grafana Dashboard:** `http://localhost:3001` (Dashboard: *Payment Gateway — System Operations & Telemetry*)
- **Prometheus Metrics:** `http://localhost:9090` (Queries: `payment_gateway_payment_duration_seconds_bucket`, `payment_gateway_payments_authorized_total`)
- **Jaeger Tracing:** `http://localhost:16686` (Service: `PaymentGateway.Api`)
- **Portal Observability Console:** `http://localhost:3000` (Tab: *Observability*)

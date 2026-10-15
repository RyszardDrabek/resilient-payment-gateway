# Delivery phases

## Phase 1: mocked-payment-lifecycle

**Objective:** A clone-and-run payment gateway: authorize, capture, refund, and cancel against an embedded Adyen mock, with idempotency, transactional outbox, payment API contract, and instrument masking — no secrets required.
**Evaluation criteria:** Fresh clone with no credentials can complete the payment lifecycle against WireMock; duplicate idempotency key has one financial effect; process kill does not lose an outbox event (proven with a phase-1 test consumer); no payment-instrument values appear in traces or logs; RFC 7807 problem documents cover payment-domain failures; overload is rejected by the gateway rate limiter with a problem document.

### Topics

- **PAY** — Provider-agnostic payment aggregate, state machine, CQRS API, idempotency, settlement port.
  - _Depends on:_ none
- **EDGE** — Payment gateway HTTP contract: problem details, rate limiting, telemetry masking of instruments.
  - _Depends on:_ PAY
- **EVT** — Transactional outbox and published financial event vocabulary for downstream consumers.
  - _Depends on:_ PAY
- **ADYEN** — Adyen channel. Phase 1 delivers secretless WireMock mode; live sandbox belongs to phase 2.
  - _Depends on:_ PAY

## Phase 2: live-adyen-settlement

**Objective:** Drive the same payment lifecycle against Adyen test/sandbox credentials, with HMAC-verified webhooks and asynchronous status reconciliation.
**Evaluation criteria:** Live mode selected with valid credentials settles against Adyen sandbox; live mode selected with absent/invalid credentials does not fall through to a fake live path; live mode not selected continues WireMock default; tampered webhooks are rejected; already-admitted notification replays do not mutate payment state; out-of-order and duplicate notifications do not corrupt payment state.

### Topics

- **ADYEN** — Live sandbox mode, HMAC webhook ingestion, status reconciliation, acquirer failure policy.
  - _Depends on:_ PAY, EVT

## Phase 3: web3-settlement-channel

**Objective:** Settle the same domain payment through a Web3 stablecoin channel (e.g. USDC) without leaking provider branching into the domain model.
**Evaluation criteria:** Architecture test forbids domain-layer Web3 types; a payment can complete via the Web3 port; “settled” is defined against an explicit finality policy.

### Topics

- **WEB3** — Second settlement-port implementation, on-chain confirmation/finality, crypto amount and asset semantics.
  - _Depends on:_ PAY, EVT

## Phase 4: ai-risk-scoring

**Objective:** Produce AML/fraud risk flags from payment events and per-user historical context without blocking the payment path.
**Evaluation criteria:** A risk verdict (or explicit degradation) is produced for a payment lifecycle event; payment latency is unaffected when scoring is slow or down (fail-open), proven with a fault-injection harness; a risk/compliance caller can list and disposition a flag via API.

### Topics

- **RISK** — Event-driven scoring pipeline, user risk-context memory, anomaly flags, review/disposition surface, degradation policy.
  - _Depends on:_ EVT

## Phase 5: operational-hardening-and-partial-settlement

**Objective:** Support partial captures and partial refunds across payment channels, establish background operational hygiene via transactional outbox retention pruning, expose operator visibility into unresolved payments across channels, and deliver outbound merchant webhook notifications on payment completion.
**Evaluation criteria:** Partial capture leaves remaining authorized balance capturable or cancellable; partial refund allows subsequent refunds up to captured total; outbox pruning worker safely removes sent messages older than TTL without affecting unread/in-flight messages; authenticated operator API queries payments in unresolved state filtered by channel and age; merchant endpoint receives HMAC-signed webhook upon payment transition to terminal state.

### Topics

- **PAY** — Partial capture and partial refund operations on payment aggregate, multiple partial transactions tracking, remaining balance validation.
  - _Depends on:_ PAY
- **EVT** — Transactional outbox retention policy and background cleanup worker.
  - _Depends on:_ EVT
- **EDGE** — Unresolved payments operator query API and outbound merchant webhook notification dispatcher with HMAC signing.
  - _Depends on:_ PAY, EVT

## Phase 6: merchant-and-operator-portal

**Objective:** Deliver an interactive, responsive web portal for merchants (webhook configuration & simulation) and operators (unresolved payments monitoring, metrics dashboard, and AML risk flag review/disposition).
**Evaluation criteria:** Merchant can add, test, and rotate webhook secrets via UI; operator can filter and investigate unresolved transactions; risk officer can inspect and disposition risk flags from the web console.

### Topics

- **PORTAL** — Web application layer providing operator dashboard, merchant webhook self-service, and compliance risk disposition consoles.
  - _Depends on:_ EDGE, RISK, PAY

## Phase 7: merchant-api-keys-and-webhook-dlq

**Objective:** Provide secure API key authentication and scoped permission management for merchants, paired with webhook delivery resilience, dead-letter queue (DLQ) tracking, failure diagnostics, and replay capabilities.
**Evaluation criteria:** Merchant can issue, scope, and revoke API keys with one-time secret reveal; webhook delivery failures exceeding max retries land in DLQ storage; operator and merchant can inspect delivery failure diagnostics and trigger manual re-dispatch from both the REST API and the web portal console.

### Topics

- **EDGE** — Merchant API key authentication, permission scoping, webhook DLQ storage, and manual re-dispatch API.
  - _Depends on:_ PAY, EVT
- **PORTAL** — Web portal consoles for merchant API key management and webhook DLQ delivery logs with replay trigger.
  - _Depends on:_ EDGE, PORTAL

## Phase 8: operational-observability-and-metrics

**Objective:** Expose rich domain, financial, and operational metrics via OpenTelemetry/Prometheus covering transaction volume, settlement channel latency, webhook delivery lifecycle, and DLQ diagnostics, paired with out-of-the-box Grafana operational cockpits.
**Evaluation criteria:** The `/metrics` endpoint exposes counters and histograms for financial volume (by currency and channel), settlement channel latency, outbound webhook deliveries, and DLQ replay attempts; Grafana dashboard visualizes transaction throughput, channel latency percentiles (p50, p95, p99), and DLQ health.

### Topics

- **EDGE** — Domain and operational Prometheus metrics for payments, channels, webhooks & DLQ.
  - _Depends on:_ PAY, EVT
- **PORTAL** — Pre-configured Grafana operational cockpits for business volume, latency percentiles, and DLQ diagnostics.
  - _Depends on:_ EDGE


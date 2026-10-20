# ADR-025: Pre-Settlement Inline Risk Gating and Fraud Decision Engine

## Status
Accepted

## Context
In Phase 4 (ADR-009), the payment gateway introduced asynchronous, off-path risk scoring via MassTransit domain events (`PaymentLifecycleEvent`). This ensured zero latency impact on payment processing. However, because fraud detection was strictly post-authorization, fraudulent transactions were still submitted to acquirers, incurring interchange fees, settlement risk, and potential chargebacks.

To protect merchants and the gateway, an ultra-fast inline fraud evaluation step must precede acquirer settlement while preserving the system's core tenet: **never allow risk assessment latency or outages to block legitimate customer payments** (fail-open architecture).

## Decision
1. **Inline Pre-Settlement Gating Port (`IInlineRiskEvaluator`):**
   - Place an inline evaluation hook in `AuthorizePaymentCommandHandler` *before* delegating to `ISettlementPort.AuthorizeAsync(...)`.
   - Provide a strict time budget (default 50 ms).

2. **Tri-State Decision Matrix:**
   - **`Allow` (RiskScore < 60):** Payment proceeds to settlement normally (frictionless flow).
   - **`Challenge3Ds` (60 <= RiskScore < 85):** Payment enforces Step-Up 3D Secure 2 authentication to shift liability away from the merchant.
   - **`Block` (RiskScore >= 85 or blacklisted/fraudulent patterns):** Payment is declined immediately with reason `FRAUD_BLOCKED`. The acquirer is **never invoked**, saving processing costs and eliminating chargeback exposure.

3. **Resilience & Fail-Open Semantics:**
   - If the inline risk evaluation exceeds the 50 ms budget or encounters an unhandled exception, it immediately logs a warning, emits `risk_evaluation_timeout_total` metrics, and **fails open** (`Decision = Allow, IsDegraded = true`).
   - Network dropouts or vector database slowness will never block payment processing.

4. **Auditability & Operator Transparency:**
   - Every blocked transaction immediately persists a `RiskFlag` in PostgreSQL for compliance inspection in the Operator Portal.
   - Metrics are recorded under `PaymentMetrics.RecordDecline("FRAUD_BLOCKED", channel)`.

5. **HTTP Contract & Idempotency Semantics for Upfront Blocks:**
   - When an authorization is blocked upfront by inline risk rules, the system creates and persists a `Payment` entity in the `Declined` state and completes the idempotency lease with HTTP `201 Created` (containing `State = "Declined"` and `DeclineReason = "FRAUD_BLOCKED: {reason}"`).
   - **Rationale (201 Created vs 422/400):** A payment resource is successfully created and ledgered in the database. Returning 201 preserves uniform API contracts with all other decline scenarios (e.g. acquirer card decline), allows merchants to query the declined payment by ID (`GET /payments/{id}`), and guarantees transparent idempotency replays via `Idempotency-Key` without breaking client SDK exception handling.

## Consequences
- **Positive:**
  - Upfront protection against card-testing bots, high-risk fraud rings, and suspicious transaction spikes.
  - Reduced merchant interchange fees and dispute penalties by rejecting confirmed fraud prior to acquirer dispatch.
  - Strict time budget (<50ms) guarantees sub-second payment endpoint latency.
  - Zero-data-loss and fail-open guarantees preserved.
- **Negative:**
  - Adds a small processing overhead (~5-20 ms) to the authorization pipeline.
  - Requires maintaining risk threshold configurations and rule sets.

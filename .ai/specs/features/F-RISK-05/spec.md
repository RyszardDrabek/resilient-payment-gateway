---
id: F-RISK-05
topic: RISK
phase: pre-settlement-inline-risk-gating
status: done
---

# F-RISK-05: Pipeline integration in AuthorizePaymentCommandHandler & operator audit trail

- **Topic:** [RISK](../../topics/risk.md)
- **Type:** Explicit
- **Value class:** RESILIENCE
- **Source:** ADR-025

## Description

Integrates the inline risk evaluation hook into `AuthorizePaymentCommandHandler`. When a transaction is blocked, the handler immediately sets payment state to `Declined` with `DeclineReason = "FRAUD_BLOCKED: {reason}"`, skips the acquirer settlement call, records a `RiskFlag` in PostgreSQL for the compliance team in the Operator Portal, and emits `PaymentMetrics.RecordDecline("FRAUD_BLOCKED", channel)`.

## Business context

Ensures operators have full visibility into prevented fraud attacks while keeping payment processing idempotent and compliant with RFC 7807 problem contracts.

## Acceptance criteria

- AC-1 `AuthorizePaymentCommandHandler` SHALL invoke `IInlineRiskEvaluator.EvaluateAsync` prior to settlement.
- AC-2 UPON receiving `Block`, the handler SHALL decline the payment without invoking `ISettlementPort.AuthorizeAsync`.
- AC-3 UPON upfront block, the system SHALL persist an open `RiskFlag` associated with the payment for operator review.
- AC-4 UPON upfront block, metrics counters SHALL record the decline under reason `FRAUD_BLOCKED`.
- AC-5 RETRYING a blocked payment with the same `Idempotency-Key` SHALL return the cached declined payment response idempotently.

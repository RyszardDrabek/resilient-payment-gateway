---
id: F-RISK-03
topic: RISK
phase: pre-settlement-inline-risk-gating
status: done
---

# F-RISK-03: Ultra-fast inline pre-settlement fraud evaluation engine & rule scoring

- **Topic:** [RISK](../../topics/risk.md)
- **Type:** Explicit
- **Value class:** SECURITY
- **Source:** ADR-025

## Description

Introduces the `IInlineRiskEvaluator` port and `InlineRiskEvaluator` implementation in the risk engine. The evaluator computes real-time risk scores (0-100) on incoming payment authorization requests before they reach the payment acquirer. It evaluates blacklisted party patterns, velocity checks, and transaction amount anomaly ratios against historical context with a strict 50 ms time budget.

## Business context

Prevents card-testing fraud attacks, fraudulent chargeback liabilities, and wasted acquirer interchange fees by evaluating risk prior to payment settlement.

## Acceptance criteria

- AC-1 THE `IInlineRiskEvaluator` SHALL evaluate incoming payment parameters (`partyId`, `amount`, `currency`, `channel`, `paymentMethodId`) within a configurable time budget (default 50 ms).
- AC-2 KNOWN high-risk / fraudulent patterns (e.g. party identifiers matching fraud prefixes) SHALL receive an immediate critical risk score of 90+.
- AC-3 EXCESSIVE velocity (e.g. > 10 requests per minute for the same party) SHALL be identified and scored as high risk (85+).
- AC-4 IF evaluation exceeds the time budget or throws an exception, IT SHALL fail open, returning `IsDegraded = true` and `Decision = Allow` so legitimate customer checkouts are never blocked.

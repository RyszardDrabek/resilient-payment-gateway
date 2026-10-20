---
id: F-RISK-04
topic: RISK
phase: pre-settlement-inline-risk-gating
status: done
---

# F-RISK-04: Fraud gating decisions: Allow, Challenge3Ds, and Upfront Block

- **Topic:** [RISK](../../topics/risk.md)
- **Type:** Explicit
- **Value class:** SECURITY
- **Source:** ADR-025

## Description

Maps risk evaluation scores into explicit, actionable gating decisions: `Allow` for normal low-risk payments, `Challenge3Ds` for elevated-risk transactions requiring mandatory step-up 3DS2 SCA authentication, and `Block` for confirmed high-risk fraud attempts that must be rejected immediately without calling the acquirer.

## Business context

Shifts liability away from the merchant on elevated-risk payments and prevents chargebacks entirely on blocked payments, saving unnecessary transaction fees.

## Acceptance criteria

- AC-1 RISK scores below the challenge threshold (< 60) SHALL produce an `Allow` decision for frictionless authorization.
- AC-2 RISK scores between 60 and 84 SHALL produce a `Challenge3Ds` decision, enforcing 3DS2 Strong Customer Authentication.
- AC-3 RISK scores of 85 and above SHALL produce a `Block` decision, terminating the authorization before contacting the acquirer.
- AC-4 EVERY `Block` decision SHALL specify a descriptive human-readable audit reason (e.g. `Fraudulent party pattern detected` or `Velocity limit exceeded`).

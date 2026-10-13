---
id: intake-03
date: 2026-10-12
kind: extension
scoped-docs:
  - .ai/sources/prd-merchant-and-operator-portal.md
supersedes: []
---

# Intake 03 — Merchant & Operator Portal

## Summary

Adds Phase 6 (`merchant-and-operator-portal`) to introduce an interactive, responsive web portal for merchants and operators. Provides operator transaction observability and unresolved payment investigations, merchant self-service webhook management and delivery simulation, and a compliance console for reviewing and dispositioning AML risk flags.

## Overview delta

- § Scope: Extend gateway scope to include interactive web user interfaces (Single Page Application / Web Dashboard) tailored for `merchant`, `ops`, and `risk` roles.
- § Success criteria: Operators can monitor gateway metrics and filter unresolved payments across channels; compliance officers can review and disposition AML risk flags directly from the console; merchants can register, rotate secrets, and simulate test delivery for their outbound webhooks.
- § Out of scope: Retain PCI certification, multi-region HA, dynamic FX/quoting, customer-facing wallet connect, and pre-settlement risk gating as out of scope.

## Phase deltas

| Phase | Decision | Why |
| ----- | -------- | --- |
| merchant-and-operator-portal | new | Delivers an interactive, unified UI portal covering operator visibility, merchant webhook management, and compliance operations. |

## Topic deltas

| Topic | Decision | Phase | Why |
| ----- | -------- | ----- | --- |
| PORTAL | new | merchant-and-operator-portal | Web application layer providing operator dashboard, merchant webhook self-service, and compliance risk disposition consoles. |

## Feature candidates

| Candidate name | Target topic | Target phase | Notes |
| -------------- | ------------ | ------------ | ----- |
| Operator dashboard & unresolved payments console | PORTAL | merchant-and-operator-portal | KPI overview, unresolved payments table with age and channel filtering, and detailed transaction inspection. |
| Merchant webhook self-service & event simulator | PORTAL | merchant-and-operator-portal | Endpoint URL management, HMAC secret rotation, active toggle, and test event dispatch simulator with signature inspection. |
| Compliance risk flags review & disposition console | PORTAL | merchant-and-operator-portal | Alerts queue for vector/AML risk flags, risk score breakdown, and disposition actions (`Approved`, `Escalated`, `Rejected`). |

## Open questions

- Technology stack for UI: Single Page Application (vanilla HTML/CSS/JS or modern lightweight framework e.g. React/Vite/Next.js) hosted standalone or served as static assets from API Gateway.
- Authentication strategy for portal: JWT Bearer token acquisition (interactive login dialog / token input for role simulation).

## Review notes

- Architect review: [OK] Portal as Phase 6 builds cleanly on top of Phase 1-5 backend APIs (`GET /api/v1/payments/unresolved`, `MerchantWebhookSubscriptions`, `RiskReviewQueue`). Decoupled frontend surfaces preserve modular monolith and CQRS boundaries.
- Requirements review: [OK] Closes the previously out-of-scope operator/merchant UI gaps identified across F-EDGE-04, F-EDGE-05, and F-RISK-02. Delivers high business value and operator productivity.

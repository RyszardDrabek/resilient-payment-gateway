---
id: F-PORTAL-03
topic: PORTAL
phase: merchant-and-operator-portal
status: done
---

# F-PORTAL-03: Compliance risk flags review & disposition console

- **Topic:** [PORTAL](../../topics/portal.md)
- **Type:** Explicit
- **Value class:** USER-VISIBLE
- **Source:** .ai/sources/prd-merchant-and-operator-portal.md (§Moduł C), F-RISK-01, F-RISK-02

## Description

Equips compliance and risk officers with an operational review interface to inspect AML/fraud anomaly flags produced by the AI risk scoring engine and submit disposition actions.

## Business context

Fulfills regulatory compliance obligations by enabling rapid human-in-the-loop triage, audit trail preservation, and disposition of flagged suspicious activities.

## Acceptance criteria

- AC-1 THE system SHALL present a prioritized review queue of open risk flags showing transaction details, risk score, and detected anomaly factors.
- AC-2 WHEN a compliance officer inspects a flagged payment, THE system SHALL display the historical user risk context and anomaly reasoning.
- AC-3 WHEN a compliance officer submits a disposition decision (`Approved`, `Escalated`, `Rejected`), THE system SHALL record the decision with notes and remove the item from the open review queue.
- AC-4 IF an unprivileged caller attempts to view or submit flag dispositions, THEN THE system SHALL deny access.

## Interface contract

- **Resource / surface:** Compliance review console consuming `/api/v1/risk/flags` and disposition endpoints
- **Operation:** read queue and record disposition action
- **Response shape category:** single resource
- **Terminal status classes:** success | auth-fail | validation-fail | server-error
- **Load-bearing fields:** `flagId`, `paymentId`, `partyId`, `riskScore`, `reason`, `disposition`, `dispositionNotes`

## Visual design

- Layout: Master-detail view — left pane renders sortable list of flagged transactions ordered by risk score descending; right pane displays comprehensive case file, anomaly factors, and disposition action form.
- States: Severity badges (`High` in crimson, `Medium` in amber, `Low` in slate); active submission spinner on action button; empty state card when zero flags require review.
- Semantics: Confirmation modal requiring optional analyst notes before submitting terminal disposition.
- Responsive: Switches to single-column tabbed navigation on narrower viewports.

## Out of scope

- External case-management ticketing synchronization (e.g. Jira/ServiceNow).
- Automated external regulatory report filing (SAR/STR).

## Dependencies

F-RISK-01, F-RISK-02

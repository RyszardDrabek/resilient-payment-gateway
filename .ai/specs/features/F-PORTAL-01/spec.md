---
id: F-PORTAL-01
topic: PORTAL
phase: merchant-and-operator-portal
status: done
---

# F-PORTAL-01: Operator dashboard & unresolved payments console

- **Topic:** [PORTAL](../../topics/portal.md)
- **Type:** Explicit
- **Value class:** USER-VISIBLE
- **Source:** .ai/sources/prd-merchant-and-operator-portal.md (§Moduł A), F-EDGE-04

## Description

Presents an interactive web dashboard for gateway operators displaying high-level transaction volume metrics and an operational query console for investigating unresolved payments across channels.

## Business context

Enables payment operations and on-call engineers to monitor channel health, detect stuck transactions, and isolate discrepancies without raw database access.

## Acceptance criteria

- AC-1 THE system SHALL present a summary metrics dashboard showing aggregate transaction counts, status breakdowns, and channel distributions.
- AC-2 WHEN an operator filters unresolved payments by settlement channel or age threshold, THE system SHALL display matching unresolved transactions with their current state, elapsed duration, and channel reference.
- AC-3 WHEN an operator selects an unresolved payment record, THE system SHALL display the detailed transaction timeline and metadata.
- AC-4 IF an unauthorized caller or client lacking operator role attempts to access the operator console, THEN THE system SHALL deny access and redirect to authentication.

## Interface contract

- **Resource / surface:** Web dashboard consuming `/api/v1/payments/unresolved` and gateway summary APIs
- **Operation:** read-only metrics and paginated filtered query
- **Response shape category:** paginated list
- **Terminal status classes:** success | auth-fail | server-error
- **Load-bearing fields:** `paymentId`, `state`, `settlementChannel`, `channelReference`, `createdAt`, `durationElapsedMinutes`

## Visual design

- Layout: Top navigation header with role badge and environment indicator; KPI summary cards row at top; main body holds paginated data table with search and filter toolbar.
- States: Skeleton loader during asynchronous data retrieval; empty state illustration when zero unresolved records match filters; dismissible error alert with retry button on network failure.
- Semantics: Badges for payment state (`Pending` in warning amber, `Unknown` in muted grey); channel tags for `ADYEN` and `WEB3`.
- Responsive: KPI metric cards reflow on mobile/tablet; data table supports responsive horizontal scrolling with pinned identifiers.

## Out of scope

- Direct database mutation or manual acquirer force-settlement execution from the UI.
- Exporting raw database dumps or CSV bulk downloads.

## Dependencies

F-EDGE-04, F-EDGE-01

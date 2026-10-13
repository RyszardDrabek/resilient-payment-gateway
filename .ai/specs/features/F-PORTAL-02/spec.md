---
id: F-PORTAL-02
topic: PORTAL
phase: merchant-and-operator-portal
status: done
---

# F-PORTAL-02: Merchant webhook self-service & event simulator

- **Topic:** [PORTAL](../../topics/portal.md)
- **Type:** Explicit
- **Value class:** USER-VISIBLE
- **Source:** .ai/sources/prd-merchant-and-operator-portal.md (§Moduł B), F-EDGE-05

## Description

Equips merchants with a self-service management interface to configure notification endpoints, rotate HMAC-SHA256 secret keys, and test receiver connectivity using an interactive event simulator.

## Business context

Reduces developer integration onboarding time and support inquiries by allowing merchants to test and verify webhook signature handling before going live.

## Acceptance criteria

- AC-1 THE system SHALL allow authenticated merchants to view and update their configured webhook endpoint URL and active delivery status.
- AC-2 WHEN a merchant requests secret key rotation, THE system SHALL generate a new cryptographically secure secret key and display it with confirmation.
- AC-3 WHEN a merchant triggers a test event dispatch, THE system SHALL deliver a simulated signed payload to the configured endpoint and display the resulting HTTP status code and response latency.
- AC-4 IF the test endpoint fails or times out, THEN THE system SHALL display diagnostic failure details and the computed HMAC signature for verification.

## Interface contract

- **Resource / surface:** Webhook management console consuming webhook subscription endpoints and simulator dispatcher
- **Operation:** read/update subscription and invoke test dispatch
- **Response shape category:** single resource
- **Terminal status classes:** success | validation-fail | auth-fail | server-error
- **Load-bearing fields:** `partyId`, `endpointUrl`, `secretKey`, `isActive`, `X-Signature-SHA256`, `X-Event-Id`, `X-Timestamp`

## Visual design

- Layout: Split-panel view — left/top panel contains webhook endpoint configuration form and secret rotation card; right/bottom panel hosts interactive webhook simulator.
- States: Active/Inactive toggle with distinct colored status badge; test dispatch button displays loading state while dispatching; test result container renders HTTP status badge (green 200 OK, red 5xx) with expandable request/response headers.
- Semantics: Secret key displayed with copy-to-clipboard action and masked view by default; warning modal before rotating secret key.
- Responsive: Stacks vertically on smaller screens; simulator payload code block supports syntax highlighting and scrolling.

## Out of scope

- Historical webhook dispatch logs archive beyond current session test dispatches.
- Custom retry backoff policy tuning per merchant.

## Dependencies

F-EDGE-05

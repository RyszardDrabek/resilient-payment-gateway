# ADR-014: Design System

## Status

Accepted — 2026-10-13

> _This ADR is **mutable** — design-system is the project's living brand book and is the documented exception to the standard "ADRs are immutable" rule. Every change is recorded in the Revision history table at the bottom._

## Context

Phase 6 introduces the web portal (`PORTAL`), delivering operational consoles for payment operations, merchant webhook self-service, and compliance risk officers:
- `F-PORTAL-01`: Operator dashboard & unresolved payments console
- `F-PORTAL-02`: Merchant webhook subscriptions & payload simulator
- `F-PORTAL-03`: Compliance risk flags review & disposition console

Financial operations consoles require high information density, rapid scannability, zero ambiguity for critical transaction states (e.g., `Pending`, `Unknown`, `Settled`, `Flagged`), and reduced visual fatigue during extended monitoring shifts. We adopt a tailored dark-mode design system with clear semantic contrast, standardized typography, and robust component primitives.

## Decision

The project adopts the brand book, layout grammar, and component inventory below. The sibling folder `adr-014-design-system/` provides a browser-renderable token and primitive reference; the tokens declared in its `styles.css` mirror the tables in this ADR 1:1.

**Reference vs. source-of-truth contract — coding agents read this:**
- The ADR token tables (palette, typography, spacing, radius/elevation/motion) are the **only** authoritative source.
- The HTML reference folder exists for **human visual review** of tokens and primitives, not for code reuse.
- Coding agents MUST source tokens via the project's design-token mechanism — Tailwind CSS utility classes and custom CSS variables configured in `src/PaymentGateway.Portal`. Never `@import` or copy from the reference `styles.css` at runtime; never copy DOM from `tokens.html` or `components.html` into application source.
- Worked screen examples in this ADR are **ASCII-only**. Real screen implementations live in feature code.
- The project's chosen UI framework (React 19 + Tailwind CSS) is the structural source of truth for component implementation.

---

## Brand book

### Palette

| Token | Hex | Role | Contrast vs. `bg` |
| --- | --- | --- | --- |
| `bg` | `#0b0f19` | Main application background (midnight slate) | — |
| `surface` | `#111827` | Card, table container, panel background | 1.15:1 |
| `surface-muted` | `#1f2937` | Table row hover, input background, code block bg | 1.45:1 |
| `border` | `#374151` | Subtle divider lines, table borders, card frames | 2.1:1 |
| `text` | `#f9fafb` | Primary headings, body copy, key data values | 17.2:1 (AAA) |
| `text-muted` | `#9ca3af` | Secondary labels, timestamps, metadata, captions | 7.3:1 (AAA) |
| `primary` | `#2563eb` | Primary call to action, active navigation item | 4.8:1 (AA) |
| `primary-contrast`| `#ffffff` | Text and icons on top of `primary` buttons | 4.6:1 (AA) |
| `success` | `#10b981` | Settled status, successful webhooks, cleared risk | 7.9:1 (AAA) |
| `warning` | `#f59e0b` | Unresolved status, pending sync, escalated alert | 9.8:1 (AAA) |
| `destructive` | `#ef4444` | Payment failed, risk blocked, dead letter webhook | 5.2:1 (AA) |
| `accent` | `#8b5cf6` | Simulated events, payload inspector, crypto/web3 | 5.8:1 (AA) |

### Typography

- **Font family:** `Inter, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif`
- **Monospace family:** `"JetBrains Mono", ui-monospace, SFMono-Regular, Menlo, monospace`
- **Weights used:** 400 (Regular), 500 (Medium), 600 (Semi-bold), 700 (Bold)
- **Base size:** 14px (optimized for dense operational tables)
- **Scale:**

| Step | Size | Line-height | Used for |
| --- | --- | --- | --- |
| `xs` | 11px | 16px | Timestamps, table tags, micro-badges |
| `sm` | 13px | 18px | Table cells, helper text, form labels |
| `md` | 14px | 20px | Standard body copy, inputs, button labels |
| `lg` | 16px | 24px | Section headers, card titles, modal headings |
| `xl` | 20px | 28px | Page titles, key operational metric values |
| `2xl` | 24px | 32px | High-level summary totals, dashboard stats |
| `3xl` | 30px | 36px | Main dashboard hero figures |

ASCII type ladder:

```
[3xl]  $1,248,590.00 SETTLED TODAY
[2xl]  Operational Dashboard
[xl]   Unresolved Payments Console
[lg]   Payment Details & Timeline
[md]   Standard data row text and inputs
[sm]   Secondary table data and field descriptions
[xs]   UTC Timestamps and cryptographic hashes
```

### Spacing

Base unit: 4px. Scale:

| Token | Px | Typical use |
| --- | --- | --- |
| `xs` | 4px | Icon-to-text spacing, compact badge padding |
| `sm` | 8px | Button vertical padding, input element gaps |
| `md` | 12px | Standard form control padding, table cell padding |
| `lg` | 16px | Card content padding, toolbar spacing |
| `xl` | 24px | Section separation, dashboard widget grid gap |
| `2xl` | 32px | Page header to content gutter |
| `3xl` | 48px | Outer viewport bounds |

### Radius / elevation / motion

| Token | Value | Used for |
| --- | --- | --- |
| `radius-sm` | 4px | Inputs, dropdown selects, badge tags |
| `radius-md` | 8px | Action buttons, dashboard metric cards |
| `radius-lg` | 12px | Modals, drawer sidebars, table containers |
| `radius-full` | 9999px | Status pill indicators, role avatar badge |
| `shadow-sm` | `0 1px 2px 0 rgba(0, 0, 0, 0.4)` | Table rows, input focus state |
| `shadow-md` | `0 4px 6px -1px rgba(0, 0, 0, 0.5)` | Hovered KPI cards, dropdown menus |
| `shadow-lg` | `0 10px 15px -3px rgba(0, 0, 0, 0.7)` | Modal overlays, dialog windows |
| `motion-fast` | 150ms ease-out | Button hover, badge transition |
| `motion-base` | 250ms ease-out | Modal backdrop fade, drawer slide |

---

## Layout grammar

### App shell

```
+-------------------------------------------------------------------------------------------------+
| [primary] GATEWAY PORTAL  |  Env: DEV  | Role: [ Ops Officer v ]        | User: ops@gateway.local |  <- Top Bar (56px)
+-------------------------------------------------------------------------------------------------+
| [surface] NAVIGATION      | [bg] CONTENT AREA (max-width: 1440px)                               |
|                           |                                                                     |
| > Dashboard               | +-----------------------------------------------------------------+ |
| > Unresolved (F-PORTAL-01)| | [lg] Unresolved Payments (Cutoff: 15m)          [ Refresh ]      | |
| > Webhooks   (F-PORTAL-02)| +-----------------------------------------------------------------+ |
| > Risk Review(F-PORTAL-03)| | [surface] Metric Card 1 | Metric Card 2 | Metric Card 3         | |
|                           | +-----------------------------------------------------------------+ |
|                           | | [surface] Data Table with Filters & Pagination                  | |
|                           | |                                                                 | |
|                           | +-----------------------------------------------------------------+ |
+-------------------------------------------------------------------------------------------------+
```

- Top Bar: 56px sticky header, dark `[surface]` background, 1px `[border]` bottom border, contains the dev Role Switcher and environment badge.
- Sidebar: 240px wide navigation menu on desktop, collapsable on smaller screens.
- Content Area: Dark `[bg]` backdrop, responsive max 1440px width with `[xl]` padding.

---

## Component anatomy

### Button

```
[primary]                 [secondary]               [destructive]
+-------------------+     +-------------------+     +-------------------+
|  Sync Payment     |     |  Cancel           |     |  Block Merchant   |
+-------------------+     +-------------------+     +-------------------+
```

- States: `default`, `hover` (elevated background brightness), `focus` (2px `[primary]` outline), `disabled` (opacity 40%, cursor not-allowed), `loading` (spinner with label).

### Status Badge / Chip

```
[success]                 [warning]                 [destructive]             [accent]
+-------------+           +-------------+           +-------------+           +-------------+
| * Settled   |           | ! Pending   |           | x Rejected  |           | ~ Simulated |
+-------------+           +-------------+           +-------------+           +-------------+
```

### KPI Metric Card

```
+---------------------------------------------------+
| [text-muted] UNRESOLVED TRANSACTIONS              |
| [3xl] 24                                          |
| [warning] +3 in last 15 min    [ View Details -> ] |
+---------------------------------------------------+
```

---

## Worked screens

### Screen 1 — Operator Unresolved Payments Console (source: F-PORTAL-01)

```
+-------------------------------------------------------------------------------------------------+
| GATEWAY PORTAL            | Role: [ Ops Officer v ]                     | ops@gateway.local     |
+-------------------------------------------------------------------------------------------------+
| Navigation   | Unresolved Payments Console                                                      |
|              |                                                                                  |
| Dashboard    | +---------------------+ +---------------------+ +---------------------+          |
| Unresolved * | | Pending Cutoff: 15m | | Oldest Unresolved   | | Manual Resync Queue |          |
| Webhooks     | | [xl] 14             | | [xl] 42m ago        | | [xl] 3 In-flight    |          |
| Risk Review  | +---------------------+ +---------------------+ +---------------------+          |
|              |                                                                                  |
|              | Filters: Status: [ All v ]   Merchant: [ All Merchants v ]   [ Trigger Sync ]   |
|              |                                                                                  |
|              | +------------------------------------------------------------------------------+ |
|              | | Payment ID   | Merchant | Amount    | Created At   | Status    | Action      | |
|              | +------------------------------------------------------------------------------+ |
|              | | pay_8a91f    | merch_01 | EUR 49.00 | 18m ago      | [Pending] | [ Re-query] | |
|              | | pay_9c12b    | merch_02 | USD 12.50 | 25m ago      | [Unknown] | [ Re-query] | |
|              | | pay_3d74a    | merch_01 | EUR 99.00 | 41m ago      | [Unknown] | [ Force Res]| |
|              | +------------------------------------------------------------------------------+ |
|              | Showing 1 - 3 of 14 unresolved payments                        Page: < 1 >       |
+-------------------------------------------------------------------------------------------------+
```

### Screen 2 — Merchant Webhooks & Event Simulator (source: F-PORTAL-02)

```
+-------------------------------------------------------------------------------------------------+
| GATEWAY PORTAL            | Role: [ Merchant (merch_01) v ]             | dev@acme-store.com    |
+-------------------------------------------------------------------------------------------------+
| Navigation   | Merchant Webhook Subscriptions & Simulator                                       |
|              |                                                                                  |
| Dashboard    | Subscribed Endpoint: https://api.acme-store.com/webhooks/payments                |
| Unresolved   | HMAC Secret: whsec_**************************** [ Reveal ] [ Regenerate ]       |
| Webhooks *   | Active Events: payment.settled, payment.failed, payment.disputed                 |
| Risk Review  |                                                                                  |
|              | +------------------------------------------------------------------------------+ |
|              | | Dispatch Simulator                                                           | |
|              | | Select Event Type: [ payment.settled v ]                                     | |
|              | | Sample Payload:                                                              | |
|              | | +--------------------------------------------------------------------------+ | |
|              | | | { "eventType": "payment.settled", "paymentId": "pay_test_99", ... }      | | |
|              | | +--------------------------------------------------------------------------+ | |
|              | | [ Send Test Webhook ] -> HTTP 200 OK (Latency: 142ms, Signature Verified)   | |
|              | +------------------------------------------------------------------------------+ |
+-------------------------------------------------------------------------------------------------+
```

### Screen 3 — Compliance Risk Flags & Disposition Modal (source: F-PORTAL-03)

```
+-------------------------------------------------------------------------------------------------+
| GATEWAY PORTAL            | Role: [ Risk Officer v ]                    | compliance@gateway.loc|
+-------------------------------------------------------------------------------------------------+
| Navigation   | Risk Review & Disposition Console                                                |
|              |                                                                                  |
| Dashboard    | +------------------------------------------------------------------------------+ |
| Unresolved   | | Flagged Tx | Score | Velocity Score | ML Anomaly | Status     | Action       | |
| Webhooks     | +------------------------------------------------------------------------------+ |
| Risk Review* | | pay_f4102  | 92/100| High (5tx/10s) | High (94%) | [Flagged]  | [Review]     | |
|              | | pay_e8910  | 81/100| Normal         | High (81%) | [Flagged]  | [Review]     | |
|              | +------------------------------------------------------------------------------+ |
|              |                                                                                  |
|              | +-- Modal: Review Disposition (pay_f4102) -------------------------------------+ |
|              | | Amount: EUR 4,800.00 | IP: 194.26.29.13 (Tor exit node)                      | |
|              | | Reason Notes: Rapid velocity from high-risk subnet.                          | |
|              | | Disposition: ( ) Approve/Clear   (*) Reject/Block   ( ) Request Step-Up KYC  | |
|              | | Auditor Comment: [ Suspicious automated card testing script detected.     ]  | |
|              | |                                            [ Cancel ]  [ Confirm Disposition]| |
|              | +------------------------------------------------------------------------------+ |
+-------------------------------------------------------------------------------------------------+
```

---

## HTML reference

Browser-renderable token and primitive preview at `.ai/docs/adr/adr-014-design-system/`:
- `index.html` — landing page with REFERENCE-ONLY warning banner.
- `tokens.html` — palette color swatches, typography scale, spacing bars, radius, elevation.
- `components.html` — interactive button states, badges, metric cards, inputs, tables, alerts.
- `styles.css` — token-bearing CSS variables mirroring the ADR tables 1:1.

## Consequences

- (+) **Unified Aesthetic:** Clean dark-mode interface built specifically for financial transaction oversight.
- (+) **WCAG AA/AAA Compliant:** Strict contrast rules ensure clarity for critical financial states.
- (+) **Tailwind Parity:** All design tokens map directly to Tailwind CSS configuration in `PaymentGateway.Portal`.
- (-) **Living Document Maintenance:** Any palette or spacing shift requires updating both this ADR and `styles.css`.

## Mitigations

- Sibling HTML reference verifies token definitions statically without JS dependencies.
- Changes are tracked via the Revision history table below.

## Alternatives Considered

- Light theme default: Rejected due to eye strain during continuous operations monitoring.
- External UI framework (Chakra / Material UI): Rejected to keep bundle size minimal and maximize Tailwind utility control.

## Revision history

| Date | Change | Author |
| --- | --- | --- |
| 2026-10-13 | Initial brand book — palette, typography, layout, components, screens. | RDF Architecture |

# F-PORTAL-03: Technical Design — Compliance Risk Flags Review & Disposition Console

## Overview

`F-PORTAL-03` delivers the compliance and fraud operations console within the Resilient Payment Gateway portal. It equips compliance officers and risk analysts with a prioritized review queue of open AML/fraud anomaly flags, deep-dive case inspection (anomaly factors, transaction metadata, risk score context), and a disposition workflow (`confirmed`, `false_positive`, `escalated`) with required audit notes.

---

## Architectural Mapping

- **ADR-004:** JWT Bearer authentication; enforces `role: risk` or `role: ops`. Denies unprivileged roles (`merchant`) with `403 Forbidden` per AC-4.
- **ADR-007:** Adheres to EDGE HTTP problem details (`application/problem+json`).
- **ADR-009:** Consumes pgvector AI anomaly scoring outputs (`RiskFlag`, `RiskVerdict`).
- **ADR-013:** Integrated into the React 19 + TypeScript SPA in `src/PaymentGateway.Portal` consuming gateway REST endpoints.
- **ADR-014:** Implements the dark-mode financial design system: master-detail split layout, severity badges (crimson for High, amber for Medium, slate for Low), monospace identifiers, and accessible modal dialogs.
- **F-RISK-01 / F-RISK-02:** Bridges the existing backend risk flagging and disposition CQRS commands into the operational web surface.

---

## Backend Design (`PaymentGateway.Api`)

### 1. Public API Surface & Authorization

Ensure risk endpoints are exposed under `/api/v1/risk` (with `/risk` alias for backwards compatibility) and secured with `RiskPolicy`:

1. **Authorization Policy `RiskPolicy`:**
   - Requires authenticated user.
   - Enforces claim `role: risk` or `role: ops`.
   - Rejects unprivileged callers (`merchant`) with `403 Forbidden`.

2. **`GET /api/v1/risk/flags`**
   - Query Parameters: `page` (default 1), `pageSize` (default 50).
   - Dispatches `ListOpenRiskFlagsQuery`.
   - Returns `OpenRiskFlagsResponse` containing only open, undispositioned flags.

3. **`GET /api/v1/risk/verdicts/{id}`**
   - Identifier matches verdict ID, flag ID, or payment ID.
   - Dispatches `GetRiskVerdictQuery`.
   - Returns `RiskVerdictDto` or `404 Not Found`.

4. **`POST /api/v1/risk/flags/{id}/disposition`**
   - Request Body:
     ```json
     {
       "disposition": "confirmed", // or "false_positive", "escalated"
       "notes": "Verified fraudulent transaction with issuer alert."
     }
     ```
   - Dispatches `DispositionRiskFlagCommand`.
   - Returns `200 OK` with updated `RiskFlagDto`, `404 Not Found`, or `409 Conflict` (if already dispositioned).

---

## Frontend Design (`src/PaymentGateway.Portal`)

### 1. Component Hierarchy

```
src/PaymentGateway.Portal/src/components/risk/
├── RiskConsole.tsx               # Master container: role guard, queue state, layout
├── RiskMetricCards.tsx           # KPI tiles: Total Open Flags, High Severity, Oldest In Queue
├── RiskFlagList.tsx              # Left/Master pane: sortable flag cards, search, pagination
├── RiskFlagDetailCard.tsx        # Right/Detail pane: transaction details, reason, verdict, action buttons
└── DispositionModal.tsx          # Accessible modal: confirmation prompt & analyst notes input
```

### 2. User Interactions & States

- **Role Guard:** If active user is `merchant`, displays a prominent banner: "Compliance Access Restricted: This console is reserved for Risk Officers and Compliance Auditors" with an action to switch role to `Risk`.
- **Master-Detail Flow:**
  - Left pane displays list of open flags with severity badges (`High`, `Medium`, `Low`), payment ID, party ID, and elapsed time since raised.
  - Clicking a flag selects it and loads comprehensive case details in the right pane:
    - Transaction metadata (amount, currency, channel reference, timestamp).
    - Anomaly reasoning & score breakdown.
    - User/party context.
- **Disposition Workflow:**
  - Three distinct action buttons:
    - **Confirm Fraud** (Red button -> `confirmed`)
    - **Mark False Positive** (Green button -> `false_positive`)
    - **Escalate Case** (Amber button -> `escalated`)
  - Clicking any action opens `DispositionModal` displaying confirmation warning and an optional/required review notes textarea.
  - Submitting disposition sends `POST /api/v1/risk/flags/{id}/disposition`, shows success feedback, and immediately removes the item from the open queue (AC-3).
- **Zero State:** Renders `EmptyState` ("All flags reviewed — Queue is clear") when all items have been dispositioned.

---

## Verification & Testing Strategy

1. **Backend Integration Tests (`tests/PaymentGateway.IntegrationTests/RiskEndpointsIntegrationTests.cs`):**
   - 401 Unauthorized when token missing.
   - 403 Forbidden when caller role is `merchant` (AC-4).
   - 200 OK when caller role is `risk` or `ops`.
   - End-to-end verification of `/api/v1/risk/flags` and `/api/v1/risk/flags/{id}/disposition`.
2. **Frontend Vitest Tests (`src/PaymentGateway.Portal/src/services/apiClient.test.ts`):**
   - Tests for `fetchOpenRiskFlags`, `fetchRiskVerdict`, and `submitRiskDisposition`.
3. **Build Gates:**
   - Strict TypeScript compilation (`npm run build`).
   - Clean formatting (`dotnet format --verify-no-changes`).
   - Full test suite green (`dotnet test`).

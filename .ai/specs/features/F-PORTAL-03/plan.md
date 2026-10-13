# F-PORTAL-03: Implementation Plan — Compliance Risk Flags Review & Disposition Console

## Overview

Step-by-step TDD plan to implement the Compliance & Risk Flags Review and Disposition Console across `PaymentGateway.Api` (authorization policy and route aliases) and `PaymentGateway.Portal` (React 19 master-detail console).

---

## Task Steps

### Step 1: Backend API Authorization & Route Aliases
1. Update `Program.cs` in `PaymentGateway.Api`:
   - Register authorization policy `RiskPolicy` (`role: risk` or `role: ops`).
2. Update `RiskEndpoints.cs` in `PaymentGateway.Api`:
   - Apply `RiskPolicy` to secure all risk operations.
   - Map routes under `/api/v1/risk` and `/risk` for flags, verdicts, and dispositions.
3. Create `RiskEndpointsIntegrationTests.cs` in `PaymentGateway.IntegrationTests`:
   - Verify unauthenticated requests return `401 Unauthorized`.
   - Verify unprivileged roles (`merchant`) return `403 Forbidden` (AC-4).
   - Verify privileged roles (`risk`, `ops`) can read open flags and submit dispositions.

### Step 2: Frontend Contracts & Typed API Client (`src/PaymentGateway.Portal`)
1. Update `src/types/api.ts` with risk models:
   - `RiskFlagDto`
   - `OpenRiskFlagsResponse`
   - `RiskVerdictDto`
   - `DispositionRiskFlagRequest`
2. Update `src/services/apiClient.ts`:
   - `fetchOpenRiskFlags(token, page, pageSize)`
   - `fetchRiskVerdict(token, identifier)`
   - `submitRiskDisposition(token, flagId, data)`
3. Add Vitest unit tests in `src/services/apiClient.test.ts`.

### Step 3: Frontend UI Components (`src/PaymentGateway.Portal`)
1. Implement `src/components/risk/RiskMetricCards.tsx`:
   - Display total open flags, high-severity anomalies, and oldest item age.
2. Implement `src/components/risk/RiskFlagList.tsx`:
   - Left-pane list displaying open flags with severity badges (`High`, `Medium`, `Low`), party, payment ID, and timestamp.
   - Search filter by payment ID or party ID.
   - Pagination controls.
3. Implement `src/components/risk/RiskFlagDetailCard.tsx`:
   - Right-pane detailed case inspector showing payment context, amount, channel, reason, and AI risk verdict.
   - Three actionable disposition buttons: "Confirm Fraud" (red), "False Positive" (green), "Escalate" (amber).
4. Implement `src/components/risk/DispositionModal.tsx`:
   - Modal prompt with required confirmation and optional/required review notes textarea.
5. Implement `src/components/risk/RiskConsole.tsx`:
   - Container component coordinating queue state, selected item, disposition submission, and optimistic list removal.
   - Role guard: displays notification and button to switch to `Risk` role if user is `merchant`.
6. Connect `RiskConsole` to `src/App.tsx` replacing the current placeholder.

### Step 4: Verification, RDF Reports & Git Push
1. Run `npm test` and `npm run build` in `src/PaymentGateway.Portal`.
2. Run `dotnet format --verify-no-changes`.
3. Run `dotnet test`.
4. Generate `coder-report.md`, `acceptance-matrix.md`, and `build-gate-report.md`.
5. Update `spec.md` status to `done`.
6. Commit on `feature/F-PORTAL-03`, push to origin, and open PR #27.

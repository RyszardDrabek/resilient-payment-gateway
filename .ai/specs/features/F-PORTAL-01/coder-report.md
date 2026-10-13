# F-PORTAL-01: Coder Report

## Summary

Implemented the Operator Dashboard & Unresolved Payments Console for Phase 6 (`PORTAL` topic).

## Changes Implemented

1. **`PaymentGateway.Api`:**
   - Configured `PortalPolicy` CORS policy in `Program.cs` and `appsettings.json` targeting `http://localhost:3000` and `http://127.0.0.1:3000`.
   - Exposed `X-Correlation-Id` header for cross-origin problem details diagnosis.
   - Added `PortalCorsIntegrationTests` verifying preflight `OPTIONS` requests from allowed and disallowed origins.

2. **`PaymentGateway.Portal`:**
   - Scaffolded React 19 + TypeScript + Vite + Tailwind CSS v4 project structure.
   - Built `tokenService.ts` utilizing standard Web Crypto API HMAC-SHA256 signing for local development JWT generation across `Ops`, `Merchant`, and `Risk` personas.
   - Built `AuthContext.tsx` providing active role state and JWT Bearer credentials.
   - Built typed `apiClient.ts` consuming `/api/v1/payments/unresolved` and `/api/v1/payments/{id}` with RFC 7807 problem details handling.
   - Built UI components adhering to ADR-014 Design System:
     - `AppShell.tsx` and `Header.tsx` with sticky top bar, DEV environment badge, and interactive role switcher dropdown.
     - `MetricCards.tsx` presenting summary KPI counts, pending/unknown status split, channel distribution, and oldest duration.
     - `FilterToolbar.tsx` supporting channel filtering (`All`, `Adyen`, `Web3`), age threshold filtering, and refresh actions.
     - `UnresolvedTable.tsx` presenting paginated financial data with state/channel badges and inspection buttons.
     - `PaymentDetailModal.tsx` displaying transaction aggregate timeline, amounts, party id, and acquirer references.
     - `LoadingSkeleton.tsx`, `EmptyState.tsx`, and `ErrorAlert.tsx` (with AC-4 role guidance).
   - Containerized via multi-stage `Dockerfile` (Node 22 build -> Nginx alpine) and `nginx.conf`.
   - Integrated `portal` service into `compose/docker-compose.yml` on port 3000.

## Test Results

- Frontend tests: 3/3 passed (`vitest`).
- Frontend production bundle: Clean build (`tsc -b && vite build`).
- Backend format: `dotnet format --verify-no-changes` passed.
- Backend tests: `dotnet test` passed 439/439 tests (0 failing).

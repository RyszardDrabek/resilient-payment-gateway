# F-PORTAL-01: Implementation Plan

## Overview

Step-by-step TDD plan to implement the Operator Dashboard & Unresolved Payments Console across `PaymentGateway.Api` (CORS) and `PaymentGateway.Portal` (React 19 + TypeScript SPA).

---

## Task Steps

### Step 1: Backend CORS Support
1. Update `appsettings.json` and `appsettings.Development.json` in `PaymentGateway.Api` to define `Cors:AllowedOrigins` (`["http://localhost:3000", "http://127.0.0.1:3000"]`).
2. Update `Program.cs` in `PaymentGateway.Api` to register `builder.Services.AddCors(...)` with `PortalPolicy` and invoke `app.UseCors("PortalPolicy")`.
3. Add an integration test in `PaymentGateway.IntegrationTests` verifying that an `OPTIONS` preflight request from `http://localhost:3000` receives `Access-Control-Allow-Origin: http://localhost:3000`.

### Step 2: Frontend Project Scaffolding
1. Scaffold `src/PaymentGateway.Portal/` directory with:
   - `package.json` (React 19, Vite, TypeScript, Tailwind CSS, Lucide icons, Vitest for testing)
   - `tsconfig.json` & `vite.config.ts`
   - `index.html` with viewport meta and title "Resilient Payment Gateway — Portal"
   - `src/index.css` configured with Tailwind CSS directives and ADR-014 color/font variables
   - `nginx.conf` and `Dockerfile` for multi-stage Docker build

### Step 3: Auth & Dev Token Service
1. Implement `src/types/auth.ts` defining user roles (`Ops`, `Merchant`, `Risk`).
2. Implement `src/services/tokenService.ts`:
   - Generates and signs valid dev JWT Bearer tokens matching the symmetric secret in `appsettings.Development.json` (`auth:dev-secret-key-12345678901234567890`) using standard HMAC-SHA256 (Web Crypto API).
3. Implement `src/context/AuthContext.tsx` providing active role, token, and switcher callback.
4. Add unit test for `tokenService.ts` verifying claim structure (`sub`, `role`, `iss`, `aud`).

### Step 4: Typed API Client & Contract
1. Implement `src/types/api.ts` defining `UnresolvedPaymentDto`, `PaginatedResult`, `PaymentDetailsDto`, `ProblemDetails`.
2. Implement `src/services/apiClient.ts`:
   - `fetchUnresolvedPayments(params)`
   - `fetchPaymentDetails(paymentId)`
   - Automatically attaches `Authorization: Bearer <token>` and `X-Correlation-Id`.
   - Normalizes errors to RFC 7807 problem details.

### Step 5: Dashboard & Unresolved Console UI Components
1. Implement `src/components/layout/AppShell.tsx`:
   - Top header with environment badge, role dropdown switcher, active user identity.
   - Left navigation rail with active route highlighting.
2. Implement `src/components/unresolved/MetricCards.tsx`:
   - Displays total unresolved count (> 15m), breakdown by channel (`ADYEN` vs `WEB3`), and oldest pending age.
3. Implement `src/components/unresolved/FilterToolbar.tsx`:
   - Channel dropdown selector (`All`, `ADYEN`, `WEB3`), age threshold input (minutes), search filter.
4. Implement `src/components/unresolved/UnresolvedTable.tsx`:
   - Renders paginated table with `paymentId`, `state` (badge), `channel` (badge), `createdAt`, `durationElapsedMinutes`.
   - Pagination controls (Previous, Next, Page indicator).
   - Clickable row triggers inspection.
5. Implement `src/components/unresolved/PaymentDetailModal.tsx`:
   - Modal drawer/dialog displaying full payment metadata, amount, currency, channel reference, and status timeline.
6. Implement error, empty, and unauthorized states:
   - Displays clear warning when 401/403 is received with an action to switch to the `Ops` role (AC-4).

### Step 6: Docker Compose Integration
1. Update `docker-compose.yml` to include the `portal` service:
   - Build context `src/PaymentGateway.Portal`
   - Port forwarding `3000:80`
   - Depends on `api`

### Step 7: Verification & Build Gate
1. Run frontend build and tests: `npm run build` and `npm test` in `src/PaymentGateway.Portal`.
2. Run backend test suite: `dotnet test`.
3. Run formatting: `dotnet format --verify-no-changes`.
4. Generate `coder-report.md`, `acceptance-matrix.md`, and `build-gate-report.md`.

# F-PORTAL-01: Technical Design

## Overview

`F-PORTAL-01` delivers the foundational frontend Single Page Application (SPA) for the Resilient Payment Gateway portal, focused on the **Operator Dashboard & Unresolved Payments Investigation Console**.

It establishes:
1. CORS configuration in `PaymentGateway.Api` allowing cross-origin requests from the portal origin (`http://localhost:3000`).
2. The React 19 + TypeScript + Vite + Tailwind CSS application structure in `src/PaymentGateway.Portal`.
3. An in-browser dev authentication and role management context supporting `Ops`, `Merchant`, and `Risk` personas with valid JWT Bearer credentials.
4. The Operator Dashboard with real-time KPI metric summaries and an interactive, filtered query console for unresolved payments consuming `GET /api/v1/payments/unresolved` and `GET /api/v1/payments/{id}`.
5. Production containerization via a multi-stage `Dockerfile` and Nginx configuration for Docker Compose.

---

## Architectural Mapping

- **ADR-004:** Adheres to JWT Bearer authentication; sends `Authorization: Bearer <token>` with `role: ops`.
- **ADR-007:** Adheres to EDGE HTTP problem details (`application/problem+json`).
- **ADR-012:** Consumes the unresolved payments query endpoint (`GET /api/v1/payments/unresolved`).
- **ADR-013:** Implements the decoupled SPA architecture, multi-origin CORS, and Docker hosting.
- **ADR-014:** Implements the dark-mode financial design system (palette, typography scale, spacing, buttons, badges, tables, modals).

---

## Component Architecture (`src/PaymentGateway.Portal`)

```
src/PaymentGateway.Portal/
├── index.html
├── package.json
├── tsconfig.json
├── vite.config.ts
├── nginx.conf
├── Dockerfile
├── src/
│   ├── main.tsx
│   ├── App.tsx
│   ├── index.css                    # Tailwind CSS + ADR-014 token definitions
│   ├── types/
│   │   ├── api.ts                   # Strongly typed contracts (Payment, Unresolved, Problem)
│   │   └── auth.ts                  # Role & auth context types
│   ├── services/
│   │   ├── apiClient.ts             # Typed Fetch client with Bearer auth, CORS, & error mapping
│   │   └── tokenService.ts          # Local Dev JWT minting for Ops/Merchant/Risk
│   ├── context/
│   │   └── AuthContext.tsx          # Current role state & JWT token provider
│   ├── components/
│   │   ├── layout/
│   │   │   ├── AppShell.tsx         # Top bar with role switcher + left navigation rail
│   │   │   └── Header.tsx           # Environment banner (DEV), user profile, role badge
│   │   ├── common/
│   │   │   ├── Badge.tsx            # Status badge (Pending, Unknown, Settled, Failed)
│   │   │   ├── LoadingSkeleton.tsx  # Table & card skeleton loaders
│   │   │   ├── ErrorAlert.tsx       # RFC 7807 error banner with retry
│   │   │   └── EmptyState.tsx       # Zero-results placeholder
│   │   └── unresolved/
│   │       ├── MetricCards.tsx      # KPI tiles: Total unresolved, Age cutoff, Channel split
│   │       ├── FilterToolbar.tsx    # Channel selector (All/ADYEN/WEB3), minAge input
│   │       ├── UnresolvedTable.tsx  # Dense data table with pagination controls
│   │       └── PaymentDetailModal.tsx # Inspection dialog showing full lifecycle timeline
```

---

## API Integration & Contracts

### 1. CORS Configuration in `PaymentGateway.Api`
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("PortalPolicy", policy =>
    {
        policy.WithOrigins(
                builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() 
                ?? ["http://localhost:3000", "http://127.0.0.1:3000"])
              .AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders("X-Correlation-Id");
    });
});
```
Registered via `app.UseCors("PortalPolicy")` prior to authentication and endpoint mapping.

### 2. Frontend Contract (`src/types/api.ts`)
```typescript
export interface UnresolvedPaymentItem {
  paymentId: string;
  merchantId: string;
  amount: number;
  currency: string;
  state: 'Pending' | 'Unknown';
  settlementChannel: 'ADYEN' | 'WEB3' | string;
  channelReference: string | null;
  createdAt: string;
  durationElapsedMinutes: number;
}

export interface PaginatedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}
```

---

## Security & Role Enforcement

1. **Role Switcher:**
   - In dev mode, allows selecting between `Ops Officer` (`role: ops`), `Merchant Admin` (`role: merchant`), and `Risk Officer` (`role: risk`).
   - Mints a signed development JWT using the shared symmetric secret from `appsettings.Development.json` (`auth:dev-secret-key-12345678901234567890`), ensuring end-to-end cryptographic validity against the API's `JwtBearerHandler`.
2. **AC-4 Denial:**
   - When calling `/api/v1/payments/unresolved` with a non-permitted role or invalid token, the API responds with `401 Unauthorized` or `403 Forbidden` (`application/problem+json`).
   - The UI intercepts this status, suppresses the data view, and displays an unauthorized banner instructing the operator to assume the `Ops` role.

---

## Test & Verification Plan

1. **Unit & Component Testing (Frontend):**
   - Verify `tokenService` generates valid, decodable JWTs with proper claims.
   - Verify `MetricCards` calculates and formats counts and channel breakdown accurately.
   - Verify `FilterToolbar` emits debounce filter changes.
   - Verify `UnresolvedTable` handles empty, loading, and populated paginated states.
2. **Backend Regression:**
   - Verify `PaymentGateway.Api` builds with CORS enabled.
   - Verify all 437 existing backend tests in `PaymentGateway.UnitTests`, `ArchitectureTests`, and `IntegrationTests` remain green.

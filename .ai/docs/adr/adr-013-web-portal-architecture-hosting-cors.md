# ADR-013: Web Portal Architecture, Docker Hosting & CORS Integration

## Status

Accepted — 2026-10-13

## Context

Phase 6 introduces the web portal topic (`PORTAL`), comprising three operational consoles:
- `F-PORTAL-01`: Operator dashboard and unresolved payments investigation console.
- `F-PORTAL-02`: Merchant webhook self-service console and payload simulation tool.
- `F-PORTAL-03`: Risk review console and compliance disposition workflow.

The core gateway is built as a .NET 10 modular monolith exposed via `PaymentGateway.Api` with JWT Bearer authentication ([ADR-004](adr-004-jwt-bearer-auth.md)) and docker-compose deployment. We need a modern, maintainable frontend architecture that connects to the existing backend APIs, operates reliably in local docker-compose environments, respects cross-origin browser security, and supports multi-role access control (`merchant`, `ops`, `risk`).

## Decision

The project adopts a decoupled Single Page Application (SPA) architecture for the portal:

1. **Frontend Stack:**
   - **Framework:** React 19 with TypeScript.
   - **Tooling:** Vite for fast bundling and hot module replacement (HMR).
   - **Directory:** Placed in `src/PaymentGateway.Portal`.
   - **State / Fetching:** TanStack Query (React Query) and typed API clients for resilient caching, polling, and mutation state.
   - **Styling:** Tailwind CSS (configured in [ADR-014](adr-014-design-system.md)).

2. **Containerization & Hosting:**
   - The portal runs as a dedicated `portal` service in `docker-compose.yml`.
   - Uses a multi-stage `Dockerfile`:
     - Stage 1: `node:22-alpine` runs `npm ci` and `npm run build`.
     - Stage 2: `nginx:alpine` serves static assets on port `3000` with SPA fallback routing (`try_files $uri $uri/ /index.html;`).
   - `PaymentGateway.Api` remains running on port `5000` / `5001`.

3. **CORS Configuration in `PaymentGateway.Api`:**
   - ASP.NET Core `AddCors` is configured with a named policy (e.g., `"PortalPolicy"`) allowing origins configured in `Cors:AllowedOrigins` (defaulting in development to `http://localhost:3000` and `http://127.0.0.1:3000`).
   - Allows standard HTTP methods (`GET`, `POST`, `PUT`, `DELETE`, `OPTIONS`) and headers (`Authorization`, `Content-Type`, `Idempotency-Key`, `X-Correlation-Id`).
   - `app.UseCors("PortalPolicy")` is registered before authentication and authorization middleware.

4. **Authentication & Role Simulation:**
   - The portal interacts with the API using Bearer tokens in the `Authorization` header.
   - For local development and operational ergonomics, the portal includes an in-app **Role Switcher & Dev Token Manager**:
     - Allows instant switching between active roles: `Merchant` (scoped to a configured merchant ID), `Ops` (system operator), and `Risk` (risk compliance officer).
     - Generates valid development JWTs matching the gateway's dev JWT signing key (`DevelopmentJwtSigner` / dev secret from `appsettings.Development.json`) or uses a local token provider endpoint.
     - Automatically attaches the active JWT to outgoing HTTP requests.

## Consequences

- (+) **Clean Decoupling:** API backend and frontend UI have separated lifecycles, dependencies, and deployment containers.
- (+) **Browser Security Parity:** Running on distinct origins (`:3000` vs `:5000`) validates real-world CORS and preflight behavior.
- (+) **Developer Experience:** Instant role toggling in the portal simplifies manual testing of permissions without requiring an external IdP (Identity Provider) during development.
- (-) **Two Toolchains:** The repository now requires both .NET 10 SDK and Node.js 22 LTS (for frontend builds).
- (-) **Docker Compose Footprint:** Adds a container to `docker-compose.yml` and increases cold build time.

## Mitigations

- Portal builds in Docker use cached npm layers in multi-stage builds.
- Frontend development can be run outside Docker (`npm run dev`) against the local API with CORS enabled.
- Dev JWT generation is constrained to development mode; production configurations require external OIDC/OAuth2 tokens.

---

## Alternatives Considered

1. **Embedded Static Files in ASP.NET Core (`UseStaticFiles` under `/portal`):**
   - *Rejected:* Coupling frontend builds with the .NET assembly would complicate frontend tooling, prevent separate scaling, and obscure cross-origin production topologies.
2. **Blazor WebAssembly / Blazor Server:**
   - *Rejected:* Slower initial bundle download times and heavier runtime footprint compared to React/Vite, with fewer off-the-shelf dashboard and charting component patterns.
3. **External Mock Identity Provider (Keycloak / Duende) in Docker:**
   - *Rejected:* Adds excessive infrastructure complexity and startup overhead for a local MVP compose setup when JWT Bearer validation already works with standard claims.

## Related ADRs

- [ADR-004: JWT Bearer Authentication](adr-004-jwt-bearer-auth.md)
- [ADR-007: EDGE HTTP Contract](adr-007-edge-http-contract.md)
- [ADR-012: Operational Hardening — Outbox, Webhooks, Query](adr-012-operational-hardening-outbox-webhooks-query.md)
- [ADR-014: Design System](adr-014-design-system.md)

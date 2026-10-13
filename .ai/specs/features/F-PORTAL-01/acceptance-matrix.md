# F-PORTAL-01: Acceptance Matrix

| Criterion | Requirement Summary | Implementation Artifacts | Verification Method | Status |
|---|---|---|---|---|
| **AC-1** | Summary metrics dashboard showing aggregate transaction counts, status breakdowns, and channel distributions | `src/components/unresolved/MetricCards.tsx`, `UnresolvedConsole.tsx` | Metrics calculated from API response; renders KPI tiles for total, pending/unknown split, Adyen/Web3 split, and max age | **PASSED** |
| **AC-2** | Filter unresolved payments by channel or age threshold; display state, elapsed duration, channel reference | `src/components/unresolved/FilterToolbar.tsx`, `UnresolvedTable.tsx`, `apiClient.ts` | Filter toolbar triggers debounced query to `/api/v1/payments/unresolved`; renders paginated table with badges and duration | **PASSED** |
| **AC-3** | Detailed transaction timeline and metadata displayed upon selecting an unresolved payment record | `src/components/unresolved/PaymentDetailModal.tsx`, `apiClient.ts` | Row click opens modal dialog, calls `GET /api/v1/payments/{id}`, displays financial amounts, channel references, and timeline | **PASSED** |
| **AC-4** | Deny access and display guidance when unauthorized or caller lacks operator role | `src/components/common/ErrorAlert.tsx`, `AuthContext.tsx`, `Program.cs` (`PortalPolicy`) | API returns 401/403 Problem Details; UI displays security banner with role switch action; tested in `UnresolvedPaymentEndpointsIntegrationTests` & `PortalCorsIntegrationTests` | **PASSED** |

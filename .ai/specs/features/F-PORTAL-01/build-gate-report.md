# F-PORTAL-01: Build Gate Report

**Date:** 2026-10-13  
**Verdict:** GREEN  

## Gates Executed

1. **Frontend Unit Tests (Vitest):**
   - Command: `npm test`
   - Output: 2 test files passed, 3 tests passed (tokenService, apiClient).
   - Verdict: **GREEN**

2. **Frontend Type Check & Production Bundle:**
   - Command: `npm run build` (`tsc -b && vite build`)
   - Output: Built 1,936 modules in 3.58s with zero errors or warnings.
   - Verdict: **GREEN**

3. **Backend Static Formatting Gate:**
   - Command: `dotnet format --verify-no-changes`
   - Output: Clean (exit code 0).
   - Verdict: **GREEN**

4. **Backend Test Suite (xUnit, NetArchTest, Testcontainers):**
   - Command: `dotnet test --no-restore`
   - Unit Tests: 337 passed, 0 failed.
   - Architecture Tests: 3 passed, 0 failed.
   - Integration Tests: 99 passed, 0 failed (including `PortalCorsIntegrationTests`).
   - Total: **439 passed, 0 failed**.
   - Verdict: **GREEN**

# Review Report — F-PORTAL-04: Merchant API Keys management console

## Code Review & Architecture Conformance

### Architectural Conformance
- **ADR-004 (Dual Authentication & API Keys):** Direct self-service portal interface for merchant API credentials created in `F-EDGE-06`.
- **ADR-013 (Web Portal Architecture & Hosting):** Implemented in React 19 + TypeScript + TailwindCSS + Lucide icons.
- **ADR-014 (Design System):** Consistent dark-mode palette, typography, badges, and layout grammar.

### Acceptance Criteria Verification
- [x] **AC-1:** "Merchant API Keys" console provided in navigation rail displaying active and revoked keys with name, prefix, scopes, creation date, and status.
- [x] **AC-2:** "Generate New Key" modal with name validation and scope checkboxes (`payments:write`, `payments:read`).
- [x] **AC-3:** Raw API key displayed in modal upon creation with one-click copy and prominent warning that it will never be displayed again.
- [x] **AC-4:** Confirmation prompt before revoking an active key, with immediate status update.

## Review Verdict: APPROVED
All acceptance criteria are satisfied with clean code, responsive design, and automated test coverage.

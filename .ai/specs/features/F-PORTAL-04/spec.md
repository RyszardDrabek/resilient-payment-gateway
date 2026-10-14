---
id: F-PORTAL-04
topic: PORTAL
phase: merchant-api-keys-and-webhook-dlq
status: done
---

# F-PORTAL-04: Merchant API Keys management console

- **Topic:** [PORTAL](../../topics/portal.md)
- **Type:** Explicit
- **Value class:** USER-VISIBLE
- **Source:** Phase 7 intake: merchant-api-keys-and-webhook-dlq

## Description

Delivers an interactive management console in the web portal for merchants to generate scoped API keys, reveal raw keys once with one-click copy, list active keys with prefix/creation date, and revoke compromised or obsolete keys.

## Business context

Empowers merchant developers and technical teams to self-serve backend API authentication credentials with granular privileges without manual IT intervention.

## Acceptance criteria

- AC-1 THE portal SHALL provide an "API Keys" console under the merchant navigation menu displaying all active and revoked API keys with key name, masked prefix (e.g. `rpg_live_...`), scopes, creation date, and status.
- AC-2 WHEN a merchant clicks "Generate New Key", THE portal SHALL display a creation modal with key name and scope selection checkboxes (`payments:write`, `payments:read`).
- AC-3 UPON successful creation, THE portal SHALL display the raw API key in a modal with a one-click copy button, accompanied by a prominent warning that the key will never be displayed again.
- AC-4 WHEN a merchant initiates revocation for an active key, THE portal SHALL prompt for confirmation and immediately update the key's status to revoked upon confirmation.

## Interface contract

- **Resource / surface:** `/portal/api-keys` (Portal Route), `ApiKeysConsole.tsx`
- **Operation:** SPA user interaction consuming `F-EDGE-06` REST endpoints
- **Response shape category:** interactive UI
- **Terminal status classes:** UI rendered | Success feedback | Error toast / problem banner
- **Load-bearing fields:** `name`, `scopes`, `prefix`, `rawKey`, `isRevoked`, `createdAt`

## Out of scope

- IP address whitelist configuration in the UI.

## Dependencies

F-EDGE-06, F-PORTAL-01

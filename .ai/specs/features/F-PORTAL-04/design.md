# F-PORTAL-04: Technical Design — Merchant API Keys Management Console

## Overview

`F-PORTAL-04` introduces a self-service interactive management console in the Web Portal for merchants and operations teams to manage backend API authentication credentials. Merging into the single-page application structure of `PaymentGateway.Portal`, it consumes the REST endpoints established in `F-EDGE-06` (`GET /api/v1/merchants/{merchantId}/api-keys`, `POST /api/v1/merchants/{merchantId}/api-keys`, `POST /api/v1/merchants/{merchantId}/api-keys/{keyId}/revoke`).

---

## Architectural Mapping

- **ADR-004:** Dual authentication — JWT Bearer and API Key credentials.
- **ADR-013:** Web Portal Architecture & Hosting — React 19, TypeScript, TailwindCSS, Lucide icons, Vitest.
- **ADR-014:** Design System tokens — rich dark mode palette (`#0b0f19` canvas, `#111827` surface, `#1f2937` card, `#374151` borders, emerald/amber/red status badges).

---

## UI Component Hierarchy (`src/PaymentGateway.Portal/src/components/apikeys/`)

1. **`ApiKeysConsole.tsx`**:
   - Container component embedded in `AppShell` with navigation tab `'api-keys'`.
   - Header with active merchant switcher / context indicator and "Generate New Key" primary action button.
   - Summary cards: Total Keys, Active Keys, Revoked Keys.
   - Keys Data Table:
     - Key Name & Scope badges (`payments:write`, `payments:read`)
     - Prefix display with copy chip (`rpg_live_...` / `rpg_test_...`)
     - Environment badge (Live vs Test)
     - Creation date (formatted local string)
     - Status badge: Active (green pulse) / Revoked (gray/red)
     - Actions column: "Revoke" button with confirmation prompt for active keys.

2. **`GenerateApiKeyModal.tsx`**:
   - Accessible modal overlay for creating a new key.
   - Form inputs:
     - Key Name (text input, required)
     - Environment (Live vs Test toggle / radio)
     - Scopes checkboxes (`payments:write`, `payments:read`)
   - Cancel / Submit actions with loading spinner and error display.

3. **`KeyCreatedModal.tsx`**:
   - Modal displaying raw key upon successful creation.
   - Prominent amber warning banner ("This key will never be displayed again. Store it securely.").
   - One-click copy button with visual feedback ("Copied!").
   - Close button to return to the keys list.

4. **`RevokeKeyModal.tsx`**:
   - Confirmation dialog before calling the revoke endpoint.
   - Clear warning explaining that applications using this key will immediately be denied access (401/403).

---

## Client Service Extensions (`src/PaymentGateway.Portal/src/services/apiClient.ts`)

- `fetchMerchantApiKeys(token: string, merchantId: string): Promise<MerchantApiKeyDto[]>`
- `createMerchantApiKey(token: string, merchantId: string, request: CreateApiKeyRequest): Promise<MerchantApiKeyCreatedDto>`
- `revokeMerchantApiKey(token: string, merchantId: string, keyId: string): Promise<void>`

---

## Data Models (`src/PaymentGateway.Portal/src/types/api.ts`)

```typescript
export interface MerchantApiKeyDto {
  id: string;
  merchantId: string;
  name: string;
  prefix: string;
  scopes: string[];
  createdAt: string;
  isRevoked: boolean;
  revokedAt: string | null;
}

export interface CreateApiKeyRequest {
  name: string;
  scopes: string[];
  isLive?: boolean;
}

export interface MerchantApiKeyCreatedDto {
  id: string;
  merchantId: string;
  name: string;
  prefix: string;
  scopes: string[];
  createdAt: string;
  rawKey: string;
}
```

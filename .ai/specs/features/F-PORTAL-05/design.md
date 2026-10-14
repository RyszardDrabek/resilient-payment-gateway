# F-PORTAL-05: Technical Design — Webhook DLQ Delivery Logs & Manual Replay Console

## Overview

`F-PORTAL-05` delivers an interactive console in the Web Portal for merchants and operations teams to inspect failed outbound webhook notifications that have landed in the Dead-Letter Queue (DLQ) after retry exhaustion. It provides rich diagnostics (HTTP status codes, raw error details, JSON payload preview) and enables one-click manual re-dispatch (`replay`) with real-time feedback.

---

## Architectural Mapping

- **ADR-004:** Dual authentication — requests authorized using JWT Bearer tokens with merchant identity or `role: ops`.
- **ADR-012:** Operational hardening — resilient webhook delivery with persistent DLQ table (`pay.MerchantWebhookDeadLetters`).
- **ADR-013:** Web Portal Architecture & Hosting — React 19, TypeScript, TailwindCSS, Lucide icons, Vitest.
- **ADR-014:** Design System tokens — rich dark mode palette (`#0b0f19` canvas, `#111827` surface, `#1f2937` card, `#374151` borders, emerald/amber/rose status badges).

---

## UI Component Hierarchy (`src/PaymentGateway.Portal/src/components/webhooks/`)

1. **`WebhookDlqConsole.tsx`**:
   - Primary view accessible as a subview/tab in the Webhooks console or dedicated navigation view.
   - Summary statistics cards:
     - Total Dead Letters
     - Pending / Exhausted Failures
     - Successfully Replayed
   - Search & Filter Toolbar:
     - Search input (by `eventId`, `destinationUrl`, `paymentId`, or error message)
     - Status toggle filter: `All` | `Exhausted` | `Replayed`
     - Refresh button
   - DLQ Entries Table:
     - Failure Timestamp (formatted local date & time)
     - Event ID with copy chip
     - Payment ID
     - Destination URL
     - Last Status Code badge (e.g. 500, 503, 504 Gateway Timeout)
     - Attempt Count
     - Status Badge (`Exhausted` with amber/rose badge vs `Replayed` with emerald badge)
     - Actions:
       - "Details" button to open diagnostics drawer/modal
       - "Replay" action button (enabled for `exhausted` records, disabled with timestamp for `replayed`)

2. **`DlqDetailsModal.tsx`**:
   - Detailed modal for examining a failed webhook:
     - Event ID, Payment ID, Destination URL, Timestamp
     - Delivery failure diagnostics banner (HTTP status code, error message)
     - Attempt counter
     - Formatted, syntax-highlighted JSON payload with one-click copy button
     - Replay button inside modal for immediate redelivery from the details view

3. **Integration with `WebhooksConsole.tsx`**:
   - Tab switcher at top of Webhooks console:
     - **Tab 1:** "Configuration & Simulator" (existing `WebhookConfigCard`, `SecretKeyCard`, `EventSimulatorPanel`)
     - **Tab 2:** "Dead-Letter Queue (DLQ)" (`WebhookDlqConsole`) with failure count badge.

---

## Client Service Extensions (`src/PaymentGateway.Portal/src/services/apiClient.ts`)

- `fetchMerchantWebhookDlq(token: string, merchantId: string, status?: string, page?: number, pageSize?: number): Promise<MerchantWebhookDeadLetterDto[]>`
- `replayMerchantWebhookDlqEntry(token: string, merchantId: string, dlqEntryId: string): Promise<ReplayMerchantWebhookResult>`

---

## Data Models (`src/PaymentGateway.Portal/src/types/api.ts`)

```typescript
export interface MerchantWebhookDeadLetterDto {
  id: string;
  eventId: string;
  paymentId: string;
  merchantId: string;
  destinationUrl: string;
  payloadJson: string;
  deliveryAttempts: number;
  lastErrorMessage: string;
  lastStatusCode: number | null;
  status: string; // 'exhausted' | 'replayed'
  failedAt: string;
  replayedAt: string | null;
  attemptCount?: number;
  lastError?: string;
  lastAttemptedAt?: string;
}

export interface ReplayMerchantWebhookResult {
  success: boolean;
  message: string;
  statusCode: number | null;
  replayedAt: string | null;
}
```

# Simplifier Report — F-PORTAL-05: Webhook DLQ delivery logs & manual replay console

## Simplification & Clean Code Review

### 1. Component Modularity & Separation of Concerns
- `DlqDetailsModal` is encapsulated as a standalone dialog handling payload formatting, copy action, and modal-level replay trigger without polluting the main console table logic.
- `WebhookDlqConsole` manages entry fetching, query/status filtering, local optimistic state transitions (`exhausted` -> `replayed`), and summary metric computations.
- `WebhooksConsole` provides clean sub-tab switching so merchants can switch seamlessly between webhook configuration & live simulation and dead-letter queue records.

### 2. Design System & Accessibility
- Aligned with ADR-014 design system: rich dark theme palette (`#0b0f19` canvas, `#111827` card surface, `#1f2937` muted surface, `#374151` borders).
- Accessible interactive controls: high-contrast badges for status classes (`exhausted` in amber/rose, `replayed` in emerald), readable code blocks for JSON payloads with one-click copy, and screen-reader accessible button labels.

### 3. Error Handling & State Resilience
- Graceful API error handling with toast and banner feedback.
- Individual loading state tracking for replay actions to prevent concurrent replay spam on the same record while keeping the rest of the UI interactive.

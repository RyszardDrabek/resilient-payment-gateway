# Topic: EVT

**Phase:** mocked-payment-lifecycle
**Description:** Transactional outbox and published financial event vocabulary for downstream consumers.

## Scope

**In scope.** Durable publication of payment lifecycle events (authorize/decline/capture/cancel/refund and pending/unknown) with payment id, transition, party identifier, amount, currency, occurrence time, event identifier, and payment aggregate version; delivery after process kill proven by a phase-1 test consumer; no raw instrument values in event payloads (permanent — omit in phase 1).

**Out of scope.** Broker install (scaffold); RISK/WEB3 production consumers and topology; webhook→domain events (ADYEN); exactly-once broker guarantees (architecture); HTTP event browse API; cold storage / historical replay for new consumers; reversible masking of instruments inside events.

## Cross-feature workflows

1. PAY completes a transition (or pending/unknown on channel timeout) → same-transaction outbox record (event and payment transition commit together) → dispatcher delivers → phase-1 test consumer observes the event.
2. Process killed after commit, before consumer observation → after restart, event still arrives at the test consumer (phase-1 eval).
3. Idempotent PAY command replay → at most one logical lifecycle event for that transition (no duplicate emission).
4. Later phases: RISK and others consume the same vocabulary without a contract break on party identifier, amount/currency, event id, or version.

## Glossary

- **Payment lifecycle event** — published fact that a payment reached a named outcome. **Closed vocabulary (seven):** `authorized`, `declined`, `captured`, `cancelled`, `refunded`, `pending`, `unknown`. No `failed` / `settled` / `unresolved` event types.
- **Event identifier** — stable unique id for one logical lifecycle emission; consumers use it for at-least-once dedupe.
- **Payment aggregate version** — monotonic version on the payment at emission time; consumers use it to order events for one payment.
- **Phase-1 test consumer** — in-repo consumer used only to prove kill-delivery; not a production RISK surface.
- **Delivered** — observed by the phase-1 test consumer after restart/recovery, not merely written to an outbox table.

## Features

| ID | Name | Type | Value class |
| -- | ---- | ---- | ----------- |
| [F-EVT-01](../features/F-EVT-01/spec.md) | Payment lifecycle event publication | Explicit | USER-VISIBLE |
| [F-EVT-02](../features/F-EVT-02/spec.md) | Transactional outbox retention & pruning | Explicit | ENABLING |
| [F-EVT-03](../features/F-EVT-03/spec.md) | End-to-end distributed tracing correlation across Edge API, Outbox, RabbitMQ, Worker, and Webhooks | Explicit | USER-VISIBLE |

## Dependencies

- **PAY** — transitions and party identifier originate in PAY; EVT publishes in the same transaction as the payment state change (aggregate-raised domain event preferred; planner confirms).
- **EDGE** — telemetry masking remains EDGE; EVT permanently forbids raw instruments in event payloads (omit in phase 1). Closed the EDGE-deferred event-payload redaction question.
- Scaffold owns RabbitMQ / MassTransit bus wiring and the restartable host / compose harness needed for the kill-test.

## Build order (within phase 1)

1. After F-PAY-01 + F-PAY-03 emit at least authorize/decline outcomes — kill-test can run on this subset.
2. F-EVT-01 outbox + vocabulary + test consumer (kill-test) against restartable host + broker from scaffold.
3. Extend emissions as F-PAY-02 transitions land (capture/cancel/refund); pending/unknown when PAY channel-timeout path exists.

Declared dependencies (F-PAY-01/02/03) reflect full verification; partial slices above are allowed for incremental build.

## Decisions taken during /rdf-features

- Merged intake “Transactional Outbox” and “Payment event contracts & consumer topology” into **F-EVT-01** — one vertical slice for the phase-1 kill-delivery journey; production topology deferred.
- **Instrument values in events:** omit entirely (permanent rule; phase-1 implementation = omit, not mask-in-place).
- **Pending/unknown:** emit a lifecycle event (aligns with PAY channel-timeout decision).
- **Declined authorize:** emit a declined event.
- Party key = PAY opaque party identifier, unchanged.
- Phase 5 (intake-02): added F-EVT-02 (Transactional outbox retention & pruning) via background cleanup worker with configurable 14-day TTL per ADR-012.

## Decisions from review (2026-08-31)

- **Load-bearing event fields (extend F-EVT-01 interface contract at plan time):** payment identifier, transition/outcome, party identifier, amount, currency, occurrence time (UTC), event identifier, payment aggregate version. Prevents RISK/WEB3 contract break and enables consumer dedupe/order.
- **Same-transaction atomicity:** a lifecycle event SHALL NOT be published for a payment transition that did not commit; event write and payment state change succeed or fail together.
- **At-most-one logical event:** idempotent PAY command replay SHALL NOT emit a second lifecycle event for the same transition.
- **Pre-commit crash window:** if the settlement channel succeeds and the process dies before the payment transition commits, no event exists until restart resolves via PAY in-flight lease → pending/unknown (then AC-2 emits). Phase-1 does not claim zero money-without-event before commit; phase-2 ADYEN reconciliation closes that window. Documented as known integrity boundary.
- **Event raise site:** prefer domain event raised from the payment aggregate, persisted to outbox in the same unit of work as PAY handlers — additive to PAY, not a retrofit of completed PAY transaction boundaries beyond including outbox write in that unit of work.
- **Kill-test harness:** owned by scaffold (restartable app host + broker in compose); EVT owns the test consumer and the eval assertion.
- **Seven outcomes:** vocabulary is defined for all seven; phase-1 mandatory for kill-test is authorize/decline; remaining outcomes required as their PAY paths land (build order).
- **Schema evolution / retention / replay:** deferred to architecture open questions (versioning convention, outbox prune, historical replay for new consumers).

## Open questions carried from intake-01

- Production consumer topology, DLQ, and retry policy (architecture / later phases).
- Exactly-once vs at-least-once + consumer idempotency (architecture — consumers use event identifier).
- Whether phase-2 ADYEN webhook reconciliation emits additional event types into this vocabulary — **closed by ADYEN:** unresolved/uncorrelated are ops-visible only; resolved outcomes reuse the existing seven lifecycle types.
- Event schema versioning convention (architecture).
- Outbox retention/pruning and historical replay for new consumers (architecture).

## Decisions from corpus review (2026-09-27)

- **WEB3 on-chain revert / underfunded (no new EVT type):** if capture/refund was broadcast (pending event already emitted) and then reverts or fails on-chain, PAY **restores the prior committed state** via apply-acquirer-outcome (e.g. stay/return `authorized` after failed capture) and **does not** emit `captured`/`refunded`. No `failed` lifecycle event. Ops unresolved (timeout without finality) remains ops-only, not an EVT type — same as ADYEN.
- **Vocabulary aliases forbidden in events:** adapters must map provider prose (“settled”, “failed”) onto the seven outcomes or onto non-event ops states.

## Review notes

- Architect review (2026-08-31): findings addressed in **Decisions from review**; pre-commit window acknowledged; kill-test harness → scaffold; field set and same-transaction rules documented for planner.
- Requirements review (2026-08-31): atomicity and at-most-one noted; AC-5 interpreted as permanent omit rule; build-order reconciliation added; EDGE redaction delegation closed.
- Corpus review (2026-09-27): WEB3 failed → prior-state restore, no eighth EVT type; unresolved stays ops-only.

# Topic: EDGE

**Phase:** mocked-payment-lifecycle
**Description:** Payment gateway HTTP contract: RFC 7807 problem details, rate limiting, telemetry masking of instruments.

## Scope

**In scope.** Structured problem documents for **gateway HTTP surfaces** (payment API, Adyen notification ingress, RISK APIs): validation, not-found, conflict, server error, rate-limit; rate-limit and overload admission before handling; irreversible masking of PAN/token/IBAN-class values at the telemetry export boundary, on outbound acquirer HTTP client telemetry, and on inbound notification telemetry.

**Out of scope.** Payment domain semantics and success bodies (PAY); Adyen **wire protocol and HMAC verification** (ADYEN owns route + crypto verification; EDGE owns shared problem-document, rate-limit, and masking **middleware contract** applied on that route); financial event payload contents (EVT omits instruments); instrument fingerprint for RISK; exact HTTP status codes and type URI registry (architecture); caller authentication (scaffold); pending/unknown payment outcomes (PAY returns payment resource, not a problem document).

## Cross-feature workflows

1. Caller hits payment API → F-EDGE-02 admission check (rate limit / overload) → if rejected, rate-limit problem document (F-EDGE-01 shape).
2. Request admitted → PAY handles command → on terminal failure, F-EDGE-01 renders payment-scoped problem document (includes trace/correlation identifier for ops).
3. Successful or failed PAY lifecycle → F-EDGE-03 masks instrument values in exported logs/traces, including outbound acquirer HTTP client telemetry.
4. Channel timeout → pending payment state (PAY); not an EDGE problem document.

## Glossary

- **Problem document** — RFC 7807 response describing a terminal failure; type identifies validation, not-found, conflict, server error, or rate limit.
- **Telemetry boundary** — the point where log and trace records leave the gateway process for collectors (Jaeger, etc.), including outbound HTTP client spans.
- **Masked form** — irreversible redaction of instrument digits; not a reversible token and not a risk fingerprint.
- **Overload** — global in-flight payment-request cap exceeded (distinct from per-caller quota); threshold deferred to architecture.

## Features

| ID | Name | Type | Value class |
| -- | ---- | ---- | ----------- |
| [F-EDGE-01](../features/F-EDGE-01/spec.md) | Payment API problem details | Explicit | USER-VISIBLE |
| [F-EDGE-02](../features/F-EDGE-02/spec.md) | Gateway rate limiting and overload protection | Explicit | USER-VISIBLE |
| [F-EDGE-03](../features/F-EDGE-03/spec.md) | Payment-instrument masking in telemetry and logs | Explicit | USER-VISIBLE (observability-verifiable; phase-1 eval criterion) |
| [F-EDGE-04](../features/F-EDGE-04/spec.md) | Unresolved payments operator query API | Explicit | USER-VISIBLE |
| [F-EDGE-05](../features/F-EDGE-05/spec.md) | Merchant outbound webhook notification dispatch | Explicit | USER-VISIBLE |
| [F-EDGE-06](../features/F-EDGE-06/spec.md) | Merchant API Key authentication & scoped permissions | Explicit | USER-VISIBLE |
| [F-EDGE-07](../features/F-EDGE-07/spec.md) | Webhook DLQ inspection & manual replay API | Explicit | USER-VISIBLE |


## Dependencies

- **PAY** — EDGE wraps payment reads and commands; problem documents cover PAY terminal classes delegated from PAY review.
- **ADYEN** — phase-1 verification of F-EDGE-03 uses instrument-bearing authorize payloads on the acquirer HTTP path (WireMock); ADYEN implements the client, EDGE owns redaction on that client's telemetry.
- **F-EDGE-01 before F-EDGE-02** — rate-limit rejections use the same problem-document contract.

## Build order (within phase 1)

1. F-EDGE-01 partial: AC-1, AC-2, AC-4, AC-5 against minimal PAY (F-PAY-01 + F-PAY-03) — validation, not-found, server-error.
2. F-EDGE-02 after F-EDGE-01 (rate-limit shape).
3. F-EDGE-03 once ADYEN WireMock path carries instrument fields on authorize requests (negative test: inject PAN in test harness, assert absent in exported telemetry).
4. F-EDGE-01 AC-3 completion: illegal-transition and idempotency conflicts after F-PAY-02 + ADYEN mock land.

Declared spec dependencies (F-PAY-01/02/03) reflect full verification; partial slices above are allowed for incremental build.

## Decisions taken during /rdf-features

- Kept three intake candidates as three features (1:1); no ENABLING slice.
- F-EDGE-01 owns conflict problem documents for idempotency violations (F-PAY-03), illegal transitions (F-PAY-02), and optimistic-concurrency losers (parallel capture vs cancel per PAY).
- Rate-limit identity: coarse API key or client IP until architecture defines tenancy (decision note — not repeated in AC wording).
- Masking at telemetry boundary including outbound acquirer HTTP client logs/traces; ADYEN does not own redaction policy.
- Pending/unknown channel outcomes: PAY payment resource in pending state; EDGE does not emit a problem document for in-flight settlement.
- Channel unavailable (WireMock unreachable before a request is sent): map to server-error problem document in phase 1. Channel deadline after a request was sent: PAY pending/unknown (not EDGE server-error) — aligned with ADYEN review.
- Phase 5 (intake-02): added F-EDGE-04 (unresolved payments operator query API) and F-EDGE-05 (merchant outbound webhook dispatch with HMAC signing and Polly retry) per ADR-012.

## Decisions from review (2026-08-31)

- **Validation problem detail:** validation-fail documents SHALL identify at least one invalid field or constraint name so integrators can correct input (extends F-EDGE-01 description; planner adds field pointers).
- **Correlation identifier:** every payment problem document SHALL carry a trace/correlation identifier ops can use with Jaeger (supports on-call stakeholder).
- **Overload definition:** F-EDGE-02 AC-3 triggers when global in-flight payment-request cap is exceeded; uses the same rate-limit problem type as AC-1; numeric cap in architecture. Phase-1 eval "overload rejected" satisfied by this global cap path.
- **Backoff wording:** phase 1 does not expose Retry-After; "backoff" means integrators infer rejection from rate-limit problem type and their own retry policy.
- **F-EDGE-03 falsifiability:** phase-1 tests inject instrument values on the ADYEN authorize HTTP path (test card field); assert masked/absent in exported telemetry — feature is not vacuous without PAN in PAY API body.
- **EVT redaction:** closed in EVT — F-EVT-01 omits raw instruments from event payloads permanently (phase-1 implementation = omit).
- **F-EDGE-03 value class:** accepted as USER-VISIBLE because phase-1 success criteria name "no instrument values in traces/logs" as an externally auditable outcome.

## Open questions carried from intake-01

- Numeric rate-limit quotas, global in-flight cap, and limit tiers (architecture).
- PCI posture: whether raw instruments ever enter persistence or API success bodies.
- Exact HTTP status mapping per problem type (architecture / planner).
- Retry-After and quota-reset signals (post phase 1).
- Event-payload instrument handling: closed by EVT (omit); no further EDGE action.

## Decisions from corpus review (2026-09-27)

- **Webhook ownership split (resolves ScopeConflict):** ADYEN owns the notification **route**, HMAC/replay admission, and Adyen wire shapes. EDGE owns the reusable **edge contract** (RFC 7807 problem documents, rate-limit/overload, instrument masking) applied to that route and to RISK APIs. EDGE out-of-scope no longer means “webhooks have no EDGE”; it means EDGE does not implement Adyen protocol.
- **Gateway HTTP edge:** F-EDGE-01/02 apply to payment API (phase 1), notification ingress (phase 2), and RISK APIs (phase 4) — confirmed corpus-wide.

## Review notes

- Architect review (2026-08-31): findings addressed in **Decisions from review** and build-order partial slices; acquirer HTTP redaction assigned to EDGE telemetry boundary; pending owned by PAY.
- Requirements review (2026-08-31): validation detail and correlation id added as decisions; overload defined; backoff drift noted; build order clarified; F-EDGE-03 observability USER-VISIBLE accepted.
- Corpus review (2026-09-27): EDGE↔ADYEN ownership clarified; EDGE scope = gateway HTTP edge, not PAY-only.

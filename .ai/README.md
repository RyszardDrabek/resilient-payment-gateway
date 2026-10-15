# Project status

**Mode:** greenfield  
**Status:** Phase 8 Done (operational-observability-and-metrics)  
**Current phase:** Phase 8 (operational-observability-and-metrics)  
**Stack:** .NET 10 modular monolith (Clean + CQRS/MediatR), PostgreSQL/EF Core, RabbitMQ/MassTransit, Polly, JWT, React 19/Vite/Tailwind portal, Prometheus/Grafana, compose-only MVP  
*Order:* [build/orders/main.md](build/orders/main.md)  
*Scaffold:* ✓ → [build/scaffold-report.md](build/scaffold-report.md)

## Pipeline

| Step | Status |
| ---- | ------ |
| Intake | done → [intake-03](intake/03-2026-10-12-merchant-and-operator-portal/intake.md) |
| Features | done (7 topics / 30 features) |
| Architecture | done → [system-overview.md](docs/system-overview.md) |
| Plan order | done → [orders/main.md](build/orders/main.md) |
| Scaffold | done → [scaffold-report.md](build/scaffold-report.md) |
| Tooling | done → [tooling-manifest.md](build/tooling-manifest.md) |
| Build | done (30/30 features built) → [reports/main-report.md](build/reports/main-report.md) |

## Topics

- [x] PAY (5 features) → [topics/pay.md](specs/topics/pay.md)
- [x] EDGE (8 features) → [topics/edge.md](specs/topics/edge.md)
- [x] EVT (2 features) → [topics/evt.md](specs/topics/evt.md)
- [x] ADYEN (5 features) → [topics/adyen.md](specs/topics/adyen.md)
- [x] WEB3 (2 features) → [topics/web3.md](specs/topics/web3.md)
- [x] RISK (2 features) → [topics/risk.md](specs/topics/risk.md)
- [x] PORTAL (6 features) → [topics/portal.md](specs/topics/portal.md)

## Architecture Decisions

| ADR | Title |
| --- | ----- |
| [ADR-001](docs/adr/adr-001-modular-monolith-clean-cqrs.md) | Modular monolith, Clean Architecture, CQRS |
| [ADR-002](docs/adr/adr-002-settlement-port-apply-outcome.md) | Settlement port, channel selection, apply-acquirer-outcome |
| [ADR-003](docs/adr/adr-003-transactional-outbox-evt.md) | Transactional outbox and payment lifecycle events |
| [ADR-004](docs/adr/adr-004-jwt-bearer-auth.md) | JWT Bearer authentication |
| [ADR-005](docs/adr/adr-005-card-data-tokens-only.md) | Card data posture — tokens only |
| [ADR-006](docs/adr/adr-006-testing-strategy-tdd.md) | Testing strategy — TDD |
| [ADR-007](docs/adr/adr-007-edge-http-contract.md) | EDGE HTTP contract |
| [ADR-009](docs/adr/adr-009-risk-fail-open-pgvector.md) | RISK fail-open, pgvector, pool isolation |
| [ADR-010](docs/adr/adr-010-idempotency-keys.md) | Idempotency keys, payload hash, in-flight lease |
| [ADR-011](docs/adr/adr-011-partial-settlement-balance-tracking.md) | Partial settlement state machine and balance tracking |
| [ADR-012](docs/adr/adr-012-operational-hardening-outbox-webhooks-query.md) | Operational hardening — outbox retention, merchant outbound webhooks, and unresolved query |
| [ADR-013](docs/adr/adr-013-web-portal-architecture-hosting-cors.md) | Web Portal Architecture, Docker Hosting & CORS Integration |
| [ADR-014](docs/adr/adr-014-design-system.md) | Design System |

Standards: [definition-of-done.md](standards/definition-of-done.md), [coding-conventions.md](standards/coding-conventions.md), [stack-context.md](standards/stack-context.md).

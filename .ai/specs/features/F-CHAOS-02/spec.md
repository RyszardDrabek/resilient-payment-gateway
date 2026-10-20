---
id: F-CHAOS-02
topic: CHAOS
phase: chaos-engineering-and-resilience-verification
status: done
---

# F-CHAOS-02: Transactional Outbox zero-loss resilience & queue catchup under broker partition

- **Topic:** [CHAOS](../../topics/chaos.md)
- **Type:** Explicit
- **Value class:** RESILIENCE
- **Source:** ADR-024

## Description

Verifies that the Transactional Outbox pattern guarantees mathematical zero message loss when the message broker (RabbitMQ) is completely severed or offline while the gateway processes transactions. Validates that once the broker connection is re-established, the outbox dispatcher automatically drains 100% of pending events in order, and that consumer crash redeliveries are handled with strict idempotency.

## Business context

Fulfills core financial ledger integrity requirements: no customer payment or refund event can ever be lost due to an asynchronous message broker failure.

## Acceptance criteria

- AC-1 WHEN RabbitMQ is disconnected, payment authorizations and captures SHALL succeed and atomically commit their outbox records to PostgreSQL.
- AC-2 WHEN RabbitMQ connectivity is restored, the outbox dispatcher SHALL automatically reconnect and dispatch 100% of pending messages without manual intervention.
- AC-3 REDELIVERED events resulting from worker abrupt termination SHALL NOT cause duplicate ledger entries or side-effects (idempotent consumers).

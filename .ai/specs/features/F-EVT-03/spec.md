---
id: F-EVT-03
topic: EVT
phase: operational-alerting-and-distributed-tracing
status: done
---

# F-EVT-03: End-to-end distributed tracing correlation across Edge API, Outbox, RabbitMQ, Worker, and Webhooks

- **Topic:** [EVT](../../topics/evt.md)
- **Type:** Explicit
- **Value class:** USER-VISIBLE
- **Source:** Phase 9: operational-alerting-and-distributed-tracing

## Description

Implement full distributed tracing correlation with W3C TraceContext (`traceparent`) traversing the entire asynchronous lifecycle: from inbound Edge API requests, through MediatR pipeline behaviors, persisted transactional outbox records, MassTransit/RabbitMQ broker publishing and consumption in Worker, to outbound merchant webhooks and external acquirer calls.

## Business context

When investigating a delayed, failed, or disputed payment, engineers and operators need a single continuous trace in Jaeger showing the exact timeline from customer payment command, through internal event outboxing and message queuing, to webhook notification delivery at the merchant's endpoint.

## Acceptance criteria

- AC-1 THE MediatR pipeline SHALL execute a `TracingPipelineBehavior` creating internal spans with tag `mediatr.request` under `ActivitySource("PaymentGateway")`.
- AC-2 THE `PaymentRepository` SHALL capture the active ambient `Activity.Current.Id` as `TraceParent` in `PaymentLifecycleEvent` when persisting transactional outbox records.
- AC-3 THE `OutboxDispatcherService` SHALL deserialize `TraceParent` from outbox records and initiate a child `Outbox.Dispatch` activity linking the parent trace context when publishing to RabbitMQ via MassTransit.
- AC-4 THE `MerchantWebhookDispatcher` SHALL initiate a `MerchantWebhook.Dispatch` activity linked to the lifecycle event's `TraceParent` and propagate the `traceparent` HTTP header on outbound webhook POST requests.
- AC-5 Both `PaymentGateway.Api` and `PaymentGateway.Worker` SHALL configure OpenTelemetry tracing to listen to `PaymentGateway` activity source alongside `MassTransit`, `Npgsql`, and `System.Net.Http`.

## Interface contract

- **ActivitySource:** `PaymentGateway` (version 1.0.0)
- **Span names:** `MediatR {RequestName}`, `Outbox.Dispatch`, `MerchantWebhook.Dispatch`
- **Propagation format:** W3C TraceContext (`traceparent`)

## Dependencies

F-PAY-01, F-EVT-01, F-EDGE-05, F-EDGE-08

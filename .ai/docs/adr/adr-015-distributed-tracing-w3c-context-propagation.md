# ADR-015: Distributed Tracing and W3C Trace Context Propagation

## Status

Accepted — 2026-10-19

## Context

The Resilient Payment Gateway processes transactions across multiple asynchronous boundaries:
1. External inbound HTTP requests at the Edge API layer.
2. In-process CQRS mediation via MediatR pipeline behaviors.
3. Durable persistence into PostgreSQL tables and Transactional Outbox records.
4. Asynchronous message publishing via MassTransit over RabbitMQ.
5. Background outbox dispatching and retention cleanup in the Worker service.
6. Outbound HTTP merchant webhook dispatching and retry handling.

Without distributed tracing, diagnosing latency bottlenecks, acquirer settlement delays, outbox publishing lag, or webhook failure cascades requires stitching together disparate log lines using correlation IDs. To provide comprehensive observability, the project requires an OpenTelemetry-native tracing architecture that propagates trace context seamlessly across in-process MediatR boundaries, database persistence boundaries, message brokers, and outbound HTTP requests, while adhering strictly to PCI-DSS masking standards ([ADR-005](adr-005-card-data-tokens-only.md)).

## Decision

The project implements end-to-end distributed tracing using OpenTelemetry .NET SDK with W3C Trace Context standard propagation:

1. **Central Activity Source:**
   - A shared `ActivitySource` named `"PaymentGateway"` (version `1.0.0`) is centralized in `PaymentGateway.Application.Common.PaymentTracing`.
   - Registered in OpenTelemetry Tracing builder across both `PaymentGateway.Api` and `PaymentGateway.Worker`.

2. **In-Process MediatR Span Correlation:**
   - Implemented `TracingPipelineBehavior<TRequest, TResponse>` in the MediatR pipeline.
   - Creates internal activity spans named after the request type (e.g., `MediatR.AuthorizePaymentCommand`), tagging them with `mediatr.request` and `mediatr.response_type`.
   - Records error status (`ActivityStatusCode.Error`) and captures exceptions on failure, and marks successful operations with `ActivityStatusCode.Ok`.

3. **Context Preservation across Transactional Outbox:**
   - Enhanced `PaymentLifecycleEvent` with an optional `TraceParent` property (`string? TraceParent = null`).
   - `PaymentRepository` captures the ambient trace context (`Activity.Current?.Id`) when serializing domain events into `pay.OutboxMessages`.
   - `OutboxDispatcherService` extracts the stored `TraceParent` header and starts a child span (`Outbox.Dispatch`) with `ActivityContext.Parse(evt.TraceParent, null)`, maintaining the unbroken causal trace graph across database persistence.

4. **Outbound Webhook Propagation:**
   - `MerchantWebhookDispatcher` starts an outbound activity span (`MerchantWebhook.Dispatch`) and injects the W3C `traceparent` HTTP header into external webhook requests, allowing merchant systems to correlate deliveries with gateway transactions.

5. **PCI-DSS Masking Integration:**
   - `AddInstrumentMasking()` is hooked into OpenTelemetry tracing export, ensuring PAN, card tokens, and IBANs are never exported to external collectors.

6. **Collector Export:**
   - Traces are exported via OTLP (`OTEL_EXPORTER_OTLP_ENDPOINT`) targeting Jaeger (`jaeger:4317`) in docker-compose.

## Consequences

- (+) **End-to-End Waterfall Tracing:** Full visibility from Edge API -> MediatR -> Outbox -> Worker -> Webhook delivery in a single unified trace tree in Jaeger.
- (+) **Zero Correlation Loss Across Queues:** Transactional outbox messages preserve causal lineage even if messages linger in the outbox table before being drained.
- (+) **W3C Standard Compliance:** Uses standard `traceparent` header (format `00-{traceId}-{spanId}-{flags}`) interoperable with merchants, gateways, and cloud observability platforms.
- (+) **Minimal Overhead:** Built on native .NET `System.Diagnostics.Activity` with near-zero memory allocation when no sampling is active.
- (-) **Outbox Payload Size:** Adds ~60 bytes (`TraceParent` string) to serialized outbox event payloads.

## Alternatives Considered

1. **Relying solely on `X-Correlation-Id` HTTP Header:**
   - *Rejected:* Correlation IDs provide flat grouping across log lines but lack span timings, parent-child causality, and visualization of synchronous vs asynchronous task durations in tools like Jaeger or Grafana Tempo.
2. **Proprietary APM SDKs (Datadog / Dynatrace / New Relic):**
   - *Rejected:* Vendor lock-in; violates the project requirement for vendor-neutral, open-standard OpenTelemetry Protocol (OTLP).
3. **MassTransit Native Activity Propagation Only:**
   - *Rejected:* MassTransit automatically correlates broker messages, but transactional outbox messages are staged in the database *before* hitting the broker. Ambient context would be lost without explicitly storing `TraceParent` in the outbox record.

## Related ADRs

- [ADR-003: Transactional Outbox and Payment Lifecycle Events](adr-003-transactional-outbox-evt.md)
- [ADR-005: Card Data Posture — Tokens Only](adr-005-card-data-tokens-only.md)
- [ADR-007: EDGE HTTP Contract](adr-007-edge-http-contract.md)
- [ADR-012: Operational Hardening — Outbox, Webhooks, Query](adr-012-operational-hardening-outbox-webhooks-query.md)

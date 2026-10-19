# Kubernetes Deployment Guide

This directory contains standalone Kubernetes manifests and instructions for deploying the **Resilient Payment Gateway** in production and staging environments.

---

## Architecture Overview

```
                      [ Ingress Controller (Nginx) ]
                                    │
                 ┌──────────────────┴──────────────────┐
                 ▼                                     ▼
        /api, /health, /metrics                        /
                 ▼                                     ▼
      [ payment-gateway-api ]               [ payment-gateway-portal ]
      (ClusterIP: 8080)                     (ClusterIP: 80)
      - Auth / SmartScheme                  - React 19 SPA (Nginx)
      - CQRS / MediatR                      - Operator & Merchant Consoles
      - Transactional Outbox
                 │                                     │
                 ├─────────────────┬───────────────────┤
                 ▼                 ▼                   ▼
          [ PostgreSQL ]      [ RabbitMQ ]       [ Observability ]
          - PaymentDb         - Event Bus        - Prometheus (9090)
          - RiskDb (pgvector) - MassTransit      - Alertmanager (9093)
                                       ▲         - Jaeger (4317)
                                       │
                            [ payment-gateway-worker ]
                            - Outbox Dispatcher
                            - Webhook Dispatcher & DLQ
                            - Risk Scoring Consumer
```

---

## Deployment Options

### Option A: Helm (Recommended)

From the project root:
```bash
helm upgrade --install payment-gateway deploy/helm/payment-gateway \
  --namespace payment-gateway \
  --create-namespace \
  --set secrets.jwtSigningKey="YOUR_32_CHAR_JWT_KEY" \
  --set secrets.dbPassword="YOUR_POSTGRES_PASSWORD" \
  --set secrets.rabbitmqPassword="YOUR_RABBITMQ_PASSWORD"
```

### Option B: Standalone Manifest (`kubectl apply`)

Apply the complete manifest stack:
```bash
kubectl apply -f deploy/k8s/payment-gateway-stack.yaml
```

---

## Health Checks & Diagnostics

| Pod | Probe | Endpoint | Port | Description |
| --- | ----- | -------- | ---- | ----------- |
| `api` | Liveness | `/healthz` | 8080 | Verifies Kestrel HTTP listener is active |
| `api` | Readiness | `/health` | 8080 | Verifies database connectivity to PaymentDb & RiskDb |
| `portal` | Liveness & Readiness | `/` | 80 | Verifies Nginx SPA server is serving `index.html` |

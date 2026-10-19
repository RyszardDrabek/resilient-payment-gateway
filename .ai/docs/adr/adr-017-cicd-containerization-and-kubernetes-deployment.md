# ADR-017: CI/CD Pipeline Automation, Containerization & Kubernetes Deployment

## Status

Accepted — 2026-10-19

## Context

With the completion of the core payment gateway (Clean Architecture, MediatR CQRS, Transactional Outbox), Web Portal (React 19 SPA), and observability infrastructure (Prometheus, Alertmanager, Jaeger, Grafana), the platform required production-grade packaging, automated delivery pipelines, and container orchestration readiness.

Specifically:
- Continuous Integration was previously limited to backend .NET compilation and unit testing. Portal builds (TypeScript/Vite), Vitest unit tests, Playwright end-to-end scenarios, Docker image packaging, and infrastructure validation were unverified in pull requests.
- Deployments relied primarily on local `docker-compose.yml`, which is unsuitable for high-availability enterprise environments requiring rolling updates, horizontal autoscaling (HPA), zero-downtime secret rotation, and declarative network isolation.
- Standardized health check probes (`/healthz` for liveness and `/health` for readiness) were needed to prevent Kestrel pods from serving traffic before database connections and warm-up cycles completed.

## Decision

The project adopts an enterprise delivery standard spanning GitHub Actions CI, multi-stage Docker builds, and production Kubernetes/Helm deployment manifests:

1. **GitHub Actions Comprehensive Pipeline (`.github/workflows/ci.yml`):**
   - **Backend Job (`backend`):** Validates .NET 10 compilation, code formatting (`dotnet format --verify-no-changes`), unit tests (393 tests), architecture boundary tests (Hexagonal/Clean constraints), and integration tests.
   - **Portal Job (`portal`):** Runs on Node 22, performs TypeScript compilation (`tsc -b`), production Vite bundling, Vitest component unit tests (16 tests), and automated Playwright E2E browser tests (19 specs). Artifacts from failing test runs are preserved for diagnostics.
   - **Container Packaging Job (`docker-build`):** Executes Docker Buildx validation against all multi-stage Dockerfiles (`compose/Dockerfile.api`, `compose/Dockerfile.worker`, `src/PaymentGateway.Portal/Dockerfile`) to ensure container images build cleanly without layer leaks.
   - **Helm Validation Job (`helm-validate`):** Executes `helm lint` and `helm template` dry-run validation against the Helm chart to verify Kubernetes syntax and template integrity.

2. **Standardized Kubernetes Health Probes:**
   - **Liveness Probe (`/healthz`):** A lightweight, unauthenticated endpoint mapped in `PaymentGateway.Api` returning HTTP 200 `{"service":"PaymentGateway.Api","status":"Healthy"}`. Used by kubelet to detect process deadlocks without generating database load.
   - **Readiness Probe (`/health`):** Validates active connectivity to both `PaymentDbContext` and `RiskDbContext`. Drops pod from Service endpoints and Ingress routing if database pools are unreachable.
   - **Portal Probes (`/`):** Nginx-served static SPA health check.

3. **Production Helm Chart (`deploy/helm/payment-gateway/`):**
   - Configurable parameters in `values.yaml` for replica counts, resource requests/limits, HorizontalPodAutoscaler (HPA targeting 75% CPU and 80% RAM), and container image tags.
   - Template hierarchy:
     - `api-deployment.yaml` & `api-service.yaml`: ClusterIP service on port 8080 with Prometheus scrape annotations (`prometheus.io/scrape: "true"`).
     - `worker-deployment.yaml`: Scalable background event consumer and outbox dispatcher.
     - `portal-deployment.yaml` & `portal-service.yaml`: Nginx SPA frontend on port 80.
     - `ingress.yaml`: Ingress configuration with path-based routing (`/api`, `/health`, `/metrics` to API, `/` to Portal) and optional TLS termination.
     - `configmap.yaml` & `secret.yaml`: Clear separation of non-sensitive environment configuration from sensitive credentials (JWT keys, DB passwords, RabbitMQ credentials).
     - `networkpolicy.yaml`: Default ingress traffic policy restricting cluster ingress to HTTP 8080 and 80 ports.

4. **Standalone Manifests (`deploy/k8s/`):**
   - Complete `payment-gateway-stack.yaml` for teams deploying via GitOps engines (ArgoCD, FluxCD) or pure `kubectl apply -f`.

## Consequences

### Positive
- Every pull request runs end-to-end verification across both .NET backend, React frontend, Docker packaging, and Helm chart templates.
- Production readiness: Kubernetes clusters can horizontally scale the gateway under load with HPA and route traffic safely using liveness/readiness probes.
- Consistent configuration across environments (development in docker-compose, staging/production in Kubernetes/Helm).

### Negative / Trade-offs
- CI pipeline execution time increases with browser E2E and Docker build stages (mitigated via caching and parallel job execution).
- Helm chart maintenance is required as new environment variables or services are added.

# Resilient Payment Gateway `v1.0.0`

[![Release](https://img.shields.io/badge/release-v1.0.0-blue.svg)](RELEASE_NOTES.md)
[![Build](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![React](https://img.shields.io/badge/React-19.0-61dafb.svg)](src/PaymentGateway.Portal)

Enterprise-grade FinTech payment gateway (.NET 10 modular monolith): Clean Architecture, CQRS, Adyen + Web3 channels, async risk scoring, 3D Secure 2 (SCA), and modern React 19 operational portal. Specs live under `.ai/`.

- 📖 **Documentation**: [C4 Architecture & System Overview](.ai/docs/system-overview.md)
- 🛒 **API Integration**: [Merchant Integration Guide](docs/merchant-integration-guide.md)
- 🚀 **Milestones**: [Release Notes v1.0.0](RELEASE_NOTES.md)

## Running locally

**Docker Desktop must use Linux containers** (tray icon → *Switch to Linux containers* / WSL2 engine). Windows-container mode cannot pull `pgvector`, `rabbitmq`, Jaeger, etc.

```bash
docker context use desktop-linux
docker compose -f compose/docker-compose.yml up --build
```

- **Portal (Operations & Merchant)**: `http://localhost:3000`
- **Gateway API & Swagger**: `http://localhost:5080/` and `http://localhost:5080/health`  
- **Grafana Dashboards**: `http://localhost:3001` (admin/admin)
- **Jaeger Distributed Tracing**: `http://localhost:16686`
- **RabbitMQ Management**: `http://localhost:15672` (guest/guest)

Web3 Anvil (optional): `docker compose -f compose/docker-compose.yml --profile web3 up`

Without Docker (API only, in-memory bus; needs Postgres for `/health`):

```bash
dotnet run --project src/PaymentGateway.Api
```

Copy `.env.example` → `.env` or use User Secrets for `Auth__JwtSigningKey` and `MEDIATR_LICENSE_KEY` (free Community key from [mediatr.io](https://mediatr.io/)).

## Install / build / test

```bash
dotnet restore PaymentGateway.slnx
dotnet build PaymentGateway.slnx
dotnet format PaymentGateway.slnx --verify-no-changes
dotnet test PaymentGateway.slnx
```

## Layout

- `src/` — Api, Worker, Domain, Application, Infrastructure, Edge
- `tests/` — Unit, Integration (Testcontainers), Architecture (NetArchTest)
- `compose/` — docker-compose + Dockerfiles
- `.ai/` — RDF specs, ADRs, build orders

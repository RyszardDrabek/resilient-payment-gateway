# Resilient Payment Gateway

Enterprise-grade FinTech payment gateway (.NET 10 modular monolith): Clean Architecture, CQRS, Adyen + Web3 channels, async risk scoring. Specs live under `.ai/`.

## Running locally

**Docker Desktop must use Linux containers** (tray icon → *Switch to Linux containers* / WSL2 engine). Windows-container mode cannot pull `pgvector`, `rabbitmq`, Jaeger, etc.

```bash
docker context use desktop-linux
docker compose -f compose/docker-compose.yml up --build
```

API: `http://localhost:5080/` and `http://localhost:5080/health`  
Jaeger: `http://localhost:16686` · Grafana: `http://localhost:3000` (admin/admin) · RabbitMQ UI: `http://localhost:15672`

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

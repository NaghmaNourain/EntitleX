# EntitleX

Enterprise license-extension platform (portfolio). Orchestrates extension requests across fictional vendor systems **CloudShield** and **NetworkGuard**. No proprietary IP.

Phase 3 adds provider simulator APIs (own DBs + seed data), `AppliedLicenseOperation` idempotency, and control-plane entitlement aggregation with Polly resilience. See [docs/implementation-progress.md](docs/implementation-progress.md).

## Architecture

- **Modular control-plane API** (`EntitleX.Api`) with Clean Architecture layers
- **DDD request aggregate** (`LicenseExtensionRequest` + items) with invariant-enforcing methods
- **Entitlement aggregation** across CloudShield + NetworkGuard (partial results, correlation IDs)
- **Vendor simulators** with own PostgreSQL databases and `AppliedLicenseOperation` claim semantics
- **Separate workers** for async work (fulfillment, outbox, reconciliation, notifications)
- **Lambda stubs** for scheduled approval reminders and expiration notifications
- Decision record: [docs/adr/0001-modular-api-vs-microservices.md](docs/adr/0001-modular-api-vs-microservices.md)

## Prerequisites

- [.NET SDK 8.x](https://dotnet.microsoft.com/download/dotnet/8.0) (see `global.json`)
- Node.js 20+ and npm (frontend)
- Docker + Docker Compose (local dependencies)

## Local quickstart

```bash
# Restore and build the solution
dotnet restore EntitleX.sln
dotnet build EntitleX.sln

# Start local infrastructure (Postgres with entitlex_platform / cloudshield / networkguard)
docker compose up -d

# Vendor simulators (Development migrates + seeds demo data)
dotnet run --project src/simulators/CloudShield/CloudShield.Api    # :5081
dotnet run --project src/simulators/NetworkGuard/NetworkGuard.Api  # :5082

# Platform API (aggregates from Providers:* BaseUrls)
dotnet run --project src/EntitleX.Api                             # :5080

# Tests
dotnet test tests/EntitleX.Domain.UnitTests
dotnet test tests/EntitleX.Application.UnitTests
dotnet test tests/EntitleX.Infrastructure.UnitTests
dotnet test tests/CloudShield.Domain.UnitTests
dotnet test tests/EntitleX.Infrastructure.IntegrationTests        # Docker for Testcontainers
dotnet test tests/CloudShield.Infrastructure.IntegrationTests     # Docker for Testcontainers

# Frontend scaffold
cd frontend && npm install && npm run dev
```

### Control-plane APIs

| Method | Path | Notes |
|--------|------|-------|
| `POST` | `/api/v1/license-extension-requests` | Create + submit; max 1000 items |
| `GET` | `/api/v1/license-extension-requests/{id}` | Load request with items |
| `GET` | `/api/v1/entitlements/search?customerId=` | Aggregate entitlements (partial OK) |
| `GET` | `/health/providers` | Per-provider readiness |
| `GET` | `/swagger` | OpenAPI UI (Development) |

### Simulator APIs (demo customer `CUST-1001`)

| Provider | Discovery | Details | Extension | Operation status |
|----------|-----------|---------|-----------|------------------|
| CloudShield | `GET /api/v1/accounts/{accountId}/entitlements` | `GET /api/v1/entitlements/{key}` | `POST .../license-extensions` | `GET /api/v1/license-operations/{logicalOperationId}` |
| NetworkGuard | `GET /api/v1/tenants/{tenantId}/licenses` | `GET /api/v1/licenses/{id}` | `POST .../extensions` | `GET /api/v1/operations/{logicalOperationId}` |

Workers (optional heartbeat stubs):

```bash
dotnet run --project src/workers/EntitleX.FulfillmentWorker
dotnet run --project src/workers/EntitleX.OutboxPublisher
dotnet run --project src/workers/EntitleX.ReconciliationWorker
dotnet run --project src/workers/EntitleX.NotificationService
```

## Docker Compose ports

| Service     | Ports                         | Notes                                      |
|-------------|-------------------------------|--------------------------------------------|
| Postgres    | `5432`                        | User/pass `entitlex` / `entitlex`          |
| Kafka       | `9092`                        | KRaft single-node                          |
| LocalStack  | `4566`                        | AWS emulator                               |
| Mailpit     | `1025` (SMTP), `8025` (UI)    | Dev email                                  |
| Jaeger      | `16686` (UI), `4317`/`4318`   | OTLP collectors                            |
| Prometheus  | `9090`                        | Metrics                                    |
| Grafana     | `3001`                        | UI (`admin` / `admin`) — host 3001 → 3000  |

Postgres init creates databases: `entitlex_platform`, `cloudshield`, `networkguard`.

## Health endpoints

| Service           | URL                              |
|-------------------|----------------------------------|
| EntitleX.Api      | `http://localhost:5080/health`   |
| CloudShield.Api   | `http://localhost:5081/health`   |
| NetworkGuard.Api  | `http://localhost:5082/health`   |

## Repository layout

```
src/
  EntitleX.{BuildingBlocks,Messaging,Domain,Application,Infrastructure,Api}/
  workers/          # BackgroundService heartbeat stubs
  simulators/       # CloudShield + NetworkGuard (own EF schemas)
  lambdas/          # Approval reminder + expiration notification stubs
frontend/           # Vite React + TypeScript (no feature pages yet)
infra/docker/       # Postgres init, Prometheus config
docs/               # ADR + implementation progress
```

## License

MIT — see [LICENSE](LICENSE).

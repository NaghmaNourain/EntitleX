# EntitleX

Enterprise license-extension platform (portfolio). Orchestrates extension requests across fictional vendor systems **CloudShield** and **NetworkGuard**. No proprietary IP.

Phase 2 adds the license-extension request aggregate, EF Core persistence for `entitlex_platform`, and create/get APIs. See [docs/implementation-progress.md](docs/implementation-progress.md).

## Architecture

- **Modular control-plane API** (`EntitleX.Api`) with Clean Architecture layers
- **DDD request aggregate** (`LicenseExtensionRequest` + items) with invariant-enforcing methods
- **Separate workers** for async work (fulfillment, outbox, reconciliation, notifications)
- **Vendor simulators** (CloudShield / NetworkGuard), each with Domain → Application → Infrastructure → Api
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

# Start local infrastructure
docker compose up -d

# Platform API (http://localhost:5080/health, Swagger in Development)
# Development migrates entitlex_platform on startup (Database:MigrateOnStartup)
dotnet run --project src/EntitleX.Api

# Vendor simulators
dotnet run --project src/simulators/CloudShield/CloudShield.Api    # :5081/health
dotnet run --project src/simulators/NetworkGuard/NetworkGuard.Api  # :5082/health

# Tests
dotnet test tests/EntitleX.Domain.UnitTests
dotnet test tests/EntitleX.Infrastructure.IntegrationTests   # requires Docker for Testcontainers

# Frontend scaffold
cd frontend && npm install && npm run dev
```

### License-extension API (Phase 2)

| Method | Path | Notes |
|--------|------|-------|
| `POST` | `/api/v1/license-extension-requests` | Create + submit; max 1000 items |
| `GET` | `/api/v1/license-extension-requests/{id}` | Load request with items |
| `GET` | `/swagger` | OpenAPI UI (Development) |

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
  simulators/       # CloudShield + NetworkGuard
  lambdas/          # Approval reminder + expiration notification stubs
frontend/           # Vite React + TypeScript (no feature pages yet)
infra/docker/       # Postgres init, Prometheus config
docs/               # ADR + implementation progress
```

## License

MIT — see [LICENSE](LICENSE).

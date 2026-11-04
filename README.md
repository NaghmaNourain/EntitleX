# EntitleX

Enterprise license-extension platform (portfolio). Orchestrates extension requests across fictional vendor systems **CloudShield** and **NetworkGuard**. No proprietary IP.

Phase 4 adds JWT/RBAC, day-band approval workflows, decision audit metadata, and notification emails with in-app links (Mailpit SMTP / LocalStack SES). See [docs/implementation-progress.md](docs/implementation-progress.md).

## Architecture

- **Modular control-plane API** (`EntitleX.Api`) with Clean Architecture layers
- **DDD request aggregate** (`LicenseExtensionRequest` + items) with invariant-enforcing methods
- **Approval case aggregate** with sequential, role-gated stages and `ApprovalVersion` / `ApprovedScopeHash`
- **JWT Bearer + RBAC** (`RenewalRepresentative`, `RenewalManager`, `DealDeskManager`, `PlatformAdmin`)
- **Entitlement aggregation** across CloudShield + NetworkGuard (partial results, correlation IDs)
- **Vendor simulators** with own PostgreSQL databases and `AppliedLicenseOperation` claim semantics
- **Notification emails** via Mailpit (SMTP) or LocalStack SES — app links only, no email approve buttons
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

# Start local infrastructure (Postgres, Mailpit, LocalStack w/ SES, …)
docker compose up -d

# Vendor simulators (Development migrates + seeds demo data)
dotnet run --project src/simulators/CloudShield/CloudShield.Api    # :5081
dotnet run --project src/simulators/NetworkGuard/NetworkGuard.Api  # :5082

# Platform API (Development: JWT + MigrateOnStartup + Mailpit SMTP)
dotnet run --project src/EntitleX.Api                             # :5080

# Optional: notification probe worker (same Email:* config)
dotnet run --project src/workers/EntitleX.NotificationService

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

### Auth (Development)

Secure defaults: APIs require a Bearer JWT except health/Swagger/dev-token. Production must set a strong `Jwt__SigningKey` (≥32 chars); the Development key below is **dev-only**.

Mint a seed token (Development + `Jwt:EnableDevTokenEndpoint=true`):

```bash
curl -s -X POST http://localhost:5080/api/v1/dev/tokens \
  -H 'Content-Type: application/json' \
  -d '{"role":"RenewalRepresentative"}'
```

| Role | Seed user id | Seed email |
|------|--------------|------------|
| `RenewalRepresentative` | `user-rep-001` | `renewal.rep@entitlex.local` |
| `RenewalManager` | `user-rm-001` | `renewal.manager@entitlex.local` |
| `DealDeskManager` | `user-dd-001` | `dealdesk.manager@entitlex.local` |
| `PlatformAdmin` | `user-admin-001` | `platform.admin@entitlex.local` |

Use the returned `accessToken` as `Authorization: Bearer <token>`.

### Approval rules

| Max extension days (across items) | Path |
|-----------------------------------|------|
| 1–30 | Auto-approved |
| 31–90 | Renewal Manager |
| 91–365 | Renewal Manager, then Deal Desk |
| >365 | Rejected (unsupported) |

### Control-plane APIs

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| `POST` | `/api/v1/dev/tokens` | Anonymous (Dev only) | Mint seed JWT by role |
| `POST` | `/api/v1/license-extension-requests` | Rep / Admin | Create + submit + open approval |
| `GET` | `/api/v1/license-extension-requests/{id}` | Authenticated | Load request with items |
| `POST` | `/api/v1/license-extension-requests/{id}/approval/start` | Rep / Admin | Start approval if missing |
| `GET` | `/api/v1/license-extension-requests/{id}/approval` | Authenticated | Case, stages, decisions |
| `POST` | `/api/v1/license-extension-requests/{id}/approval/decisions` | RM / DealDesk / Admin | `{ "outcome":"Approved\|Rejected", "comment":"..." }` |
| `GET` | `/api/v1/entitlements/search?customerId=` | Authenticated | Aggregate entitlements |
| `GET` | `/health` | Anonymous | Liveness |
| `GET` | `/swagger` | Anonymous (Dev) | OpenAPI UI |

### Email notifications

- Config: `Email:Provider` = `Smtp` (default → Mailpit `:1025`) or `Ses` (LocalStack `:4566`)
- Bodies include `Email:AppBaseUrl/requests/{id}` deep links — **no approve/reject email actions**
- Inspect Mailpit UI at `http://localhost:8025`

### Simulator APIs (demo customer `CUST-1001`)

| Provider | Discovery | Details | Extension | Operation status |
|----------|-----------|---------|-----------|------------------|
| CloudShield | `GET /api/v1/accounts/{accountId}/entitlements` | `GET /api/v1/entitlements/{key}` | `POST .../license-extensions` | `GET /api/v1/license-operations/{logicalOperationId}` |
| NetworkGuard | `GET /api/v1/tenants/{tenantId}/licenses` | `GET /api/v1/licenses/{id}` | `POST .../extensions` | `GET /api/v1/operations/{logicalOperationId}` |

Workers (optional):

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
| LocalStack  | `4566`                        | AWS emulator (includes **SES**)            |
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
  workers/          # BackgroundService workers (notifications use Email:*)
  simulators/       # CloudShield + NetworkGuard (own EF schemas)
  lambdas/          # Approval reminder + expiration notification stubs
frontend/           # Vite React + TypeScript (no feature pages yet)
infra/docker/       # Postgres init, Prometheus config
docs/               # ADR + implementation progress
```

## License

MIT — see [LICENSE](LICENSE).

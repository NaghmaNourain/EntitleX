# EntitleX

Enterprise license-extension platform (portfolio). Orchestrates extension requests across fictional vendor systems **CloudShield** and **NetworkGuard**. No proprietary IP.

Phase 11 adds OpenTelemetry (Jaeger), Prometheus/Grafana dashboards, structured Serilog, and performance hardening. See [docs/observability.md](docs/observability.md), [docs/performance.md](docs/performance.md), [docs/implementation-progress.md](docs/implementation-progress.md), and [frontend/README.md](frontend/README.md).

## Architecture

- **Modular control-plane API** (`EntitleX.Api`) with Clean Architecture layers
- **DDD request aggregate** (`LicenseExtensionRequest` + items) with invariant-enforcing methods
- **Durable saga** (`LicenseExtensionSaga`) with transition history, item outcomes, timeout hooks; request `InFulfillment` maps to saga `QueuedForFulfillment`/`Processing`
- **Forward recovery only** — never compensate successful license extensions when siblings fail
- **Approval case aggregate** with sequential, role-gated stages and `ApprovalVersion` / `ApprovedScopeHash`
- **JWT Bearer + RBAC** (`RenewalRepresentative`, `RenewalManager`, `DealDeskManager`, `PlatformAdmin`)
- **Entitlement aggregation** across CloudShield + NetworkGuard (partial results, correlation IDs)
- **Transactional outbox** → SQS fulfillment → Kafka provider commands (partition key = entitlement id) → simulator results → saga settlement
- **Vendor simulators** with own PostgreSQL databases and `AppliedLicenseOperation` claim semantics
- **Notification emails** via Mailpit (SMTP) or LocalStack SES — app links only, no email approve buttons
- **Workers**: OutboxPublisher, FulfillmentWorker (SQS + results), ReconciliationWorker, NotificationService probe
- **Scheduled Lambdas** (local host runners + EventBridge stubs): approval reminders and expiration notifications — see [docs/lambdas.md](docs/lambdas.md)
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

# Start local infrastructure (Postgres, Kafka, LocalStack SQS+SES, Mailpit, …)
docker compose up -d

# Vendor simulators (Development migrates + seeds demo data; Kafka consumers on)
dotnet run --project src/simulators/CloudShield/CloudShield.Api    # :5081
dotnet run --project src/simulators/NetworkGuard/NetworkGuard.Api  # :5082

# Platform API (Development: JWT + MigrateOnStartup + Mailpit SMTP)
dotnet run --project src/EntitleX.Api                             # :5080

# Messaging workers (require Compose LocalStack SQS + Kafka)
dotnet run --project src/workers/EntitleX.OutboxPublisher
dotnet run --project src/workers/EntitleX.FulfillmentWorker

# Optional: notification probe worker (same Email:* config)
dotnet run --project src/workers/EntitleX.NotificationService

# Optional: reconciliation worker (timeouts + provider repair)
dotnet run --project src/workers/EntitleX.ReconciliationWorker

# Phase 9 scheduled jobs (run-once local hosts; EventBridge stubs in docs/lambdas.md)
dotnet run --project src/lambdas/EntitleX.ApprovalReminder
dotnet run --project src/lambdas/EntitleX.ExpirationNotification -- --provider CloudShield
dotnet run --project src/lambdas/EntitleX.ExpirationNotification -- --provider NetworkGuard

# Tests
dotnet test tests/EntitleX.Domain.UnitTests
dotnet test tests/EntitleX.Application.UnitTests
dotnet test tests/EntitleX.Infrastructure.UnitTests
dotnet test tests/CloudShield.Domain.UnitTests
dotnet test tests/EntitleX.Infrastructure.IntegrationTests        # Docker for Testcontainers
dotnet test tests/CloudShield.Infrastructure.IntegrationTests     # Docker for Testcontainers

# Frontend (Phase 10) — proxies /api and /health to :5080
cd frontend && npm install && npm run dev     # http://localhost:5173
cd frontend && npm run build
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
| `GET` | `/api/v1/approvals/pending` | RM / DealDesk / Admin | Pending approval queue (`role`, `take`, `skip`) |
| `GET` | `/api/v1/audit/requests/{requestId}` | Authenticated | Audit events for a request |
| `GET` | `/api/v1/admin/ops/summary` | PlatformAdmin | Ops monitoring summary |
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
| Kafka       | `9092`                        | KRaft; auto-creates provider command/result topics |
| LocalStack  | `4566`                        | SQS (`entitlex-fulfillment` + DLQ) + SES   |
| Mailpit     | `1025` (SMTP), `8025` (UI)    | Dev email                                  |
| Jaeger      | `16686` (UI), `4317`/`4318`   | OTLP collectors                            |
| Prometheus  | `9090`                        | Scrapes API `/metrics` + worker `:9180–9183` |
| Grafana     | `3001`                        | UI (`admin` / `admin`); EntitleX Overview dashboard |

Postgres init creates databases: `entitlex_platform`, `cloudshield`, `networkguard`. LocalStack init creates SQS queues on ready.

### Messaging path (after final approval)

`OutboxMessage` (same PG txn) → OutboxPublisher → SQS `entitlex-fulfillment` → FulfillmentWorker → Kafka provider commands (key = entitlement id) → simulators → `entitlex.provider.results` → idempotent settlement.

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

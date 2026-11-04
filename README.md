# EntitleX

Enterprise license-extension platform (portfolio). Orchestrates extension requests across fictional vendor systems **CloudShield** and **NetworkGuard**. No proprietary IP.

**Phase 12** completes documentation packaging, optional AWS Terraform, container images, and CI image builds on top of Phases 1–11 (domain → messaging → saga → retry → reconciliation → lambdas → frontend → observability).

## Architecture (Phase 12 complete)

- **Modular control-plane API** (`EntitleX.Api`) with Clean Architecture layers — see [docs/architecture.md](docs/architecture.md), [docs/system-design.md](docs/system-design.md)
- **DDD aggregates**: `LicenseExtensionRequest`, `ApprovalCase`, durable `LicenseExtensionSaga` (forward recovery only)
- **JWT Bearer + RBAC** (`RenewalRepresentative`, `RenewalManager`, `DealDeskManager`, `PlatformAdmin`)
- **Transactional outbox** → SQS fulfillment → Kafka provider commands → simulator results → saga settlement
- **Selective retry** with `LogicalOperationId` / `AttemptId` / `ParentId` lineage and DB-backed idempotency
- **Reconciliation** before blind retry (probe provider status; never POST when Unknown/NotFound)
- **Vendor simulators** with own PostgreSQL DBs and unique `AppliedLicenseOperation` claims
- **Workers**: OutboxPublisher, FulfillmentWorker, ReconciliationWorker, NotificationService
- **Lambdas** (local hosts + EventBridge): ApprovalReminder, ExpirationNotification
- **Observability**: OTel → Jaeger, Prometheus/Grafana, Serilog — [docs/observability.md](docs/observability.md)
- **Frontend**: Vite React control-plane UI — [frontend/README.md](frontend/README.md)
- **Optional AWS**: Terraform under `infra/terraform/` — [docs/deployment.md](docs/deployment.md)
- ADR: [docs/adr/0001-modular-api-vs-microservices.md](docs/adr/0001-modular-api-vs-microservices.md)

## Prerequisites

- [.NET SDK 8.x](https://dotnet.microsoft.com/download/dotnet/8.0) (see `global.json`)
- Node.js 20+ / 22 and npm (frontend)
- Docker + Docker Compose (local dependencies; integration tests use Testcontainers)

## End-to-end reproduce workflow

```bash
# 1. Restore and build
dotnet restore EntitleX.sln
dotnet build EntitleX.sln

# 2. Start Compose infra (Postgres, Kafka, LocalStack SQS+SES, Mailpit, Jaeger, Prometheus, Grafana)
docker compose up -d

# 3. Vendor simulators (Development: migrate + seed; Kafka consumers on)
dotnet run --project src/simulators/CloudShield/CloudShield.Api     # :5081
dotnet run --project src/simulators/NetworkGuard/NetworkGuard.Api   # :5082

# 4. Control-plane API
dotnet run --project src/EntitleX.Api                               # :5080

# 5. Messaging + repair workers
dotnet run --project src/workers/EntitleX.OutboxPublisher           # metrics :9180
dotnet run --project src/workers/EntitleX.FulfillmentWorker         # metrics :9181
dotnet run --project src/workers/EntitleX.ReconciliationWorker      # metrics :9182
dotnet run --project src/workers/EntitleX.NotificationService       # metrics :9183 (optional)

# 6. Scheduled lambdas (run-once local hosts; EventBridge stubs in docs/lambdas.md)
dotnet run --project src/lambdas/EntitleX.ApprovalReminder
dotnet run --project src/lambdas/EntitleX.ExpirationNotification -- --provider CloudShield
dotnet run --project src/lambdas/EntitleX.ExpirationNotification -- --provider NetworkGuard

# 7. Frontend (proxies /api and /health to :5080)
cd frontend && npm ci && npm run dev                                # :5173

# 8. Tests
dotnet test tests/EntitleX.Domain.UnitTests
dotnet test tests/EntitleX.Application.UnitTests
dotnet test tests/EntitleX.Infrastructure.UnitTests
dotnet test tests/CloudShield.Domain.UnitTests
dotnet test tests/EntitleX.Infrastructure.IntegrationTests          # Docker for Testcontainers
dotnet test tests/CloudShield.Infrastructure.IntegrationTests
dotnet test tests/EntitleX.E2E.Tests                                # large scenarios need ENTITLEX_E2E=1
cd frontend && npm run build
```

### Optional: Terraform (AWS lab)

Local Compose is the primary path. AWS packaging is optional and costs money — read [infra/terraform/COST.md](infra/terraform/COST.md) first.

```bash
cd infra/terraform
cp terraform.tfvars.example terraform.tfvars   # non-secret values only
terraform init && terraform plan
# terraform apply   # only when you intend to spend AWS budget
```

Secrets (JWT, connection strings) go into Secrets Manager **outside** Terraform — never commit them. Details: [docs/deployment.md](docs/deployment.md).

### Auth (Development)

Mint a seed token (`Jwt:EnableDevTokenEndpoint=true` in Development):

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

Use `Authorization: Bearer <accessToken>`. Production must set a strong `Jwt__SigningKey` (≥32 chars).

### Approval bands

| Max extension days (across items) | Path |
|-----------------------------------|------|
| 1–30 | Auto-approved |
| 31–90 | Renewal Manager |
| 91–365 | Renewal Manager → Deal Desk |
| >365 | Rejected (unsupported) |

## Docker Compose ports

| Service | Ports | Notes |
|---------|-------|-------|
| Postgres | `5432` | User/pass `entitlex` / `entitlex`; DBs `entitlex_platform`, `cloudshield`, `networkguard` |
| Kafka | `9092` | KRaft |
| LocalStack | `4566` | SQS + SES |
| Mailpit | `1025` SMTP, `8025` UI | Dev email |
| Jaeger | `16686` UI, `4317`/`4318` OTLP | Tracing |
| Prometheus | `9090` | Scrapes API `/metrics` + workers `:9180–9183` |
| Grafana | `3001` | `admin` / `admin` |

App processes (not in Compose by default): API `:5080`, CloudShield `:5081`, NetworkGuard `:5082`, Vite `:5173`.

## Documentation index

| Doc | Topic |
|-----|-------|
| [docs/architecture.md](docs/architecture.md) | System architecture + Mermaid flowchart |
| [docs/system-design.md](docs/system-design.md) | Design decisions, DDD, messaging, authZ |
| [docs/database-schema.md](docs/database-schema.md) | ER diagrams (platform + provider DBs) |
| [docs/api-contracts.md](docs/api-contracts.md) | Control-plane + simulator endpoints |
| [docs/event-contracts.md](docs/event-contracts.md) | Envelope, SQS/Kafka, sequence diagram |
| [docs/saga-workflow.md](docs/saga-workflow.md) | Saga states + AllowedTransitions |
| [docs/idempotency.md](docs/idempotency.md) | Selective retry + retry lineage |
| [docs/reconciliation.md](docs/reconciliation.md) | Probe → repair decision matrix |
| [docs/failure-scenarios.md](docs/failure-scenarios.md) | F1–F10 failure catalog |
| [docs/deployment.md](docs/deployment.md) | Compose vs AWS Terraform, CI/CD, secrets |
| [docs/interview-guide.md](docs/interview-guide.md) | Senior/Staff interview questions |
| [docs/diagrams/README.md](docs/diagrams/README.md) | Mermaid diagram index |
| [docs/lambdas.md](docs/lambdas.md) | ApprovalReminder + ExpirationNotification |
| [docs/observability.md](docs/observability.md) | OTel, Prometheus, Serilog |
| [docs/performance.md](docs/performance.md) | Indexes, batching, pooling |
| [docs/implementation-progress.md](docs/implementation-progress.md) | Phase 1–12 checklist |
| [docs/adr/0001-modular-api-vs-microservices.md](docs/adr/0001-modular-api-vs-microservices.md) | Modular API decision |

## Repository layout

```
src/
  EntitleX.{BuildingBlocks,Messaging,Domain,Application,Infrastructure,Api}/
  workers/          # OutboxPublisher, FulfillmentWorker, ReconciliationWorker, NotificationService
  simulators/       # CloudShield + NetworkGuard
  lambdas/          # ApprovalReminder + ExpirationNotification
frontend/           # Vite React + TypeScript control-plane UI
infra/
  docker/           # Postgres init, Prometheus/Grafana, container Dockerfiles
  eventbridge/      # Schedule stubs
  terraform/        # Optional AWS packaging
docs/               # Architecture, contracts, interview guide
tests/              # Unit, integration (DockerFact), E2E scaffolding
```

## License

MIT — see [LICENSE](LICENSE).

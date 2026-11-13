# EntitleX

Enterprise license-extension platform (portfolio). Orchestrates extension requests across fictional vendor systems **CloudShield** and **NetworkGuard**. No proprietary IP.

**Phase 12** completes documentation packaging, optional AWS Terraform, container images, and CI image builds on top of Phases 1–11 (domain → messaging → saga → retry → reconciliation → lambdas → frontend → observability).

## Architecture (Phase 12 complete)

- **Modular control-plane API** (`EntitleX.Api`) with Clean Architecture layers — see [docs/architecture.md](docs/architecture.md), [docs/system-design.md](docs/system-design.md)
- **DDD aggregates**: `LicenseExtensionRequest`, `ApprovalCase`, durable `LicenseExtensionSaga` (forward recovery only)
- **JWT Bearer + RBAC** (`RenewalRepresentative`, `RenewalManager`, `DealDeskManager`, `PlatformAdmin`) with optional **OIDC** (Keycloak locally; Entra/Auth0/Cognito via config)
- **Transactional outbox** → SQS fulfillment → Kafka provider commands → simulator results → saga settlement
- **Selective retry** with `LogicalOperationId` / `AttemptId` / `ParentId` lineage and DB-backed idempotency
- **Reconciliation** before blind retry (probe provider status; never POST when Unknown/NotFound)
- **Vendor simulators** with own PostgreSQL DBs and unique `AppliedLicenseOperation` claims
- **Workers**: OutboxPublisher, FulfillmentWorker, ReconciliationWorker, NotificationService
- **Lambdas** (local hosts + EventBridge): ApprovalReminder, ExpirationNotification
- **Observability**: OTel → Jaeger, Prometheus/Grafana, Serilog — [docs/observability.md](docs/observability.md)
- **Insights (optional)**: grounded Explain / reconciliation RCA with optional validated structured diagnosis via Microsoft.Extensions.AI + Ollama (`AiAssist:Enabled=false` by default; no AWS AI spend) — [docs/ai-agent.md](docs/ai-agent.md), [docs/reconciliation.md](docs/reconciliation.md)
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

# 2. Start Compose infra (Keycloak, Postgres, Kafka, LocalStack SQS+SES, Mailpit, Jaeger, Prometheus, Grafana)
docker compose up -d

# 3. Vendor simulators (Development: migrate + seed; Kafka consumers on)
# Bound to 127.0.0.1; require X-Api-Key matching Providers:*/ApiKey (lab default in appsettings).
dotnet run --project src/simulators/CloudShield/CloudShield.Api     # 127.0.0.1:5081
dotnet run --project src/simulators/NetworkGuard/NetworkGuard.Api   # 127.0.0.1:5082

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
# Rider: use shared run config "Lab Full Stack" under .run/ (Compose + API + SPA + simulators + workers)

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

### Optional: local Ollama for structured insight diagnosis

Explain / RCA always return a deterministic grounded summary (and composer `recommendedAction`). To optionally add a validated `IncidentDiagnosis` with a local model (no cloud cost):

```bash
# Install/run Ollama, then: ollama pull qwen2.5:7b   # or llama3.2
# In EntitleX.Api config or env:
# AiAssist__Enabled=true
# AiAssist__BaseUrl=http://127.0.0.1:11434
# AiAssist__Model=qwen2.5:7b
# AiAssist__Temperature=0
# ENTITLEX_AI_API_KEY=ollama   # optional; Ollama ignores auth
```

Leave `AiAssist:Enabled=false` (default) on AWS lab / CI. See [docs/ai-agent.md](docs/ai-agent.md).

### Auth

Three local/cloud modes share the same ASP.NET policies and role names.

#### 1) Keycloak (Compose — preferred local IdP)

```bash
docker compose up -d keycloak   # http://localhost:8080 (admin / admin — lab only)
# Realm `entitlex` is imported from infra/docker/keycloak/realm-entitlex.json
```

| Username | Password | Role |
|----------|----------|------|
| `renewal.rep` | `demo` | `RenewalRepresentative` |
| `renewal.manager` | `demo` | `RenewalManager` |
| `dealdesk.manager` | `demo` | `DealDeskManager` |
| `platform.admin` | `demo` | `PlatformAdmin` |

SPA PKCE client: `entitlex-spa` → API audience `entitlex-api`. Frontend defaults in `frontend/.env.development`.

API Development already enables OIDC against Keycloak (`Oidc:Authority=http://localhost:8080/realms/entitlex`).

#### 2) DevToken (Development only)

Mint a seed token when `Jwt:EnableDevTokenEndpoint=true` **and** `ASPNETCORE_ENVIRONMENT=Development` (forced off in Production).
Minting **`PlatformAdmin` also requires `Jwt:AllowDevPlatformAdmin=true`** (default `false`) — prefer Keycloak for admin labs.

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
| `PlatformAdmin` | `user-admin-001` | `platform.admin@entitlex.local` (needs `AllowDevPlatformAdmin`) |

Frontend shows the DevToken panel only when `VITE_ENABLE_DEV_TOKEN=true`.

See [docs/adr/0002-lab-vs-production-auth.md](docs/adr/0002-lab-vs-production-auth.md) for lab vs production auth posture (actors bound to JWT, resource auth, simulator API keys, metrics/CORS/HSTS).

#### 3) Cloud IdP (Entra / Auth0 / Cognito)

No code forks — set generic OIDC env vars (never commit secrets):

| Variable | Purpose |
|----------|---------|
| `Oidc__Enabled` | `true` |
| `Oidc__Authority` | Issuer URL (required in Production) |
| `Oidc__Audience` | API audience / app id URI (**required outside Development**) |
| `Oidc__ClientId` | SPA client id (documentation / SPA) |
| `Oidc__RoleClaimType` | Claim carrying roles (default `roles`) |
| `Oidc__RoleMap__{IdPRole}` | Optional map → EntitleX role name |
| `Jwt__EnableDevTokenEndpoint` | Must stay `false` in Production |
| `Jwt__AllowDevPlatformAdmin` | Dev-only; default `false` |
| `Jwt__SigningKey` | Only needed if DevToken is enabled (Development) |

Frontend cloud SPA:

| Variable | Purpose |
|----------|---------|
| `VITE_OIDC_AUTHORITY` | Same authority as API |
| `VITE_OIDC_CLIENT_ID` | Public SPA client |
| `VITE_OIDC_AUDIENCE` | Optional audience hint |
| `VITE_ENABLE_DEV_TOKEN` | `false` outside local labs |

Use `Authorization: Bearer <accessToken>` for API calls.

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
| Keycloak | `8080` | Realm `entitlex`; admin UI `admin`/`admin` (lab only) |
| Postgres | `5432` | User/pass `entitlex` / `entitlex`; DBs `entitlex_platform`, `cloudshield`, `networkguard` |
| Kafka | `9092` | KRaft |
| LocalStack | `4566` | SQS + SES |
| Mailpit | `1025` SMTP, `8025` UI | Dev email |
| Jaeger | `16686` UI, `4317`/`4318` OTLP | Tracing |
| Prometheus | `9090` | Scrapes API `/metrics` + workers `:9180–9183` |
| Grafana | `3001` | `admin` / `admin` |

App processes (not in Compose by default): API `:5080`, CloudShield `127.0.0.1:5081`, NetworkGuard `127.0.0.1:5082`, Vite `:5173`. Keep simulators on a private network in shared labs.

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
| [docs/guides/README.md](docs/guides/README.md) | Educational guides (collections → security) with EntitleX samples |
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
docs/               # Architecture, contracts, interview guide, educational guides/
tests/              # Unit, integration (DockerFact), E2E scaffolding
```

## License

MIT — see [LICENSE](LICENSE).

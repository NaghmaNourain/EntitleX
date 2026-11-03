# EntitleX implementation progress

## Phase 1 — Foundations (current)

**Status:** Complete (skeletons + local infra + docs + CI)

### Verified

| Item | Result |
|------|--------|
| `dotnet build EntitleX.sln` | Succeeds on .NET SDK 8.x (0 warnings, 0 errors) |
| Solution projects compile | Platform, workers, simulators, lambda stubs |
| Frontend `npm run build` | Succeeds (Vite React + TypeScript scaffold) |
| `docker compose config` | Validated successfully (Compose v2) |
| Live `GET /health` | EntitleX.Api `:5080`, CloudShield `:5081`, NetworkGuard `:5082` return Healthy JSON |
| ADR 0001 | Documented modular API decision |
| CI workflow | `.github/workflows/ci.yml` restores + builds on PR/push |

### Unverified in this environment

| Item | Reason |
|------|--------|
| `docker compose up` / container health | Docker engine/daemon not running in the agent environment (CLI present; config OK) |
| End-to-end Postgres init script against a live cluster | Init shell script present; not executed against a running Postgres |
| Kafka / LocalStack / Mailpit / Jaeger / Prometheus / Grafana smoke | Images declared; not started |

### Delivered in Phase 1

- Repo bootstrap: `.gitignore`, MIT `LICENSE`, `README.md`, `global.json` (SDK 8.x), `Directory.Build.props`
- Clean Architecture solution: `EntitleX.sln` with BuildingBlocks, Messaging, Domain, Application, Infrastructure, Api
- Workers (heartbeat stubs): FulfillmentWorker, OutboxPublisher, ReconciliationWorker, NotificationService
- Simulators: CloudShield + NetworkGuard (Domain/Application/Infrastructure/Api) with `GET /health`
- Lambda stubs: ApprovalReminder, ExpirationNotification
- Platform Api `GET /health`
- `frontend/` Vite React + TypeScript scaffold (no feature pages)
- `docker-compose.yml` with Postgres (multi-DB init), Kafka KRaft, LocalStack, Mailpit, Jaeger, Prometheus, Grafana + named volumes
- ADR: `docs/adr/0001-modular-api-vs-microservices.md`
- CI: restore + `dotnet build` on PR/push

### Explicitly out of scope (Phase 1)

- EF Core migrations / persistence
- Domain aggregates and real use-cases
- JWT / auth
- Kafka producers/consumers
- React feature pages
- Terraform
- Polly resilience pipelines
- Integration tests

---

## Phase 2 — Remaining (not started)

Expected next work (indicative; not implemented here):

- Domain model for license-extension requests / approvals / entitlements (selective DDD)
- EF Core DbContexts + migrations for `entitlex_platform`, `cloudshield`, `networkguard`
- Application use-cases and API endpoints beyond health
- Outbox pattern + Kafka messaging wiring
- AuthN/AuthZ (JWT) for the control plane
- Simulator vendor APIs with realistic license operations
- Frontend feature pages against the control plane
- Integration test harness against docker-compose dependencies
- Observability instrumentation (OTLP → Jaeger, metrics → Prometheus)

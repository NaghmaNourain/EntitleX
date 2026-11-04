# EntitleX implementation progress

## Phase 1 — Foundations

**Status:** Complete (see PR branch `cursor/phase-1-foundations-bc4f`)

### Verified

| Item | Result |
|------|--------|
| `dotnet build EntitleX.sln` | Succeeds on .NET SDK 8.x |
| Solution projects compile | Platform, workers, simulators, lambda stubs |
| Frontend `npm run build` | Succeeds (Vite React + TypeScript scaffold) |
| `docker compose config` | Validated successfully (Compose v2) |
| Live `GET /health` | EntitleX.Api `:5080`, CloudShield `:5081`, NetworkGuard `:5082` |
| ADR 0001 | Modular API decision documented |
| CI workflow | Restore + build on PR/push |

---

## Phase 2 — Domain model and request APIs

**Status:** Complete on branch `cursor/phase-2-domain-apis-bc4f` (based on Phase 1)

### Verified

| Item | Result |
|------|--------|
| `dotnet build EntitleX.sln` | Succeeds (0 warnings, 0 errors) |
| Domain unit tests | Passed (`EntitleX.Domain.UnitTests`) |
| Integration tests (Testcontainers) | Skipped without Docker (`DockerFact`) |
| EF migration `InitialLicenseExtension` | Generated for `entitlex_platform` |
| MediatR create/get + FluentValidation | Wired; max 1000 items |
| API endpoints | `POST/GET /api/v1/license-extension-requests` |

---

## Phase 3 — Providers + entitlement aggregation

**Status:** Complete on branch `cursor/phase-3-providers-bc4f` (based on Phase 2)

### Verified

| Item | Result |
|------|--------|
| `dotnet build EntitleX.sln` | Succeeds (0 warnings, 0 errors) |
| CloudShield / NetworkGuard domain unit tests | Passed |
| Aggregation + adapter unit tests | Passed |
| Provider integration (Testcontainers) | Skipped without Docker (`DockerFact`) |
| EF migrations | `InitialCloudShield`, `InitialNetworkGuard` |

### Delivered in Phase 3

**Simulator APIs (own DBs + deterministic seed)**

- CloudShield (`cloudshield` DB): account entitlement discovery, details (product/group/expiry), license-extension submit, operation status lookup
- NetworkGuard (`networkguard` DB): tenant license discovery, details (SKU/group/validity), extension submit, operation status lookup
- Demo seeds: `CUST-1001` appears in both providers; additional exclusive customers for partial-coverage demos
- Development: `Database:MigrateOnStartup` + `Database:SeedDemoData`

**AppliedLicenseOperation**

- Unique `LogicalOperationId` (DB unique index)
- Atomic claim + expiry update inside a single provider DB transaction
- Idempotent replay returns `AlreadyApplied` without double-extending
- No remote HTTP is performed while a DB transaction is open

**Control-plane aggregation**

- `IProviderEntitlementClient` adapters via `IHttpClientFactory`
- Polly v8 (`Microsoft.Extensions.Http.Resilience`): timeout, retry+jitter for idempotent GET clients only, circuit breaker, concurrency limiter (bulkhead)
- Separate command HttpClients **without** retry (non-idempotent POSTs must not be blindly retried)
- Normalized `NormalizedEntitlementDto`; partial aggregation when a provider fails
- Correlation IDs (`X-Correlation-ID`) + structured Serilog
- Health checks: `/health/ready`, `/health/providers` (per-provider tags)

**API**

- `GET /api/v1/entitlements/search?customerId={id}` — fan-out aggregation

---

## Phase 4 — Approvals + JWT/RBAC

**Status:** Complete on branch `cursor/phase-4-approvals-bc4f` (based on Phase 3)

### Verified

| Item | Result |
|------|--------|
| `dotnet build EntitleX.sln` | Succeeds (0 warnings, 0 errors) |
| Domain unit tests | **29 passed** (includes approval rules + unauthorized transitions) |
| Application unit tests | **4 passed** (aggregation + forbidden decision rejection) |
| Infrastructure / CloudShield unit tests | Passed |
| Integration tests (Testcontainers) | Skipped without Docker |
| EF migration `ApprovalWorkflow` | Generated for approval cases/stages/decisions |

### Delivered in Phase 4

**JWT authentication + RBAC**

- Roles: `RenewalRepresentative`, `RenewalManager`, `DealDeskManager`, `PlatformAdmin`
- Secure defaults: JWT required (fallback authorization policy); empty `Jwt:SigningKey` fails fast outside Development; HTTPS metadata required outside Development
- Development token minting: `POST /api/v1/dev/tokens` with `{ "role": "RenewalManager" }` (disabled unless `Jwt:EnableDevTokenEndpoint` + Development)
- Policies: create requests (Rep/Admin), decide approvals (RM/DealDesk/Admin), authenticated reads

**Approval rules (max extension days across items)**

| Days | Outcome |
|------|---------|
| 1–30 | Auto-approve (sets `ApprovedScopeHash`, bumps `ApprovalVersion`) |
| 31–90 | Renewal Manager |
| 91–365 | Renewal Manager → Deal Desk (sequential) |
| >365 | Unsupported — request rejected |

**Approval APIs**

- Create request also opens an approval case in the same transaction
- `POST /api/v1/license-extension-requests/{id}/approval/start` — start if not already opened
- `GET /api/v1/license-extension-requests/{id}/approval` — case, stages, decisions
- `POST /api/v1/license-extension-requests/{id}/approval/decisions` — `{ "outcome": "Approved"|"Rejected", "comment": "..." }`
- Wrong-role decisions → HTTP 403 (`UnauthorizedApprovalTransitionException` / `ForbiddenException`)
- Decisions persist actor user/role/display name, comment, correlation id, timestamps

**Notifications**

- Emails contain **app deep links** (`Email:AppBaseUrl/requests/{id}`) — never email approve buttons
- Default local path: SMTP → Mailpit (`localhost:1025`); optional `Email:Provider=Ses` → LocalStack SES (`:4566`, Compose enables `ses`)
- API sends on start / stage-ready / decision; NotificationService probes the same path at startup

### Unverified in this environment

| Item | Reason |
|------|--------|
| Live Mailpit / LocalStack SES delivery | Docker engine/CLI not available |
| End-to-end JWT + approval against Compose Postgres | Docker not available |
| Testcontainers persistence of approval aggregates | Docker not available; tests auto-skip |

### Explicitly out of scope (Phase 4)

- Outbox / SQS / Kafka (Phase 5+)
- Saga / selective retry factories (Phase 6–7)
- React approval UI (Phase 10)

---

## Phase 5 — Outbox / SQS / Kafka fulfillment

**Status:** Complete on branch `cursor/phase-5-messaging-bc4f` (based on Phase 4)

### Verified

| Item | Result |
|------|--------|
| `dotnet build EntitleX.sln` | Succeeds (0 warnings, 0 errors) |
| Domain / application / infrastructure unit tests | Passed (includes fulfillment transitions, outbox enqueue, envelope round-trip, idempotent approval→outbox) |
| Integration tests (Testcontainers) | Skip without Docker (`DockerFact`) — cover outbox txn persist, SKIP LOCKED claim, consumer idempotency |
| EF migration `OutboxMessaging` | `outbox_messages`, `processed_messages`, `fulfillment_item_results` |
| `docker compose config` | Not re-validated here (no Docker CLI); Compose updated with SQS init + Kafka health |

### Delivered in Phase 5

**Transactional outbox**

- `OutboxMessage` persisted in the same Postgres unit of work as final approval
- Auto-approve (create) and final human approval both enqueue `entitlex.fulfillment.command` and transition request → `InFulfillment`
- Envelope fields: schema version, message/correlation/causation/request/logical-operation ids

**OutboxPublisher worker**

- `FOR UPDATE SKIP LOCKED` batch claim (also reclaims stale `Processing` locks)
- Publishes to SQS or Kafka per destination kind
- Exponential retry + poison after max attempts
- `IOutboxMetrics` logging hooks
- Graceful shutdown via host cancellation

**Fulfillment pipeline**

- SQS `entitlex-fulfillment` (+ DLQ `entitlex-fulfillment-dlq`, visibility timeout, maxReceiveCount 5)
- FulfillmentWorker: idempotent SQS consumer → Kafka provider commands (partition key = entitlement id)
- CloudShield / NetworkGuard hosted Kafka consumers apply extensions and emit completion/failure to `entitlex.provider.results`
- Platform Kafka results consumer records outcomes idempotently and settles request status when all items report

**Local infrastructure**

- Compose: LocalStack SQS init script, Kafka auto-create topics + healthcheck
- Contracts notes: `docs/event-contracts.md`

### Unverified in this environment

| Item | Reason |
|------|--------|
| Live LocalStack SQS publish/consume | No Docker |
| Live Kafka end-to-end (worker → simulators → results) | No Docker |
| Testcontainers outbox / SKIP LOCKED / idempotency tests | Docker not available; tests auto-skip |
| `docker compose config` / compose up | No Docker CLI |

### Explicitly out of scope (Phase 5)

- Full saga state machine (Phase 6)
- Selective retry factories (Phase 7)
- Reconciliation worker (Phase 8)
- React UI (Phase 10)

---

## Phase 6 — Durable saga / forward recovery

**Status:** Complete on branch `cursor/phase-6-saga-bc4f` (based on Phase 5)

### Verified

| Item | Result |
|------|--------|
| `dotnet build EntitleX.sln` | Succeeds (0 warnings, 0 errors) |
| Domain unit tests | Include saga transition graph, approval ordering, timeout hooks, status aggregation under partial failure |
| Application unit tests | Approval decision → saga dispatch path |
| EF migration `SagaOrchestration` | `license_extension_sagas`, `saga_transitions`, `saga_item_outcomes` |
| Docs | `docs/saga-workflow.md` (forward recovery rationale + `InFulfillment` mapping) |

### Delivered in Phase 6

**Centralized saga (`LicenseExtensionSaga`)**

- States: `Requested`, `PendingRenewalManagerApproval`, `PendingDealDeskApproval`, `Approved`, `QueuedForFulfillment`, `Processing`, `PartiallyCompleted`, `Completed`, `Failed`, `ReconciliationRequired`, `Escalated`, `Rejected`
- Request status `InFulfillment` maps to saga `QueuedForFulfillment` + `Processing` (documented; no duplicate saga enum value)
- Persisted transition history + per-item outcomes
- Validated transition graph; RM → Deal Desk ordering enforced
- Dispatch fulfillment via existing outbox; FulfillmentWorker marks `Processing`; results consumer updates outcomes and aggregates
- Timeout hooks (`ApprovalPending` → Escalated; `FulfillmentProcessing` → ReconciliationRequired)
- **Forward recovery only** — successful extensions are never compensated

**API**

- `GET /api/v1/license-extension-requests/{id}/saga`

### Unverified in this environment

| Item | Reason |
|------|--------|
| Live end-to-end saga against Compose Postgres/SQS/Kafka | No Docker |
| Testcontainers saga persistence | Docker not available; tests auto-skip |
| Lambda-scheduled timeout firing | Phase 9 |

### Explicitly out of scope (Phase 6)

- Selective retry factories (Phase 7)
- Full reconciliation worker repair loop (Phase 8)
- Approval-reminder / expiration Lambdas (Phase 9)
- React UI (Phase 10)

---

## Phase 7 — Selective retry / idempotency

**Status:** Complete on branch `cursor/phase-7-retries-bc4f` (based on Phase 6)

### Verified

| Item | Result |
|------|--------|
| `dotnet build EntitleX.sln` | Succeeds (0 warnings, 0 errors) |
| Domain unit tests | Include failed-item selection, selective-retry factory, approval reuse, multi-generation `ParentId` chains |
| Application unit tests | Include approval reuse, lost-response idempotency, dual concurrent retry calls, reconcile-uncertain gate, representative authz |
| Infrastructure concurrency tests | `RetryClaimConcurrencyTests` (Testcontainers / `DockerFact` — skip without Docker) |
| EF migration `SelectiveRetryIdempotency` | `api_idempotency_records`, `retry_item_claims`, `retry_history`, request `notes` |
| Docs | `docs/idempotency.md` |

### Delivered in Phase 7

**API**

- `POST /api/v1/license-extensions/{requestId}/retry-failed`
- AuthZ: `RenewalRepresentative` / `PlatformAdmin` (`CanCreateRequests`)
- Required `Idempotency-Key` (header or body)
- Optional notes + LogicalOperationId filter
- Returns new request id; lost-response replay returns prior retry

**Domain / application**

- `LicenseExtensionRequest.CreateSelectiveRetry` — copy failed items only; preserve `LogicalOperationId`; new `AttemptId`; `ParentId` lineage; notes + `RetryHistory`
- `FailedItemSelector` + `ApprovalReuseEvaluator` (`ApprovedScopeHash` still valid → auto-approve reuse)
- `StubReconciliationGate` (`IReconciliationGate`) blocks uncertain / `ReconciliationRequired` (Phase 8 owns full recon)
- Short Postgres unit of work: claims + request + approval + saga + outbox (no Kafka/SQS/HTTP inside the txn)
- Async fulfillment via existing outbox dispatch after approval reuse

**Idempotency**

- `api_idempotency_records` — unique key → prior retry id
- `retry_item_claims` — unique `(source_request_id, logical_operation_id)` prevents overlapping concurrent retries
- Multi-generation chains (retry of a retry) supported

### Unverified in this environment

| Item | Reason |
|------|--------|
| Live Postgres unique-index races under dual HTTP callers | No Docker |
| Testcontainers `RetryClaimConcurrencyTests` | Docker not available; tests auto-skip |
| End-to-end retry → outbox → SQS → Kafka | No Docker |

### Explicitly out of scope (Phase 7)

- Full reconciliation repair loop (Phase 8)
- Approval-reminder / expiration Lambdas (Phase 9)
- React UI (Phase 10)

---

## Phase 8 — Reconciliation / failure repair

**Status:** Complete on branch `cursor/phase-8-reconciliation-bc4f` (based on Phase 7)

### Verified

| Item | Result |
|------|--------|
| `dotnet build EntitleX.sln` | Succeeds (0 warnings, 0 errors) |
| Domain unit tests | Include corrective repair (Uncertain/Failed → Succeeded), keep-unknown |
| Application unit tests | Reconciliation service failure-injection, concurrent LogicalOperationId idempotency, gate mismatch checks |
| EF migration `ReconciliationAudit` | `audit_events` |
| Docs | `docs/reconciliation.md`, `docs/failure-scenarios.md` |

### Delivered in Phase 8

**ReconciliationWorker**

- Applies elapsed saga timeouts, then scans `ReconciliationRequired` / Uncertain / stale Pending
- Probes CloudShield / NetworkGuard `AppliedLicenseOperation` by `LogicalOperationId` (idempotent GET)
- Confirmed Applied → repair local Succeeded + backfill fulfillment ledger + audit
- Confirmed Failed → mark Failed (retry-eligible); never demote local Succeeded
- NotFound / Unknown → keep `ReconciliationRequired` / Uncertain — **no blind retry**

**Admin API**

- `POST /api/v1/admin/reconciliation/requests/{requestId}` — `PlatformAdmin` only

**Gate**

- Production `ReconciliationGate` replaces Phase 7 stub registration; also blocks local↔saga ledger mismatches

### Unverified in this environment

| Item | Reason |
|------|--------|
| Live worker → simulator recon against Compose | No Docker |
| Testcontainers provider GET / dual-worker races | Docker not available |
| End-to-end timeout → recon → selective retry | No Docker |

### Explicitly out of scope (Phase 8)

- Approval-reminder / expiration Lambdas (Phase 9)
- React UI (Phase 10)
- Observability dashboards (Phase 11)

---

## Phase 9 — Approval reminder + expiration Lambdas

**Status:** Complete on branch `cursor/phase-9-lambdas-bc4f` (based on Phase 8)

### Verified

| Item | Result |
|------|--------|
| `dotnet build EntitleX.sln` | Succeeds (0 warnings, 0 errors) |
| Domain unit tests | **66 passed** (includes reminder policy + configured terminal-state idempotency) |
| Application unit tests | **25 passed** (includes reminder/escalation idempotency + expiry window dedupe + per-provider thresholds) |
| Infrastructure / CloudShield unit tests | **3 + 3 passed** |
| EF migration `ScheduledNotifications` | `approval_reminder_records`, `expiration_notification_records` |
| Docs | `docs/lambdas.md`, `infra/eventbridge/schedules.stub.json` |

### Delivered in Phase 9

**ApprovalReminder Lambda / local host**

- Pending approval stage ≥ `ApprovalReminder:PendingThresholdDays` (default 7) → email responsible role mailbox (deep link)
- Persist reminder in `approval_reminder_records` (unique window / case+stage) — idempotent
- After `EscalationDelayHours` (default 24) apply **`ApprovalReminder:TerminalSagaState`** (default `Escalated`, configurable — not hardcoded in the job)
- SES or Mailpit via shared `Email:*` wiring (`AddInfrastructureForLambdas`)
- Local: `dotnet run --project src/lambdas/EntitleX.ApprovalReminder`

**ExpirationNotification Lambda / local host**

- CloudShield cadence 24h / NetworkGuard 7d (`--provider` filter for schedule stubs)
- Configurable warning thresholds per provider
- Emails Renewal Representatives with customer, product, entitlement ref, expiry, portal link
- Dedupe via `expiration_notification_records.window_key` (day or ISO-week bucket)
- Local: `dotnet run --project src/lambdas/EntitleX.ExpirationNotification -- --provider CloudShield`

**EventBridge**

- Schedule stubs documented in `docs/lambdas.md` and `infra/eventbridge/schedules.stub.json` (Terraform packaging → Phase 12)

### Unverified in this environment

| Item | Reason |
|------|--------|
| Live Mailpit / LocalStack SES delivery from lambdas | No Docker |
| Live provider discovery for expiry against Compose simulators | No Docker |
| EventBridge → Lambda invoke in AWS | Stub only until Phase 12 |

### Explicitly out of scope (Phase 9)

- React UI (Phase 10)
- Observability dashboards / perf (Phase 11)
- Terraform Lambda packaging + IAM (Phase 12)

---

## Phase 10 — React frontend

**Status:** Complete on branch `cursor/phase-10-frontend-bc4f` (based on Phase 9)

### Verified

| Item | Result |
|------|--------|
| Frontend `npm run build` | Succeeds (`tsc -b` + Vite production bundle) |
| `dotnet build EntitleX.sln` | Succeeds (0 warnings, 0 errors) |
| Domain / Application unit tests | **66 + 25 passed** |
| Stack | React 19 + TypeScript + Vite + TanStack Query + React Router + MUI |

### Delivered in Phase 10

**Pages (15)**

1. Login (dev JWT via `POST /api/v1/dev/tokens`)
2. Dashboard
3. Customer entitlement search (pagination + provider filter + bulk select)
4. Unified entitlement details
5. Create extension request
6. Bulk entitlement selection
7. Extension request details (+ approval summary)
8. Renewal Manager approval queue
9. Deal Desk approval queue
10. Fulfillment progress (saga poll)
11. Failed entitlement details
12. Retry Failed Extensions (explicit **will retry** vs **untouched successes**)
13. Request lineage / retry history
14. Audit history
15. Operational monitoring dashboard

**Frontend quality**

- Typed API client with Bearer JWT, `X-Correlation-ID`, `Idempotency-Key`
- Loading / validation / ProblemDetails errors / pagination / filtering
- Role-gated nav + route guards; keyboard-focusable controls; `aria-*` on busy/error regions
- Vite proxy: `/api` + `/health` → `http://localhost:5080`

**Thin BFF reads added for UI (existing stores/repos)**

- `GET /api/v1/approvals/pending?role=&take=&skip=`
- `GET /api/v1/audit/requests/{requestId}`
- `GET /api/v1/admin/ops/summary`
- Dev CORS for `localhost:5173`

### Unverified in this environment

| Item | Reason |
|------|--------|
| Live UI ↔ API against Compose Postgres/simulators | No Docker; API not run end-to-end in this agent |
| Visual a11y audit / screen-reader pass | Manual tooling not run |
| Production JWT / CORS hardening | Dev-oriented wiring only |

### Explicitly out of scope (Phase 10)

- Observability dashboards / perf (Phase 11)
- Terraform / CI pack / docs polish (Phase 12)

---

## Phase 11 — Observability and performance (current)

**Status:** Complete on branch `cursor/phase-11-observability-bc4f` (based on Phase 10)

### Verified

| Item | Result |
|------|--------|
| `dotnet build EntitleX.sln` | Succeeds (0 warnings, 0 errors) |
| Domain / Application unit tests | Passed |
| Compose YAML / Grafana JSON | Parsed successfully (`docker` binary unavailable in agent) |

### Delivered in Phase 11

**OpenTelemetry**

- Shared helpers in `EntitleX.BuildingBlocks/Observability` (`ActivitySource`s, meters, host DI)
- W3C trace context; OTLP export to Jaeger (`:4317`)
- Instrumentation: ASP.NET, HttpClient, EF Core, SQS/Kafka produce/consume, saga transitions, outbox publish, reconciliation, provider latency
- Traceparent attributes/headers on SQS/Kafka messages

**Prometheus + Grafana**

- `/metrics` on API + simulators; worker HttpListener ports `9180–9183`
- Compose Prometheus scrapes `host.docker.internal` targets
- Grafana datasource + **EntitleX Overview** dashboard (request duration, approval latency, fulfillment outcomes, retries, recon/outbox backlog, SQS depth, Kafka lag hook, provider latency, circuit breaker, saga transitions)
- **No high-cardinality ID labels**

**Serilog**

- Structured console + Service/environment/machine enrichers on API, workers, simulators, lambdas
- CloudWatch notes in `docs/observability.md`

**Performance**

- Batch inserts for ≤1000 items (`MaxBatchSize(100)` + AutoDetectChanges off on attach)
- Indexes: audit keyset, saga state+updated, outbox status+locked
- Keyset pagination for audit (+ optional pending-approvals cursors)
- `AsNoTracking` on saga GET / existence checks; connection pooling notes in `docs/performance.md`

### Docs

- `docs/observability.md`, `docs/performance.md`

### Unverified in this environment

| Item | Reason |
|------|--------|
| Live Jaeger/Prometheus/Grafana scrape against running hosts | Compose may be unavailable; exporters wired but not soak-tested |
| EMF / CloudWatch sink in AWS | Documented only |
| True Kafka consumer-group lag (admin client) | Best-effort watermark hook only |

### Explicitly out of scope (Phase 11)

- Terraform / CI pack / docs polish (Phase 12)

---

## Phase 12 — Terraform, CI, documentation pack (final)

**Status:** Complete on branch `cursor/phase-12-delivery-bc4f` (based on Phase 11 / PR #11)

### Verified

| Item | Result |
|------|--------|
| `dotnet build EntitleX.sln` | Succeeds (0 warnings, 0 errors) |
| Domain / Application / Infrastructure / CloudShield unit tests | Passed |
| E2E offline invariant tests (`EntitleX.E2E.Tests`) | Passed (live 1000-item harness intentionally skipped) |
| Docs pack | Present under `docs/` + root README reproduce workflow |
| Terraform modules | Present under `infra/terraform/` (fmt-checked in CI) |
| Dockerfiles | Present under `infra/docker/images/` |
| CI workflows | Build/unit/integration/E2E + static analysis + Docker image builds; optional `deploy-aws.yml` |

### Delivered in Phase 12

**Terraform (optional AWS, cost-documented)**

- Modules: VPC, ECS Fargate, RDS PostgreSQL, SQS (+DLQ), SNS/SES, Lambda, EventBridge, CloudWatch, IAM, Secrets Manager, S3 audit exports
- Credentials never hardcoded — Secrets Manager + task roles; see `infra/terraform/COST.md` + `README.md`
- Defaults keep spend low (`enable_ecs_services=false`, single-AZ `db.t4g.micro`)

**GitHub Actions**

- `.github/workflows/ci.yml` — restore, build, unit tests, integration tests (DockerFact skip), E2E scaffolding tests, static analysis (`dotnet format` soft gate + analyzers + `terraform fmt`), Docker image builds (API, worker, simulator, frontend)
- `.github/workflows/deploy-aws.yml` — `workflow_dispatch` only, OIDC, plan/apply gated by GitHub Environment

**Documentation pack (Mermaid diagrams)**

- `README.md` end-to-end reproduce workflow
- `docs/architecture.md`, `system-design.md`, `database-schema.md`, `api-contracts.md`
- Updated: `event-contracts.md`, `saga-workflow.md`, `idempotency.md`, `reconciliation.md`, `failure-scenarios.md`
- New: `deployment.md`, `interview-guide.md`, `docs/diagrams/README.md`
- Diagrams: system architecture, request sequence, approval workflow, saga state machine, ER, retry lineage, outbox, reconciliation, failure recovery

**E2E scaffolding**

- `tests/EntitleX.E2E.Tests` — 1000 / 970 / 30 / retry lineage scenario documented + offline invariants
- Live harness skipped until `ENTITLEX_E2E=1` + Compose wiring (see scenario markdown)

### Unverified in this environment

| Item | Reason |
|------|--------|
| `terraform apply` against a real AWS account | Optional / cost-bearing; not applied |
| Docker image builds on this agent | Docker engine not available locally (CI job covers it) |
| Live 1000-item E2E against Compose | No Docker; harness scaffolded + documented |
| SES / EventBridge invoke in AWS | Terraform + stubs only |
| Managed Kafka/MSK | Intentionally out of Terraform; Compose Kafka for local |

### Explicitly out of scope (Phase 12)

- None remaining on the original 12-phase roadmap — **final planned phase complete**

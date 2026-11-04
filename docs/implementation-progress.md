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

## Phase 5 — Outbox / SQS / Kafka fulfillment (current)

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

## Remaining (roadmap only — not started)

6. Saga · 7. Selective retry · 8. Reconciliation · 9. Lambdas · 10. React UI · 11. Observability/perf · 12. Terraform/CI/docs pack.

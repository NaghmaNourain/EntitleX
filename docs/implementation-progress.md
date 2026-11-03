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
| Domain unit tests | **11 passed** (`EntitleX.Domain.UnitTests`) |
| Integration tests (Testcontainers) | **2 skipped** without Docker (`DockerFact`) |
| EF migration `InitialLicenseExtension` | Generated for `entitlex_platform` |
| MediatR create/get + FluentValidation | Wired; max 1000 items |
| API endpoints | `POST/GET /api/v1/license-extension-requests` |

---

## Phase 3 — Providers + entitlement aggregation (current)

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

**Tests**

- `EntitleX.Application.UnitTests` — partial aggregation
- `EntitleX.Infrastructure.UnitTests` — adapter normalization (CloudShield + NetworkGuard)
- `CloudShield.Domain.UnitTests` — expiry / AppliedLicenseOperation invariants
- `CloudShield.Infrastructure.IntegrationTests` — atomic claim idempotency (Testcontainers; skips without Docker)

### Unverified in this environment

| Item | Reason |
|------|--------|
| Live simulator ↔ API aggregation against Compose | Docker engine/CLI not available |
| Testcontainers PostgreSQL for claim/idempotency | Docker not available; tests auto-skip |
| Circuit-breaker trip under sustained provider failure | Unit coverage of adapters/aggregation only |

### Explicitly out of scope (Phase 3)

- Approvals / JWT (Phase 4)
- Outbox / SQS / Kafka (Phase 5+)
- Saga / selective retry factories (Phase 6–7)
- React entitlement search UI (Phase 10)

---

## Remaining (roadmap only — not started)

4. Approvals + auth · 5. Outbox/SQS/Kafka · 6. Saga · 7. Selective retry · 8. Reconciliation · 9. Lambdas · 10. React UI · 11. Observability/perf · 12. Terraform/CI/docs pack.

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

## Phase 2 — Domain model and request APIs (current)

**Status:** Complete on branch `cursor/phase-2-domain-apis-bc4f` (based on Phase 1)

### Verified

| Item | Result |
|------|--------|
| `dotnet build EntitleX.sln` | Succeeds (0 warnings, 0 errors) |
| Domain unit tests | **11 passed** (`EntitleX.Domain.UnitTests`) — expiry baseline stub + aggregate invariants |
| Integration tests (Testcontainers) | **2 skipped** — Docker engine/CLI not available in this agent environment |
| EF migration `InitialLicenseExtension` | Generated for `entitlex_platform` (`license_extension_requests` + items) |
| MediatR create/get + FluentValidation | Wired; max 1000 items; required fields |
| API endpoints | `POST/GET /api/v1/license-extension-requests` with ProblemDetails + OpenAPI/Swagger |

### Unverified in this environment

| Item | Reason |
|------|--------|
| Testcontainers PostgreSQL persist/load | Docker not installed/available; tests auto-skip via `DockerFact` |
| Live API against Compose Postgres | Requires `docker compose up` + migrate |
| Concurrent update conflict on `row_version` | Unit coverage only; needs live DB |

### Delivered in Phase 2

**Domain (rich aggregate, not anemic)**

- `LicenseExtensionRequest` + `LicenseExtensionRequestItem`
- Invariants: ≤1000 items; `ParentId` must be null for fresh create; status transition graph stubs (`Draft` → `Submitted` for create); `ApprovalVersion` / `ApprovedScopeHash` fields + approval stub methods
- `ExpiryBaselineRules` stub: requested expiry must be strictly after current expiry baseline
- Strongly typed IDs in BuildingBlocks: `RequestId`, `RequestItemId`, `LogicalOperationId`, `AttemptId`, `EntitlementId`

**Infrastructure**

- EF Core 8 + Npgsql mappings for `entitlex_platform`
- Unique index on `ExtensionNumber`; FKs (items → request, optional parent self-FK); `row_version` concurrency token; supporting indexes
- Migration: `src/EntitleX.Infrastructure/Persistence/Migrations/…_InitialLicenseExtension.cs`
- Repository + `IUnitOfWork` via `EntitleXDbContext`

**Application / API**

- `CreateLicenseExtensionRequestCommand` / `GetLicenseExtensionRequestQuery` (MediatR)
- FluentValidation (item cap, required fields, provider enum, expiry ordering)
- Validation pipeline behavior → ProblemDetails (`400` / `404` / `409` / `422`)
- Swagger/OpenAPI in Development
- Connection string `EntitleXPlatform`; optional `Database:MigrateOnStartup` (enabled in Development)

**Tests**

- `tests/EntitleX.Domain.UnitTests`
- `tests/EntitleX.Infrastructure.IntegrationTests` (Testcontainers PostgreSQL; skips without Docker)

### Explicitly out of scope (Phase 2)

- Approvals workflow / JWT auth (Phase 4)
- Provider aggregation / simulator vendor APIs (Phase 3)
- Outbox / SQS / Kafka (Phase 5+)
- Saga / selective retry factories beyond ParentId null invariant (Phase 6–7)
- React feature pages, Terraform, cloudshield/networkguard EF schemas

---

## Remaining (roadmap only — not started)

3. Providers + aggregation · 4. Approvals + auth · 5. Outbox/SQS/Kafka · 6. Saga · 7. Selective retry · 8. Reconciliation · 9. Lambdas · 10. React UI · 11. Observability/perf · 12. Terraform/CI/docs pack.

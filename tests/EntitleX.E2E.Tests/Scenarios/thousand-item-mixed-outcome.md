# E2E scenario: 1000 items / 970 success / 30 fail / retry lineage

## Goal

Prove forward recovery + selective retry under a realistic bulk load:

1. Create one `LicenseExtensionRequest` with **1000** items (aggregate max).
2. Drive fulfillment so **970** items succeed and **30** fail.
3. Assert saga `PartiallyCompleted` / request `PartiallyFulfilled`.
4. Call `POST /api/v1/license-extensions/{id}/retry-failed` with `Idempotency-Key`.
5. Assert:
   - New request `ParentId` = source request id
   - Exactly **30** items copied
   - Each item keeps `LogicalOperationId`, new `AttemptId`
   - Successful siblings are **not** copied
6. After retry fulfillment, assert terminal `Completed` / `Fulfilled` and unique provider `applied_license_operations`.

## Prerequisites

```bash
docker compose up -d
# Run API, CloudShield, NetworkGuard, OutboxPublisher, FulfillmentWorker
export ENTITLEX_E2E=1
dotnet test tests/EntitleX.E2E.Tests --filter Full_stack
```

Without `ENTITLEX_E2E=1` (or without Docker), the live `[E2EFact]` skips; offline invariant tests still run.

## Failure injection (required for the 30 fails)

Simulators currently succeed on valid entitlements. To force 30 failures without changing production paths, pick one:

| Approach | Notes |
|----------|-------|
| **A. Pre-delete / missing entitlement keys** | Include 30 item entitlement ids that do not exist in provider DBs so apply fails. |
| **B. Env flag (future)** | e.g. `CloudShield:FailureInjection:LogicalOperationIds` list — not required for Phase 12 scaffolding. |
| **C. Kafka drop** | Drop 30 completion events → Uncertain → recon; then force Failed — heavier, exercises Phase 8. |

Recommended for the portfolio demo: **Approach A** (deterministic missing keys).

## Auto-approve band

Use extension days **≤ 30** so the request auto-approves and enters fulfillment without human approval stages.

## Observability checks (optional)

- Jaeger: single correlation id across API → outbox → SQS → Kafka → simulators → results
- Grafana EntitleX Overview: fulfillment success/fail counters ≈ 970/30
- Audit trail: create, fulfill outcomes, retry claim, retry fulfill

## Status in Phase 12

| Layer | Status |
|-------|--------|
| Offline invariant tests | Implemented (always run) |
| Live Compose harness | Scaffolded (`[E2EFact]` + this doc); **unverified** without Docker/E2E flag |

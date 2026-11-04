# EntitleX system design

Design decisions that shape the Phase 1–12 codebase. Interview-oriented detail lives in [interview-guide.md](interview-guide.md); ADR: [adr/0001-modular-api-vs-microservices.md](adr/0001-modular-api-vs-microservices.md).

## Goals

1. Orchestrate multi-vendor license extensions with **strong local consistency** on the control plane and **at-least-once** async fulfillment.
2. Never revoke a successful extension when siblings fail (**forward recovery**).
3. Make retries and repairs **safe**: selective copy of failed items only; reconcile uncertain outcomes before POST.
4. Stay demographically runnable: one Compose stack + `dotnet run` processes; optional AWS packaging.

## Clean Architecture + selective DDD

| Layer | Projects | Responsibility |
|-------|----------|----------------|
| Domain | `EntitleX.Domain` | Aggregates, invariants, saga transitions — no I/O |
| Application | `EntitleX.Application` | MediatR commands/queries, ports, orchestration services |
| Infrastructure | `EntitleX.Infrastructure` | EF Core, outbox, SQS/Kafka, provider HTTP, email, OTel |
| Api | `EntitleX.Api` | Minimal APIs, JWT policies, Swagger (Dev) |
| BuildingBlocks / Messaging | shared IDs, envelopes, meters | Cross-cutting contracts |

**Selective DDD:** rich aggregates where invariants matter (`LicenseExtensionRequest`, `ApprovalCase`, `LicenseExtensionSaga`); infrastructure entities (`OutboxMessage`, `ProcessedMessage`, audit/notification records) stay thinner.

Simulators mirror the same layering with their own DbContexts (`cloudshield`, `networkguard`).

## Modular monolith vs microservices

**Chosen:** one modular control-plane API + **separate worker and simulator processes**.

- Modules share a process and the `entitlex_platform` database; boundaries are projects/folders + reviews.
- Workers scale/restart independently for outbox, fulfillment, reconciliation, notifications.
- Simulators are separate hosts so provider failure isolation is realistic.

Do **not** split the control plane into many deployables until ownership, scale, or blast radius demands it. See ADR 0001.

## Forward recovery (not compensation)

License extensions **lengthen entitlement time**. Once a provider has applied an extension:

- The customer already holds a later expiry.
- A compensating “shorten expiry” is not a safe product inverse.
- Sibling failures must not punish successes.

Saga behaviour:

1. Persist per-item outcomes (`Pending` / `Succeeded` / `Failed` / `Uncertain`).
2. Mixed results → `PartiallyCompleted` / request `PartiallyFulfilled`.
3. Succeeded `LogicalOperationId`s are never demoted to Failed.
4. Selective retry copies **only failed** items, preserves `LogicalOperationId`, mints new `AttemptId`, chains via `ParentId`.

## Durable saga

`LicenseExtensionSaga` is the fine-grained orchestrator; request status remains coarser (`InFulfillment` maps to saga `QueuedForFulfillment` / `Processing`). Allowed transitions are enforced in domain code — see [saga-workflow.md](saga-workflow.md).

Timeouts:

| Kind | Effect |
|------|--------|
| `ApprovalPending` | → `Escalated` (never auto-approve) |
| `FulfillmentProcessing` | Pending items → `Uncertain`; saga → `ReconciliationRequired` |

## Messaging topology

```
Final approval (same PG txn)
  → outbox_messages
  → OutboxPublisher → SQS entitlex-fulfillment
  → FulfillmentWorker → Kafka provider command topics (key = entitlement id)
  → Simulator consumers → AppliedLicenseOperation
  → Kafka entitlex.provider.results
  → ProviderResultProcessor → saga aggregation
```

- **At-least-once** on SQS and Kafka; consumers use `processed_messages` `(consumer_name, idempotency_key)`.
- Outbox claim: `FOR UPDATE SKIP LOCKED`; poison after `MaxAttempts`.
- Contracts: [event-contracts.md](event-contracts.md).

## Idempotency model

| Concern | Mechanism |
|---------|-----------|
| HTTP selective retry | `api_idempotency_records` + required `Idempotency-Key` |
| Concurrent retry of same items | `retry_item_claims` unique `(source_request_id, logical_operation_id)` |
| Provider apply | Unique `logical_operation_id` on `applied_license_operations` |
| Consumer dedupe | `processed_messages` |
| Stable intent across retries | `LogicalOperationId` preserved; new `AttemptId` |

Gate: `IReconciliationGate` blocks retry while `ReconciliationRequired` / `Uncertain` / ledger mismatch. Details: [idempotency.md](idempotency.md), [reconciliation.md](reconciliation.md).

## Provider HTTP resilience (Polly)

Named HttpClients in `ProviderHttpClientRegistration`:

| Client | Retry | Rationale |
|--------|-------|-----------|
| Idempotent GET (discovery / operation status) | Timeout + exponential retry + circuit breaker + bulkhead | Safe to retry |
| Command POST (license extension) | Timeout + circuit breaker + bulkhead; **no retry** | Non-idempotent POSTs must not be blindly retried |

Unknown provider effects are resolved by reconciliation GET probes, not by re-POSTing.

## AuthZ

JWT Bearer with fallback authenticated policy. Policies (`AuthorizationPolicies`):

| Policy | Roles |
|--------|-------|
| `CanCreateRequests` | `RenewalRepresentative`, `PlatformAdmin` |
| `CanDecideApprovals` | `RenewalManager`, `DealDeskManager`, `PlatformAdmin` |
| `CanReadApprovals` | any authenticated user |
| `CanManageReconciliation` | `PlatformAdmin` |

Handlers re-check roles for sensitive paths (retry, manual reconcile) so MediatR cannot bypass endpoint policy.

## Approval bands

| Max extension days | Path |
|--------------------|------|
| 1–30 | Auto-approve |
| 31–90 | Renewal Manager |
| 91–365 | Renewal Manager → Deal Desk |
| >365 | Rejected (unsupported) |

`ApprovalVersion` / `ApprovedScopeHash` bind decisions to the approved item set; selective retry reuses approval when the hash remains valid for failed items.

## Observability & performance

- OTel traces → Jaeger; Prometheus meters with **low-cardinality** labels only ([observability.md](observability.md)).
- Batch inserts, keyset pagination, connection pooling ([performance.md](performance.md)).

## Deployment shape

- **Primary:** Docker Compose deps + process hosts (`dotnet run` / Vite).
- **Optional AWS:** Terraform (VPC, RDS, ECS Fargate, SQS, Lambda/EventBridge, Secrets Manager, CloudWatch) — [deployment.md](deployment.md), [infra/terraform/COST.md](../infra/terraform/COST.md).

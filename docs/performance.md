# EntitleX performance notes (Phase 11)

## Batch inserts (≤1000 items)

- Aggregate `LicenseExtensionRequest` may carry up to **1000** items.
- Repository disables `AutoDetectChanges` while attaching the graph, then `SaveChanges` uses Npgsql **`MaxBatchSize(100)`** so inserts are batched (~10 round-trips for 1000 rows) instead of one statement per item.
- Items remain ownership of the aggregate (cascade insert); no separate bulk-COPY path in this phase.

## Indexes (and isolation tradeoffs)

| Index | Purpose | Tradeoff |
|-------|---------|----------|
| `ix_outbox_messages_status_available` | Outbox claim (`FOR UPDATE SKIP LOCKED`) | Write amplification on status transitions; essential for publisher concurrency |
| `ix_outbox_messages_status_locked` | Stale-lock reclaim | Extra index maintenance on lock updates |
| `ux_*_logical_operation_id` (outcomes / applied ops) | Idempotent fulfillment | Unique constraint isolation — concurrent inserts race to one winner |
| `ix_license_extension_sagas_state_updated` | Recon / ops backlog scans | Helps range scans; slightly costlier saga updates |
| `ix_audit_events_request_occurred_id` | Audit **keyset** pagination | Supports `(occurred_at, id)` cursors; avoid `OFFSET` on large histories |
| Request/item indexes (`request_id`, `entitlement_id`, …) | Hot lookups | Standard B-tree cost on inserts |

Isolation: outbox claim uses row-level locks with `SKIP LOCKED` so concurrent publishers do not block each other (read-committed + skip). Unique `LogicalOperationId` provides idempotency under concurrent consumers without needing serializable isolation.

## Keyset pagination

- Audit: `GET /api/v1/audit/requests/{id}?take=&afterOccurredAtUtc=&afterId=`
- Pending approvals: optional `afterCreatedAtUtc` + `afterApprovalCaseId` (Skip kept for UI compat)
- Prefer keyset over `OFFSET` for growing tables; Skip remains OK for small pending queues.

## AsNoTracking

- Audit, retry claims, notification stores, existence checks, and **saga GET** (`GetByRequestIdReadOnlyAsync`) use `AsNoTracking`.
- Mutation paths (orchestrator, recon, approvals) keep tracking + Includes — intentional to avoid N+1 when updating collections.

## Connection pooling

Npgsql pools by default. Tune via connection string:

- `Maximum Pool Size` (default 100)
- `Minimum Pool Size`
- `Timeout` / `Command Timeout` (EF also sets `CommandTimeout(60)` for large graphs)

Workers and API share the platform DB — size pools so total open connections stay under Postgres `max_connections`.

## Avoiding N+1

- Repositories use explicit `Include` for aggregates needed in one query.
- Aggregation HttpClients fan out per provider (bounded to two), not per entitlement.
- Outbox claim returns a batch then publishes without per-row DB re-query beyond the claimed set.

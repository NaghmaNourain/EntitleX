# EntitleX interview guide (Senior / Staff)

Questions and expected talking points grounded in the Phase 1–12 codebase. Prefer concrete file/table names over slogans. Diagrams: [diagrams/README.md](diagrams/README.md).

---

## 1. Forward recovery vs compensation

**Q:** Why doesn’t EntitleX compensate (shorten expiry) when a sibling item fails after another succeeded?

**Expect:**

- Extensions **grant time**; customers already hold the longer expiry.
- Compensating revoke is not a safe product inverse and can punish legitimate coverage.
- Saga persists per-item outcomes; mixed → `PartiallyCompleted` / `PartiallyFulfilled`.
- Succeeded `LogicalOperationId`s are never demoted; selective retry copies **failed items only**.
- Point to [saga-workflow.md](saga-workflow.md) and domain invariants on `SagaItemOutcome`.

**Follow-up:** When *would* compensation be appropriate? (e.g. reservation/hold patterns with clear inverse — not this domain.)

---

## 2. Modular API vs microservices

**Q:** Why a modular control-plane API instead of request/approval/entitlement microservices from day one?

**Expect:** ADR 0001 — faster local iteration; clear module seams for later extraction; workers/simulators separate where async isolation matters; avoid premature distributed transactions. Tradeoff: shared process blast radius managed by discipline, not network boundaries.

---

## 3. Transactional outbox

**Q:** How do you publish fulfillment work without dual-write races?

**Expect:**

- Final approval writes `outbox_messages` in the **same Postgres transaction** as saga/request updates.
- `OutboxPublisher` claims with `FOR UPDATE SKIP LOCKED`, publishes to SQS, marks Published; exponential backoff → Poison.
- API never holds a DB transaction across SQS/Kafka/HTTP.
- At-least-once downstream + `processed_messages` consumer idempotency.

**Follow-up:** Why not change-data-capture? (Portfolio simplicity; outbox is explicit and testable.)

---

## 4. Messaging topology (SQS then Kafka)

**Q:** Why SQS for fulfillment commands and Kafka for provider fan-out/results?

**Expect:**

- SQS: work queue + DLQ (`maxReceiveCount` 5) for control-plane dispatch after outbox.
- Kafka: per-provider command topics with **partition key = entitlement id** (ordering per entitlement); shared results topic for saga settlement.
- Simulators own their consumer groups; platform fulfillment worker consumes results.

---

## 5. Selective retry and lineage

**Q:** How do you retry only failed items without double-extending successes?

**Expect:**

- Preserve `LogicalOperationId`; new `AttemptId`; `ParentId` chain.
- `api_idempotency_records` for lost HTTP; `retry_item_claims` for concurrent overlap → 409.
- Provider unique index on `logical_operation_id` makes re-apply a no-op.
- [idempotency.md](idempotency.md) lineage diagram.

---

## 6. Reconciliation before retry

**Q:** Why block selective retry when status is Uncertain?

**Expect:**

- Timeout may mean “applied but completion lost” **or** “never received.”
- Blind POST risks double extension if Applied but unknown locally.
- Recon GETs provider by `LogicalOperationId`; Applied→repair; Failed→retry-eligible; NotFound/Unknown→hold.
- `ReconciliationGate` also catches local ledger Success vs saga Failed/Pending mismatches.

---

## 7. Polly: no retry on POST

**Q:** Provider HTTP uses Polly — why no retry on extension POSTs?

**Expect:**

- Separate named clients: GET pipeline has timeout + retry + CB + bulkhead; **command clients omit retry**.
- Defense-in-depth: GET retry handler also refuses non-GET methods.
- Uncertain POSTs are resolved via recon probes, not client-side re-POST.
- File: `ProviderHttpClientRegistration.cs`.

---

## 8. Saga vs request status

**Q:** Why both `LicenseExtensionRequestStatus` and `SagaState`?

**Expect:** Coarse API lifecycle vs fine-grained orchestrator. `InFulfillment` retained on the request; saga uses `QueuedForFulfillment` / `Processing` projected via `SagaRequestStatusMap`. Avoids breaking API clients while adding transition history and item outcomes.

---

## 9. Approval bands and AuthZ

**Q:** How are multi-stage approvals and roles enforced?

**Expect:**

- Bands by max extension days: auto / RM / RM+DD / reject.
- Saga enforces RM before Deal Desk; `ApprovalCase` stages + decisions.
- JWT policies: create, decide, read, manage recon; handlers re-check for sensitive MediatR paths.
- Escalation on approval timeout — **never** auto-approve on timeout.

---

## 10. Idempotency layers (stack them)

**Q:** List every idempotency mechanism in the fulfillment path.

**Expect (stack):**

1. Outbox `message_id` uniqueness + claim locking  
2. SQS at-least-once + `processed_messages` for fulfillment consumer  
3. Kafka results + `processed_messages` for result processor  
4. Unique `logical_operation_id` on provider `applied_license_operations`  
5. Unique `fulfillment_item_results.logical_operation_id`  
6. API `Idempotency-Key` + `retry_item_claims` on selective retry  

---

## 11. Observability without cardinality explosions

**Q:** What must never appear as Prometheus labels?

**Expect:** No `requestId`, `messageId`, `logicalOperationId`, or raw URLs. Low-cardinality labels only (`provider`, `outcome`, `from_state`/`to_state`, …). Correlation via W3C trace + `X-Correlation-ID` in logs/traces instead.

---

## 12. Deployment tradeoffs

**Q:** Why keep Compose primary if Terraform exists?

**Expect:** Cost and iteration speed ([COST.md](../infra/terraform/COST.md)); Kafka/MSK not auto-provisioned; secrets outside Terraform; ECS/Lambda gated. Staff answer: packaging ≠ production readiness — IAM, secrets rotation, ALB/DNS, MSK still follow-ups.

---

## Quick design quiz (short answers)

| Prompt | Punchy answer |
|--------|----------------|
| Dual-write risk? | Outbox in same UoW |
| Sibling failure after success? | Forward recovery / Partial |
| Unknown provider effect? | Recon GET; no POST |
| Concurrent retries? | Unique claim → 409 |
| Lost retry HTTP? | Idempotency-Key replay |
| Approval timeout? | Escalated, not approved |
| POST 5xx to provider? | No Polly retry; recon later |
| Extract microservice when? | Ownership/scale/isolation demand |

---

## References

- [system-design.md](system-design.md)
- [architecture.md](architecture.md)
- [saga-workflow.md](saga-workflow.md)
- [idempotency.md](idempotency.md)
- [reconciliation.md](reconciliation.md)
- [failure-scenarios.md](failure-scenarios.md)
- [adr/0001-modular-api-vs-microservices.md](adr/0001-modular-api-vs-microservices.md)

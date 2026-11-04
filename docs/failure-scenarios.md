# Failure scenarios (stubs)

Catalog of failure / uncertainty scenarios EntitleX is designed to survive.
Phase 8 covers reconciliation for uncertain provider outcomes; later phases
expand observability and UI-driven operator playbooks.

## Fulfillment path

| # | Scenario | Detection | Recovery |
|---|---|---|---|
| F1 | Provider applied; Kafka completion lost | Fulfillment timeout → Uncertain; recon GET finds Applied | Repair local → Succeeded; audit |
| F2 | Provider never received command | Timeout → Uncertain; recon GET → NotFound | Keep Uncertain; **no blind retry** until operator confirms |
| F3 | Provider recorded Failed | Recon GET → ConfirmedFailed | Mark Failed (retry-eligible); selective retry (Phase 7) |
| F4 | Provider HTTP 5xx during recon probe | Lookup → Unknown | Keep Uncertain; retry probe later |
| F5 | Local saga Failed but provider Applied | Ledger / recon mismatch | Correct local → Succeeded (forward repair) |
| F6 | Local saga Succeeded but provider Failed row | Mismatch audit | **Keep Succeeded** (forward recovery; never revoke) |
| F7 | Duplicate completion events | `processed_messages` + unique `LogicalOperationId` | Idempotent no-op |
| F8 | Concurrent selective retries of same failed items | `retry_item_claims` unique index | One wins; other → 409 |
| F9 | Lost HTTP response on retry create | `api_idempotency_records` | Replay returns prior retry id |
| F10 | Approval timeout | Saga `ApprovalPending` timeout | Escalated (not auto-approved) |

## Explicit non-goals (do not do)

- Blind POST of a new provider extension when status is NotFound / Unknown
- Compensating “shorten expiry” after a successful apply
- Retrying items still marked Uncertain / ReconciliationRequired

## Unit-testable injection points

| Injection | Test location |
|---|---|
| Provider returns Applied / Failed / NotFound / Unknown | `ReconciliationServiceTests` |
| Concurrent recon on same LogicalOperationId | `ReconciliationServiceTests.ConcurrentReconcile_*` |
| Correct Failed → Succeeded from provider Applied | `ReconciliationCorrectiveRepairTests` |
| Gate blocks uncertain + ledger mismatches | `ReconciliationGateTests` |
| AppliedLicenseOperation unique claim | CloudShield `LicenseExtensionApplierTests` |

## Remaining (later phases)

- Operator UI for unresolved ReconciliationRequired queues (Phase 10)
- Metric / alert boards for recon lag (Phase 11)
- Chaos / Testcontainers end-to-end loss of Kafka completions (when Docker available)

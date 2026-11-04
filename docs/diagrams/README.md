# Mermaid diagram index

All diagrams are embedded in markdown under `docs/` (and the root README messaging blurb). Render with any Mermaid-capable viewer (GitHub, VS Code Mermaid extension, etc.).

| Diagram | Required topic | Location |
|---------|----------------|----------|
| System architecture flowchart | System architecture | [architecture.md](../architecture.md) — “System flowchart” |
| Happy-path request sequence | Request sequence | [architecture.md](../architecture.md) — “Happy-path request sequence” |
| Approval band workflow | Approval workflow | [architecture.md](../architecture.md) — “Approval workflow (bands)” |
| Messaging / outbox topology | Outbox | [architecture.md](../architecture.md) — “Messaging topology (outbox)”; also [event-contracts.md](../event-contracts.md) — “Outbox publish loop” |
| Messaging sequence (SQS/Kafka) | Request / messaging sequence | [event-contracts.md](../event-contracts.md) — “Messaging sequence” |
| Saga AllowedTransitions | Saga state machine | [saga-workflow.md](../saga-workflow.md) — “AllowedTransitions (state machine)” |
| Platform + provider ER | ER | [database-schema.md](../database-schema.md) — platform, CloudShield, NetworkGuard |
| Retry lineage (ParentId / AttemptId / LogicalOperationId) | Retry lineage | [idempotency.md](../idempotency.md) — “Retry lineage” |
| Reconciliation decision / repair | Reconciliation | [reconciliation.md](../reconciliation.md) — “Decision / repair flow” |
| Failure recovery overview | Failure recovery | [failure-scenarios.md](../failure-scenarios.md) — “Failure recovery overview” |

Design narrative without extra diagrams: [system-design.md](../system-design.md). Interview prompts that reference these: [interview-guide.md](../interview-guide.md).

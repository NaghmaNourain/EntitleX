# EntitleX event contracts (Phase 5)

Integration messages use a versioned envelope. Schema version stubs start at `"1"`; payload DTOs are suffixed `V1`.

## Envelope

| Field | Purpose |
|-------|---------|
| `schemaVersion` | Contract version (`"1"`) |
| `messageType` | Stable type name (see below) |
| `messageId` | Unique id for this message (consumer idempotency) |
| `correlationId` | End-to-end correlation across the operation |
| `causationId` | Parent `messageId` that caused this message |
| `requestId` | License-extension request id when applicable |
| `logicalOperationId` | Stable item intent id (survives selective retries) |
| `occurredAtUtc` | Producer timestamp |
| `payload` | Typed JSON body |

## Message types

| Type | Transport | Notes |
|------|-----------|-------|
| `entitlex.fulfillment.command` | Outbox → SQS `entitlex-fulfillment` | Written on final approval in the same PG transaction |
| `entitlex.provider.license-extension.command` | Kafka (per-provider topic) | Partition key = entitlement id |
| `entitlex.provider.license-extension.completed` | Kafka `entitlex.provider.results` | Simulator success (incl. already-applied) |
| `entitlex.provider.license-extension.failed` | Kafka `entitlex.provider.results` | Simulator failure |

## Topics / queues (local Compose)

| Resource | Name |
|----------|------|
| SQS | `entitlex-fulfillment` |
| SQS DLQ | `entitlex-fulfillment-dlq` (maxReceiveCount 5) |
| Kafka | `cloudshield.license-extension.commands` |
| Kafka | `networkguard.license-extension.commands` |
| Kafka | `entitlex.provider.results` |

## Delivery semantics

- **At-least-once** on SQS and Kafka.
- Consumers claim `(consumerName, idempotencyKey)` in `processed_messages` before side effects.
- Fulfillment SQS: leave message on failure (visibility timeout retry); after repeated failures, move to DLQ.
- Outbox publisher: `FOR UPDATE SKIP LOCKED`, exponential retry, `Poison` after `MaxAttempts`.

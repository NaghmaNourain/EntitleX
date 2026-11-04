# EntitleX system architecture

Phase 12 view of the running system: modular control-plane API, durable saga, transactional outbox, SQS/Kafka messaging, vendor simulators, reconciliation, scheduled lambdas, and local observability stack.

## Components

| Layer | Processes |
|-------|-----------|
| UI | Vite React frontend (`:5173`) → proxies to API |
| Control plane | `EntitleX.Api` (`:5080`) — JWT Minimal APIs, MediatR, EF Core |
| Async workers | OutboxPublisher, FulfillmentWorker, ReconciliationWorker, NotificationService |
| Scheduled | ApprovalReminder, ExpirationNotification (Lambda hosts / EventBridge) |
| Simulators | CloudShield.Api (`:5081`), NetworkGuard.Api (`:5082`) |
| Data | Postgres DBs `entitlex_platform`, `cloudshield`, `networkguard` |
| Messaging | LocalStack SQS (`entitlex-fulfillment` + DLQ), Kafka topics |
| Observability | Jaeger (OTLP), Prometheus, Grafana, Serilog |

## System flowchart

```mermaid
flowchart LR
  UI[Frontend :5173] --> API[EntitleX.Api :5080]
  API --> PG[(entitlex_platform)]
  API --> Saga[LicenseExtensionSaga]
  Saga --> Outbox[(outbox_messages)]
  Outbox --> Pub[OutboxPublisher]
  Pub --> SQS[SQS entitlex-fulfillment]
  SQS --> Ful[FulfillmentWorker]
  Ful --> KCmd[Kafka provider command topics]
  KCmd --> CS[CloudShield.Api :5081]
  KCmd --> NG[NetworkGuard.Api :5082]
  CS --> CSPG[(cloudshield)]
  NG --> NGPG[(networkguard)]
  CS --> KRes[Kafka entitlex.provider.results]
  NG --> KRes
  KRes --> Ful
  Ful --> Saga
  Recon[ReconciliationWorker] --> Saga
  Recon --> CS
  Recon --> NG
  API --> Recon
  Lambdas[ApprovalReminder / ExpirationNotification] --> PG
  Lambdas --> Mail[Mailpit / SES]
  API --> Mail
  API -. OTLP .-> Jaeger
  Pub -. metrics .-> Prom[Prometheus]
  Ful -. metrics .-> Prom
  Recon -. metrics .-> Prom
  Prom --> Grafana
```

## Happy-path request sequence

```mermaid
sequenceDiagram
  actor User
  participant UI as Frontend
  participant API as EntitleX.Api
  participant PG as Platform PG
  participant Outbox as OutboxPublisher
  participant SQS as SQS
  participant Ful as FulfillmentWorker
  participant Kafka as Kafka
  participant Sim as Provider simulator

  User->>UI: Create + approve request
  UI->>API: JWT APIs (create / decide)
  API->>PG: Request + ApprovalCase + Saga (same UoW)
  Note over API,PG: Final approval writes OutboxMessage
  Outbox->>PG: Claim outbox FOR UPDATE SKIP LOCKED
  Outbox->>SQS: Publish fulfillment command
  Ful->>SQS: Receive
  Ful->>Kafka: Per-item provider commands
  Kafka->>Sim: Consume command
  Sim->>Sim: AppliedLicenseOperation claim
  Sim->>Kafka: completed / failed result
  Ful->>Kafka: Consume results
  Ful->>PG: Saga item outcomes + aggregate
```

## Approval workflow (bands)

```mermaid
flowchart TD
  Start[Request submitted] --> Band{Max extension days}
  Band -->|1-30| Auto[Auto-approve → Approved]
  Band -->|31-90| RM[PendingRenewalManagerApproval]
  Band -->|91-365| RM2[PendingRenewalManagerApproval]
  Band -->|>365| Rej[Rejected unsupported]
  RM -->|Approved| Approved[Approved]
  RM -->|Rejected| RejHuman[Rejected]
  RM2 -->|Approved| DD[PendingDealDeskApproval]
  RM2 -->|Rejected| RejHuman
  DD -->|Approved| Approved
  DD -->|Rejected| RejHuman
  Approved --> Queue[QueuedForFulfillment]
  Queue --> Process[Processing]
```

Timeouts: `ApprovalPending` → `Escalated` (not auto-approved). Reminder Lambda may email then apply configurable terminal state (default `Escalated`).

## Messaging topology (outbox)

```mermaid
flowchart LR
  Tx[API / worker PG transaction] --> OB[(outbox_messages Pending)]
  OB --> Claim[OutboxClaimer SKIP LOCKED]
  Claim --> Pub[OutboxPublisher]
  Pub -->|Sqs| SQS[entitlex-fulfillment]
  Pub -->|fail| Retry[Exponential backoff]
  Retry -->|MaxAttempts| Poison[Poison]
  SQS -->|maxReceiveCount 5| DLQ[entitlex-fulfillment-dlq]
  SQS --> Ful[FulfillmentWorker]
  Ful --> CSCmd[cloudshield.license-extension.commands]
  Ful --> NGCmd[networkguard.license-extension.commands]
  CSCmd --> Results[entitlex.provider.results]
  NGCmd --> Results
```

## Observability

- **Traces**: OpenTelemetry → Jaeger `:16686` (W3C `traceparent`)
- **Metrics**: API/simulators `GET /metrics`; workers HttpListener `:9180–9183`; Grafana `:3001`
- **Logs**: Serilog structured console; CloudWatch path documented in [observability.md](observability.md)

## Related docs

- [system-design.md](system-design.md) — decisions and tradeoffs
- [database-schema.md](database-schema.md) — tables
- [event-contracts.md](event-contracts.md) — envelopes
- [saga-workflow.md](saga-workflow.md) — state machine
- [diagrams/README.md](diagrams/README.md) — diagram index

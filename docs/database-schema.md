# Database schema

Three PostgreSQL databases (Compose init: `infra/docker/postgres/init-databases.sh`):

| Database | DbContext | Owner |
|----------|-----------|-------|
| `entitlex_platform` | `EntitleXDbContext` | Control plane, workers, lambdas |
| `cloudshield` | `CloudShieldDbContext` | CloudShield simulator |
| `networkguard` | `NetworkGuardDbContext` | NetworkGuard simulator |

Schema names are `public`. Tables below match EF models / migrations (inventory Phase 12).

## Platform ER (`entitlex_platform`)

```mermaid
erDiagram
  license_extension_requests ||--o{ license_extension_request_items : owns
  license_extension_requests ||--o{ retry_history : "source"
  license_extension_requests |o--o| license_extension_requests : "parent_id"
  license_extension_requests ||--o| approval_cases : "request_id"
  approval_cases ||--o{ approval_stages : owns
  approval_cases ||--o{ approval_decisions : owns
  license_extension_requests ||--o| license_extension_sagas : "request_id"
  license_extension_sagas ||--o{ saga_transitions : owns
  license_extension_sagas ||--o{ saga_item_outcomes : owns

  license_extension_requests {
    uuid id PK
    string extension_number UK
    uuid parent_id FK
    string status
    int approval_version
    string approved_scope_hash
    string requested_by
    string notes
    bytea row_version
    timestamptz created_at_utc
  }

  license_extension_request_items {
    uuid id PK
    uuid request_id FK
    string entitlement_id
    string provider
    uuid logical_operation_id
    uuid attempt_id
    timestamptz expiries
  }

  retry_history {
    uuid id PK
    uuid source_request_id FK
    uuid retry_request_id UK
    string extension_number
    int item_count
    string created_by
    string notes
  }

  approval_cases {
    uuid id PK
    uuid request_id UK
    string band
    string status
    int current_stage_order
    int max_extension_days
  }

  approval_stages {
    uuid id PK
    uuid approval_case_id FK
    int stage_order
    string required_role
    string status
    uuid decision_id
  }

  approval_decisions {
    uuid id PK
    uuid approval_case_id FK
    uuid request_id
    int stage_order
    string stage_role
    string outcome
    string comment
    string correlation_id
  }

  license_extension_sagas {
    uuid id PK
    uuid request_id UK
    string state
    string approval_band
    bool requires_deal_desk
  }

  saga_transitions {
    uuid id PK
    uuid SagaId FK
    string from_state
    string to_state
    string reason
    string actor
    string correlation_id
    timestamptz occurred_at_utc
  }

  saga_item_outcomes {
    uuid id PK
    uuid logical_operation_id UK
    uuid saga_id FK
    uuid request_item_id
    string entitlement_id
    string status
  }

  outbox_messages {
    uuid id PK
    uuid message_id UK
    string destination
    string kind
    jsonb payload
    string status
    uuid logical_operation_id
  }

  processed_messages {
    string consumer_name PK
    string idempotency_key PK
    string message_type
    timestamptz processed_at_utc
  }

  fulfillment_item_results {
    uuid id PK
    uuid logical_operation_id UK
    uuid request_id
    string provider
    bool succeeded
  }

  api_idempotency_records {
    string idempotency_key PK
    uuid source_request_id
    uuid retry_request_id
  }

  retry_item_claims {
    uuid source_request_id PK
    uuid logical_operation_id PK
    uuid retry_request_id
    string idempotency_key
  }

  audit_events {
    uuid id PK
    string event_type
    uuid request_id
    uuid logical_operation_id
    string actor
    string correlation_id
  }

  approval_reminder_records {
    uuid id PK
    string window_key UK
    uuid approval_case_id
    int stage_order
  }

  expiration_notification_records {
    uuid id PK
    string window_key UK
    string provider
    string entitlement_id
    string customer_id
  }
```

### Platform table notes

| Table | Role |
|-------|------|
| `license_extension_requests` | Aggregate root; self-FK `parent_id` (Restrict) for retry chains |
| `license_extension_request_items` | Cascade from request; carries `logical_operation_id` + `attempt_id` |
| `retry_history` | Appended on selective retry |
| `approval_cases` / `stages` / `decisions` | Sequential role-gated approval |
| `license_extension_sagas` / `transitions` / `item_outcomes` | Durable saga ledger |
| `outbox_messages` | Transactional outbox (`Pending`/`Processing`/`Published`/`Poison`) |
| `processed_messages` | Consumer idempotency composite PK |
| `fulfillment_item_results` | Local fulfillment ledger (unique `(logical_operation_id, attempt_id)`) |
| `api_idempotency_records` / `retry_item_claims` | Selective-retry HTTP + concurrency |
| `audit_events` | Append-only audit (recon + ops) |
| `approval_reminder_records` / `expiration_notification_records` | Lambda idempotency windows |

## CloudShield ER (`cloudshield`)

```mermaid
erDiagram
  entitlements ||--o{ applied_license_operations : "entitlement_key"

  entitlements {
    uuid id PK
    string entitlement_key UK
    string customer_account_id
    string product_code
    string product_group
    string display_name
    timestamptz expires_at_utc
  }

  applied_license_operations {
    uuid id PK
    uuid logical_operation_id UK
    uuid attempt_id
    string entitlement_key
    timestamptz previous_expiry
    timestamptz new_expiry
    string status
  }
```

## NetworkGuard ER (`networkguard`)

```mermaid
erDiagram
  licenses ||--o{ applied_license_operations : "license_id"

  licenses {
    uuid id PK
    string license_id UK
    string tenant_external_id
    string sku_code
    string license_group
    timestamptz valid_until_utc
  }

  applied_license_operations {
    uuid id PK
    uuid logical_operation_id UK
    uuid attempt_id
    string license_id
    timestamptz previous_expiry
    timestamptz new_expiry
    string status
  }
```

Provider apply: unique `logical_operation_id` makes double-apply a no-op success (idempotent replay).

## Related

- [architecture.md](architecture.md)
- [idempotency.md](idempotency.md)
- [saga-workflow.md](saga-workflow.md)

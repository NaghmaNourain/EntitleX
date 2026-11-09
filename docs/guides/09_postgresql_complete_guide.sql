-- =============================================================================
-- EntitleX Educational Guide 09 — PostgreSQL Complete Guide
-- =============================================================================
-- Audience : interview prep / portfolio deep-dive for the EntitleX control plane
-- Style    : heavily commented, runnable commentary (safe on a throwaway DB)
-- Grounding: docs/database-schema.md, EF configurations under
--            src/EntitleX.Infrastructure/Persistence/Configurations/,
--            simulator DBs cloudshield / networkguard, OutboxClaimer SKIP LOCKED
--
-- HOW TO USE
--   1. Prefer a local Compose Postgres (infra/docker/postgres/init-databases.sh).
--   2. Connect as the entitlex role and read along; most blocks are illustrative.
--   3. Do NOT run destructive blocks against a shared lab without a sandbox DB.
--
-- THREE DATABASES (Compose init)
--   entitlex_platform  → EntitleXDbContext  (control plane, workers, lambdas)
--   cloudshield        → CloudShieldDbContext
--   networkguard       → NetworkGuardDbContext
-- =============================================================================

-- #############################################################################
-- TABLE OF CONTENTS
-- #############################################################################
--  1. Mental model: why three databases
--  2. Types used by EntitleX (uuid, timestamptz, text, jsonb, bytea/xid)
--  3. Schema sketches matching EF mappings (platform)
--  4. Indexes — claim paths, unique intents, keyset pagination
--  5. Constraints — uniqueness as idempotency, FKs, Restrict vs Cascade
--  6. Transactions & unit-of-work boundaries
--  7. Isolation levels — what EntitleX actually needs
--  8. Locking — FOR UPDATE SKIP LOCKED for outbox_messages
--  9. UPSERT patterns — processed_messages, retry claims, api_idempotency
-- 10. JSONB — outbox payload_json
-- 11. Pagination — keyset for pending approvals / audit
-- 12. Optimistic concurrency — row_version on license_extension_requests
-- 13. Simulator DBs — unique(logical_operation_id) on applied_license_operations
-- 14. EXPLAIN thoughts for entitlement search & claim hot paths
-- 15. Interview drills
-- #############################################################################


-- =============================================================================
-- 1. Mental model: why three databases
-- =============================================================================
-- Interview answer (short):
--   The control plane owns orchestration state (requests, approvals, saga,
--   outbox, idempotency). CloudShield and NetworkGuard are *vendor simulators*
--   with their own entitlement/license tables. Separate DBs enforce the
--   anti-corruption boundary: the platform never joins into simulator schemas
--   and never holds a transaction open across provider HTTP.
--
-- Real init script:
--   infra/docker/postgres/init-databases.sh
--
-- Real DbContexts:
--   src/EntitleX.Infrastructure/Persistence/EntitleXDbContext.cs
--   src/simulators/CloudShield/CloudShield.Infrastructure/Persistence/CloudShieldDbContext.cs
--   src/simulators/NetworkGuard/NetworkGuard.Infrastructure/Persistence/NetworkGuardDbContext.cs

-- Illustrative (idempotent) database creation matching Compose:
-- SELECT 'CREATE DATABASE entitlex_platform' WHERE NOT EXISTS
--   (SELECT FROM pg_database WHERE datname = 'entitlex_platform')\gexec
-- SELECT 'CREATE DATABASE cloudshield' WHERE NOT EXISTS
--   (SELECT FROM pg_database WHERE datname = 'cloudshield')\gexec
-- SELECT 'CREATE DATABASE networkguard' WHERE NOT EXISTS
--   (SELECT FROM pg_database WHERE datname = 'networkguard')\gexec

COMMENT ON DATABASE postgres IS
  'Default Compose DB; EntitleX apps connect to entitlex_platform / cloudshield / networkguard.';


-- =============================================================================
-- 2. Types used by EntitleX
-- =============================================================================
-- uuid         — RequestId, LogicalOperationId, AttemptId, MessageId, saga ids
--                (BuildingBlocks.Ids are CLR readonly record structs over Guid)
-- timestamptz  — ALL business clocks are UTC DateTimeOffset → timestamptz
-- text/varchar — statuses stored as strings via EF HasConversion<string>()
-- jsonb        — outbox_messages.payload_json (MessageEnvelope payload)
-- integer      — approval_version, stage_order, attempt_count, max_attempts
-- boolean      — requires_deal_desk, fulfillment succeeded flags
-- xid/uint     — row_version concurrency token (EF maps uint; Postgres stores
--                as integer/xid-like concurrency column depending on provider
--                mapping — see LicenseExtensionRequestConfiguration)

-- Demo types table (sandbox only) — mirrors the *kinds* of columns, not prod DDL:
DROP TABLE IF EXISTS guide09_type_lab;
CREATE TABLE guide09_type_lab (
    id                    uuid PRIMARY KEY,
    logical_operation_id  uuid NOT NULL,
    status                varchar(32) NOT NULL,
    payload               jsonb NOT NULL,
    created_at_utc        timestamptz NOT NULL DEFAULT now(),
    row_version           integer NOT NULL DEFAULT 0
);

INSERT INTO guide09_type_lab (id, logical_operation_id, status, payload)
VALUES (
    gen_random_uuid(),
    gen_random_uuid(),
    'Pending',
    '{"messageType":"entitlex.fulfillment.command","schemaVersion":"1"}'::jsonb
);

-- Interview tip: never store "local time" for saga deadlines.
-- SagaTimeoutKind.ApprovalPending / FulfillmentProcessing use UTC deadlines
-- (src/EntitleX.Domain/Saga/LicenseExtensionSaga.cs).


-- =============================================================================
-- 3. Schema sketches matching docs/database-schema.md + EF mappings
-- =============================================================================
-- These CREATE TABLE statements are *educational sketches*. Production schema
-- is owned by EF migrations under:
--   src/EntitleX.Infrastructure/Persistence/Migrations/
-- Column names below mirror Configuration classes (snake_case).

-- ---------------------------------------------------------------------------
-- 3.1 license_extension_requests (aggregate root)
-- Config: LicenseExtensionRequestConfiguration.cs
-- ---------------------------------------------------------------------------
DROP TABLE IF EXISTS guide09_license_extension_request_items CASCADE;
DROP TABLE IF EXISTS guide09_retry_history CASCADE;
DROP TABLE IF EXISTS guide09_license_extension_requests CASCADE;

CREATE TABLE guide09_license_extension_requests (
    id                   uuid PRIMARY KEY,
    extension_number     varchar(64) NOT NULL,
    parent_id            uuid NULL,                 -- self-FK for selective retry
    status               varchar(32) NOT NULL,      -- Draft/Submitted/.../Failed
    approval_version     integer NOT NULL DEFAULT 0,
    approved_scope_hash  varchar(128) NULL,
    requested_by         varchar(256) NOT NULL,
    notes                varchar(2000) NULL,
    row_version          integer NOT NULL DEFAULT 0, -- optimistic concurrency
    created_at_utc       timestamptz NOT NULL
);

-- Business unique: ExtensionNumber (ux_license_extension_requests_extension_number)
CREATE UNIQUE INDEX ux_guide09_ler_extension_number
    ON guide09_license_extension_requests (extension_number);

CREATE INDEX ix_guide09_ler_status
    ON guide09_license_extension_requests (status);

CREATE INDEX ix_guide09_ler_created_at_utc
    ON guide09_license_extension_requests (created_at_utc);

CREATE INDEX ix_guide09_ler_parent_id
    ON guide09_license_extension_requests (parent_id);

-- Self-FK with Restrict (retry chain must not cascade-delete parents)
ALTER TABLE guide09_license_extension_requests
    ADD CONSTRAINT fk_guide09_ler_parent
    FOREIGN KEY (parent_id)
    REFERENCES guide09_license_extension_requests (id)
    ON DELETE RESTRICT;

-- ---------------------------------------------------------------------------
-- 3.2 license_extension_request_items
-- Config: LicenseExtensionRequestItemConfiguration.cs
-- Carries logical_operation_id (stable intent) + attempt_id (per try)
-- ---------------------------------------------------------------------------
CREATE TABLE guide09_license_extension_request_items (
    id                     uuid PRIMARY KEY,
    request_id             uuid NOT NULL
        REFERENCES guide09_license_extension_requests (id) ON DELETE CASCADE,
    entitlement_id         varchar(128) NOT NULL,
    provider               varchar(64) NOT NULL,   -- CloudShield | NetworkGuard
    logical_operation_id   uuid NOT NULL,
    attempt_id             uuid NOT NULL,
    current_expiry_utc     timestamptz NOT NULL,
    requested_expiry_utc   timestamptz NOT NULL
);

CREATE INDEX ix_guide09_leri_request_id
    ON guide09_license_extension_request_items (request_id);

CREATE INDEX ix_guide09_leri_entitlement_id
    ON guide09_license_extension_request_items (entitlement_id);

CREATE INDEX ix_guide09_leri_logical_operation_id
    ON guide09_license_extension_request_items (logical_operation_id);

-- ---------------------------------------------------------------------------
-- 3.3 approval_cases / stages / decisions
-- Config: ApprovalCaseConfiguration.cs, ApprovalStageConfiguration.cs, ...
-- ---------------------------------------------------------------------------
DROP TABLE IF EXISTS guide09_approval_decisions CASCADE;
DROP TABLE IF EXISTS guide09_approval_stages CASCADE;
DROP TABLE IF EXISTS guide09_approval_cases CASCADE;

CREATE TABLE guide09_approval_cases (
    id                   uuid PRIMARY KEY,
    request_id           uuid NOT NULL UNIQUE,      -- 1:1 with request
    band                 varchar(64) NOT NULL,      -- AutoApprove / RM / RM+DD / Unsupported
    status               varchar(32) NOT NULL,
    current_stage_order  integer NOT NULL DEFAULT 0,
    max_extension_days   integer NOT NULL,
    created_at_utc       timestamptz NOT NULL,
    completed_at_utc     timestamptz NULL
);

CREATE TABLE guide09_approval_stages (
    id                 uuid PRIMARY KEY,
    approval_case_id   uuid NOT NULL
        REFERENCES guide09_approval_cases (id) ON DELETE CASCADE,
    stage_order        integer NOT NULL,
    required_role      varchar(64) NOT NULL,       -- RenewalManager / DealDeskManager
    status             varchar(32) NOT NULL,
    decision_id        uuid NULL
);

CREATE TABLE guide09_approval_decisions (
    id                 uuid PRIMARY KEY,
    approval_case_id   uuid NOT NULL
        REFERENCES guide09_approval_cases (id) ON DELETE CASCADE,
    request_id         uuid NOT NULL,
    stage_order        integer NOT NULL,
    stage_role         varchar(64) NOT NULL,
    outcome            varchar(32) NOT NULL,       -- Approved / Rejected
    comment            varchar(2000) NULL,
    actor_user_id      varchar(128) NOT NULL,
    correlation_id     varchar(64) NULL,
    decided_at_utc     timestamptz NOT NULL
);

-- ---------------------------------------------------------------------------
-- 3.4 saga tables
-- Config: LicenseExtensionSagaConfiguration.cs, SagaTransition..., SagaItemOutcome...
-- Domain: src/EntitleX.Domain/Saga/LicenseExtensionSaga.cs
-- ---------------------------------------------------------------------------
DROP TABLE IF EXISTS guide09_saga_item_outcomes CASCADE;
DROP TABLE IF EXISTS guide09_saga_transitions CASCADE;
DROP TABLE IF EXISTS guide09_license_extension_sagas CASCADE;

CREATE TABLE guide09_license_extension_sagas (
    id                   uuid PRIMARY KEY,
    request_id           uuid NOT NULL UNIQUE,
    state                varchar(64) NOT NULL,
    approval_band        varchar(64) NOT NULL,
    requires_deal_desk   boolean NOT NULL DEFAULT false,
    created_at_utc       timestamptz NOT NULL,
    updated_at_utc       timestamptz NOT NULL
);

CREATE INDEX ix_guide09_sagas_state_updated
    ON guide09_license_extension_sagas (state, updated_at_utc);

CREATE TABLE guide09_saga_transitions (
    id               uuid PRIMARY KEY,
    saga_id          uuid NOT NULL
        REFERENCES guide09_license_extension_sagas (id) ON DELETE CASCADE,
    from_state       varchar(64) NOT NULL,
    to_state         varchar(64) NOT NULL,
    reason           varchar(512) NOT NULL,
    actor            varchar(128) NULL,
    correlation_id   varchar(64) NULL,
    occurred_at_utc  timestamptz NOT NULL
);

CREATE TABLE guide09_saga_item_outcomes (
    id                     uuid PRIMARY KEY,
    logical_operation_id   uuid NOT NULL UNIQUE,  -- one outcome per intent
    saga_id                uuid NOT NULL
        REFERENCES guide09_license_extension_sagas (id) ON DELETE CASCADE,
    request_item_id        uuid NOT NULL,
    entitlement_id         varchar(128) NOT NULL,
    status                 varchar(32) NOT NULL   -- Pending/Succeeded/Failed/Uncertain
);

-- ---------------------------------------------------------------------------
-- 3.5 outbox_messages (transactional outbox)
-- Config: OutboxMessageConfiguration.cs
-- Claimer: src/EntitleX.Infrastructure/Outbox/OutboxClaimer.cs
-- ---------------------------------------------------------------------------
DROP TABLE IF EXISTS guide09_outbox_messages CASCADE;

CREATE TABLE guide09_outbox_messages (
    id                     uuid PRIMARY KEY,
    message_id             uuid NOT NULL UNIQUE,
    message_type           varchar(200) NOT NULL,
    payload_json           jsonb NOT NULL,
    destination_kind       varchar(32) NOT NULL,   -- Sqs | Kafka
    destination            varchar(256) NOT NULL,  -- queue/topic name
    partition_key          varchar(256) NULL,      -- entitlement id for Kafka
    status                 varchar(32) NOT NULL,   -- Pending/Processing/Published/Poison
    attempt_count          integer NOT NULL DEFAULT 0,
    max_attempts           integer NOT NULL DEFAULT 5,
    created_at_utc         timestamptz NOT NULL,
    available_at_utc       timestamptz NOT NULL,
    locked_until_utc       timestamptz NULL,
    locked_by              varchar(128) NULL,
    published_at_utc       timestamptz NULL,
    last_error             varchar(2000) NULL,
    correlation_id         uuid NULL,
    causation_id           uuid NULL,
    request_id             uuid NULL,
    logical_operation_id   uuid NULL,
    schema_version         varchar(32) NOT NULL
);

-- Claim hot path indexes (documented in Configuration + docs/performance.md)
CREATE INDEX ix_guide09_outbox_status_available
    ON guide09_outbox_messages (status, available_at_utc);

CREATE INDEX ix_guide09_outbox_status_locked
    ON guide09_outbox_messages (status, locked_until_utc);

-- ---------------------------------------------------------------------------
-- 3.6 Idempotency / claims / fulfillment ledger
-- ---------------------------------------------------------------------------
DROP TABLE IF EXISTS guide09_processed_messages CASCADE;
DROP TABLE IF EXISTS guide09_fulfillment_item_results CASCADE;
DROP TABLE IF EXISTS guide09_api_idempotency_records CASCADE;
DROP TABLE IF EXISTS guide09_retry_item_claims CASCADE;

CREATE TABLE guide09_processed_messages (
    consumer_name      varchar(128) NOT NULL,
    idempotency_key    varchar(256) NOT NULL,
    message_type       varchar(200) NULL,
    processed_at_utc   timestamptz NOT NULL,
    PRIMARY KEY (consumer_name, idempotency_key)
);

CREATE TABLE guide09_fulfillment_item_results (
    id                     uuid PRIMARY KEY,
    logical_operation_id   uuid NOT NULL UNIQUE,
    request_id             uuid NOT NULL,
    provider               varchar(64) NOT NULL,
    succeeded              boolean NOT NULL,
    recorded_at_utc        timestamptz NOT NULL
);

CREATE TABLE guide09_api_idempotency_records (
    idempotency_key     varchar(128) PRIMARY KEY,
    source_request_id   uuid NOT NULL,
    retry_request_id    uuid NOT NULL,
    notes               varchar(2000) NULL,
    created_at_utc      timestamptz NOT NULL
);

CREATE TABLE guide09_retry_item_claims (
    source_request_id      uuid NOT NULL,
    logical_operation_id   uuid NOT NULL,
    retry_request_id       uuid NOT NULL,
    idempotency_key        varchar(128) NOT NULL,
    claimed_at_utc         timestamptz NOT NULL,
    PRIMARY KEY (source_request_id, logical_operation_id)
);

CREATE INDEX ix_guide09_retry_claims_retry_request
    ON guide09_retry_item_claims (retry_request_id);

CREATE INDEX ix_guide09_retry_claims_idempotency
    ON guide09_retry_item_claims (idempotency_key);

-- ---------------------------------------------------------------------------
-- 3.7 audit + lambda window tables
-- ---------------------------------------------------------------------------
DROP TABLE IF EXISTS guide09_audit_events CASCADE;
DROP TABLE IF EXISTS guide09_approval_reminder_records CASCADE;
DROP TABLE IF EXISTS guide09_expiration_notification_records CASCADE;

CREATE TABLE guide09_audit_events (
    id                     uuid PRIMARY KEY,
    event_type             varchar(128) NOT NULL,
    request_id             uuid NULL,
    logical_operation_id   uuid NULL,
    actor                  varchar(128) NULL,
    correlation_id         varchar(64) NULL,
    occurred_at_utc        timestamptz NOT NULL,
    payload_json           jsonb NULL
);

-- Keyset-friendly composite for audit history (docs/performance.md)
CREATE INDEX ix_guide09_audit_request_occurred_id
    ON guide09_audit_events (request_id, occurred_at_utc, id);

CREATE TABLE guide09_approval_reminder_records (
    id                 uuid PRIMARY KEY,
    window_key         varchar(256) NOT NULL UNIQUE,
    approval_case_id   uuid NOT NULL,
    stage_order        integer NOT NULL,
    sent_at_utc        timestamptz NOT NULL
);

CREATE TABLE guide09_expiration_notification_records (
    id                 uuid PRIMARY KEY,
    window_key         varchar(256) NOT NULL UNIQUE,
    provider           varchar(64) NOT NULL,
    entitlement_id     varchar(128) NOT NULL,
    customer_id        varchar(128) NOT NULL,
    sent_at_utc        timestamptz NOT NULL
);


-- =============================================================================
-- 4. Indexes — why each exists (interview talking points)
-- =============================================================================
-- | Index | Purpose | Tradeoff |
-- | ix_outbox_messages_status_available | FOR UPDATE SKIP LOCKED claim | write amp on status |
-- | ix_outbox_messages_status_locked    | stale-lock reclaim          | extra updates |
-- | ux_*_logical_operation_id           | idempotent apply / outcomes | insert races → one winner |
-- | ux_extension_number                 | business uniqueness         | collisions surface as 409 |
-- | ix_sagas_state_updated              | recon / ops backlog         | slightly costlier updates |
-- | ix_audit_request_occurred_id        | keyset pagination           | larger index |
--
-- Source: docs/performance.md + OutboxMessageConfiguration comments.


-- =============================================================================
-- 5. Constraints as product rules
-- =============================================================================
-- 5.1 Unique LogicalOperationId = "this extension intent already applied"
--     Platform: saga_item_outcomes, fulfillment_item_results
--     Simulators: applied_license_operations
--
-- 5.2 Unique (source_request_id, logical_operation_id) on retry_item_claims
--     → concurrent selective retries of the same failed items → one winner,
--       loser gets 409 Conflict (docs/idempotency.md).
--
-- 5.3 Unique idempotency_key on api_idempotency_records
--     → lost HTTP response replay returns the prior retry_request_id.
--
-- 5.4 Parent FK ON DELETE RESTRICT
--     → cannot delete a parent request that still has retry children.
--
-- 5.5 Items CASCADE from request
--     → aggregate ownership; deleting a request removes its items/history.


-- =============================================================================
-- 6. Transactions & unit-of-work boundaries
-- =============================================================================
-- CRITICAL EntitleX rule (docs/idempotency.md, system-design.md):
--   The API never holds a Postgres transaction open across Kafka, SQS, or
--   outbound HTTP. Async work is enqueued by writing outbox_messages in the
--   SAME short transaction as the business change (final approval, selective
--   retry claims, saga QueueFulfillment).
--
-- Typical final-approval UoW (conceptual):
--   BEGIN;
--     UPDATE license_extension_requests SET status='InFulfillment', row_version=row_version+1 ...;
--     UPDATE license_extension_sagas SET state='QueuedForFulfillment' ...;
--     INSERT INTO saga_item_outcomes (... Pending ...);
--     INSERT INTO outbox_messages (... status='Pending' ...);
--   COMMIT;
--   -- OutboxPublisher later claims and publishes to SQS entitlex-fulfillment

BEGIN;
INSERT INTO guide09_license_extension_requests (
    id, extension_number, parent_id, status, approval_version,
    requested_by, created_at_utc, row_version)
VALUES (
    '11111111-1111-1111-1111-111111111111',
    'EXT-DEMO-0001',
    NULL,
    'Approved',
    1,
    'renewal.rep@example.com',
    now(),
    0);

INSERT INTO guide09_outbox_messages (
    id, message_id, message_type, payload_json, destination_kind, destination,
    status, attempt_count, max_attempts, created_at_utc, available_at_utc, schema_version,
    request_id, partition_key)
VALUES (
    gen_random_uuid(),
    gen_random_uuid(),
    'entitlex.fulfillment.command',
    '{"schemaVersion":"1","items":[{"provider":"CloudShield"}]}'::jsonb,
    'Sqs',
    'entitlex-fulfillment',
    'Pending',
    0,
    5,
    now(),
    now(),
    '1',
    '11111111-1111-1111-1111-111111111111',
    NULL);
COMMIT;


-- =============================================================================
-- 7. Isolation levels — what EntitleX actually needs
-- =============================================================================
-- Default Postgres isolation: READ COMMITTED.
--
-- EntitleX does NOT require SERIALIZABLE for the happy path because:
--   1) Outbox claiming uses row locks + SKIP LOCKED (workers don't block each other).
--   2) Idempotency is enforced by UNIQUE constraints (logical_operation_id,
--      processed_messages PK, retry_item_claims PK, api_idempotency_records PK).
--   3) Optimistic concurrency (row_version) catches conflicting request updates.
--
-- When would REPEATABLE READ / SERIALIZABLE help?
--   Rare analytics-style multi-aggregate reads. Not the outbox/fulfillment path.
--
-- Show current session settings:
SHOW transaction_isolation;
SHOW default_transaction_isolation;


-- =============================================================================
-- 8. Locking — FOR UPDATE SKIP LOCKED (OutboxClaimer)
-- =============================================================================
-- Real SQL (trimmed) from OutboxClaimer.cs:
--
--   WITH claimed AS (
--     SELECT id FROM outbox_messages
--     WHERE (status = 'Pending' AND available_at_utc <= @now)
--        OR (status = 'Processing' AND locked_until_utc < @now)
--     ORDER BY available_at_utc
--     LIMIT @batch
--     FOR UPDATE SKIP LOCKED
--   )
--   UPDATE outbox_messages AS o
--   SET status = 'Processing',
--       attempt_count = o.attempt_count + 1,
--       locked_until_utc = @lockedUntil,
--       locked_by = @workerId
--   FROM claimed WHERE o.id = claimed.id
--   RETURNING o.id;
--
-- Interview points:
--   * SKIP LOCKED = competing OutboxPublisher instances take disjoint batches
--     without waiting (no classic leader election required for workers).
--   * locked_until_utc enables stale-lock reclaim if a worker dies mid-publish.
--   * attempt_count + max_attempts → Poison (see failure-scenarios / event-contracts).
--   * Lease/fencing token here is (locked_by, locked_until_utc), not ZooKeeper.

-- Seed a few pending rows for the demo claim:
INSERT INTO guide09_outbox_messages (
    id, message_id, message_type, payload_json, destination_kind, destination,
    status, attempt_count, max_attempts, created_at_utc, available_at_utc, schema_version)
SELECT
    gen_random_uuid(),
    gen_random_uuid(),
    'entitlex.fulfillment.command',
    '{}'::jsonb,
    'Sqs',
    'entitlex-fulfillment',
    'Pending',
    0,
    5,
    now(),
    now(),
    '1'
FROM generate_series(1, 5);

-- Demo claim (single session):
WITH claimed AS (
    SELECT id
    FROM guide09_outbox_messages
    WHERE status = 'Pending' AND available_at_utc <= now()
    ORDER BY available_at_utc
    LIMIT 2
    FOR UPDATE SKIP LOCKED
)
UPDATE guide09_outbox_messages AS o
SET status = 'Processing',
    attempt_count = o.attempt_count + 1,
    locked_until_utc = now() + interval '30 seconds',
    locked_by = 'outbox-publisher-demo-1'
FROM claimed
WHERE o.id = claimed.id
RETURNING o.id, o.attempt_count, o.locked_by;

-- Poison path (conceptual):
-- UPDATE outbox_messages
-- SET status = 'Poison', last_error = 'MaxAttempts exceeded'
-- WHERE status = 'Processing' AND attempt_count >= max_attempts;


-- =============================================================================
-- 9. UPSERT patterns for idempotency
-- =============================================================================
-- 9.1 processed_messages — consumer inbox / at-least-once dedupe
--     Config: ProcessedMessageConfiguration.cs
--     Store:  EfConsumerIdempotencyStore.cs
--
-- Pattern: INSERT ... ON CONFLICT DO NOTHING, then check whether we inserted.
-- If conflict → already processed → skip side effects.

INSERT INTO guide09_processed_messages (consumer_name, idempotency_key, message_type, processed_at_utc)
VALUES ('entitlex-provider-results', 'msg-aaaa-bbbb', 'entitlex.provider.license-extension.completed', now())
ON CONFLICT (consumer_name, idempotency_key) DO NOTHING;

-- Second delivery (duplicate Kafka/SQS receive) — no-op:
INSERT INTO guide09_processed_messages (consumer_name, idempotency_key, message_type, processed_at_utc)
VALUES ('entitlex-provider-results', 'msg-aaaa-bbbb', 'entitlex.provider.license-extension.completed', now())
ON CONFLICT (consumer_name, idempotency_key) DO NOTHING
RETURNING consumer_name;  -- empty = already processed

-- 9.2 api_idempotency_records — selective retry lost-response replay
--     Store: EfRetryClaimStore.FindByIdempotencyKeyAsync / TryClaimItemsAsync
INSERT INTO guide09_api_idempotency_records (
    idempotency_key, source_request_id, retry_request_id, notes, created_at_utc)
VALUES (
    'client-key-42',
    '11111111-1111-1111-1111-111111111111',
    '22222222-2222-2222-2222-222222222222',
    'retry after CloudShield timeout',
    now())
ON CONFLICT (idempotency_key) DO NOTHING;

-- Lookup (replay path):
SELECT retry_request_id
FROM guide09_api_idempotency_records
WHERE idempotency_key = 'client-key-42';

-- 9.3 retry_item_claims — concurrent overlapping retries
-- Unique PK (source_request_id, logical_operation_id) is the last line of defense
-- when two callers race with different Idempotency-Keys (docs/idempotency.md).
INSERT INTO guide09_retry_item_claims (
    source_request_id, logical_operation_id, retry_request_id, idempotency_key, claimed_at_utc)
VALUES (
    '11111111-1111-1111-1111-111111111111',
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    '22222222-2222-2222-2222-222222222222',
    'client-key-42',
    now())
ON CONFLICT (source_request_id, logical_operation_id) DO NOTHING;

-- Competing claim (different key, same items) → conflict / 0 rows:
INSERT INTO guide09_retry_item_claims (
    source_request_id, logical_operation_id, retry_request_id, idempotency_key, claimed_at_utc)
VALUES (
    '11111111-1111-1111-1111-111111111111',
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    '33333333-3333-3333-3333-333333333333',
    'other-key-99',
    now())
ON CONFLICT (source_request_id, logical_operation_id) DO NOTHING
RETURNING logical_operation_id;

-- 9.4 fulfillment_item_results — local ledger unique on logical_operation_id
INSERT INTO guide09_fulfillment_item_results (
    id, logical_operation_id, request_id, provider, succeeded, recorded_at_utc)
VALUES (
    gen_random_uuid(),
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    '11111111-1111-1111-1111-111111111111',
    'CloudShield',
    true,
    now())
ON CONFLICT (logical_operation_id) DO NOTHING;


-- =============================================================================
-- 10. JSONB — outbox payload_json
-- =============================================================================
-- EF: .HasColumnType("jsonb") on OutboxMessage.PayloadJson
-- Envelope fields documented in docs/event-contracts.md
--
-- Why jsonb (not text)?
--   * Operators can inspect payloads in ops without app code.
--   * GIN indexes possible later for message_type filters (not required Phase 12).
--   * Still treat schemaVersion + messageType as the contract, not ad-hoc SQL.

SELECT
    message_type,
    payload_json ->> 'schemaVersion' AS schema_version,
    jsonb_pretty(payload_json) AS pretty
FROM guide09_outbox_messages
WHERE destination = 'entitlex-fulfillment'
LIMIT 3;

-- Containment query example (ops debugging only):
SELECT id, message_type
FROM guide09_outbox_messages
WHERE payload_json @> '{"schemaVersion":"1"}'::jsonb
LIMIT 5;


-- =============================================================================
-- 11. Pagination — keyset for pending approvals / audit
-- =============================================================================
-- App code: ListPendingApprovalsQueryHandler.cs
--   Prefer AfterCreatedAtUtc + AfterApprovalCaseId over OFFSET/Skip.
-- Audit: GET /api/v1/audit/requests/{id}?afterOccurredAtUtc=&afterId=
--
-- Why keyset?
--   OFFSET N scans/skips N rows → gets slower as history grows.
--   Keyset (created_at, id) > cursor is O(log n) with the right index.

-- Seed approval cases for paging demo:
INSERT INTO guide09_approval_cases (
    id, request_id, band, status, current_stage_order, max_extension_days, created_at_utc)
VALUES
    ('a0000000-0000-0000-0000-000000000001', gen_random_uuid(), 'RenewalManagerOnly', 'Pending', 0, 45, '2026-11-01 10:00:00+00'),
    ('a0000000-0000-0000-0000-000000000002', gen_random_uuid(), 'RenewalManagerOnly', 'Pending', 0, 60, '2026-11-01 10:00:01+00'),
    ('a0000000-0000-0000-0000-000000000003', gen_random_uuid(), 'RenewalManagerThenDealDesk', 'Pending', 0, 120, '2026-11-01 10:00:02+00'),
    ('a0000000-0000-0000-0000-000000000004', gen_random_uuid(), 'RenewalManagerOnly', 'Pending', 0, 40, '2026-11-01 10:00:03+00');

-- Page 1 (take 2):
SELECT id, max_extension_days, created_at_utc
FROM guide09_approval_cases
WHERE status = 'Pending'
ORDER BY created_at_utc, id
LIMIT 2;

-- Page 2 using keyset cursor from last row of page 1:
SELECT id, max_extension_days, created_at_utc
FROM guide09_approval_cases
WHERE status = 'Pending'
  AND (
        created_at_utc > '2026-11-01 10:00:01+00'
     OR (created_at_utc = '2026-11-01 10:00:01+00'
         AND id > 'a0000000-0000-0000-0000-000000000002')
  )
ORDER BY created_at_utc, id
LIMIT 2;

-- Anti-pattern (avoid on large tables):
-- SELECT ... ORDER BY created_at_utc OFFSET 10000 LIMIT 50;


-- =============================================================================
-- 12. Optimistic concurrency — row_version
-- =============================================================================
-- Config: LicenseExtensionRequestConfiguration
--   builder.Property(x => x.RowVersion)
--     .HasColumnName("row_version")
--     .IsConcurrencyToken()
--     .HasDefaultValue(0u);
--
-- Domain: LicenseExtensionRequest.RowVersion (uint)
--
-- Pattern:
--   Worker A reads row_version=3, Worker B reads row_version=3.
--   A saves successfully → row_version becomes 4.
--   B save with WHERE row_version=3 affects 0 rows → DbUpdateConcurrencyException.
--
-- EntitleX uses this on the request aggregate so concurrent approval/retry
-- mutations cannot silently overwrite each other.

UPDATE guide09_license_extension_requests
SET status = 'InFulfillment',
    row_version = row_version + 1
WHERE id = '11111111-1111-1111-1111-111111111111'
  AND row_version = 0
RETURNING id, status, row_version;

-- Stale writer simulation (expects 0 rows):
UPDATE guide09_license_extension_requests
SET status = 'Fulfilled',
    row_version = row_version + 1
WHERE id = '11111111-1111-1111-1111-111111111111'
  AND row_version = 0
RETURNING id;


-- =============================================================================
-- 13. Simulator databases — unique(logical_operation_id)
-- =============================================================================
-- CloudShield: entitlements + applied_license_operations
-- NetworkGuard: licenses + applied_license_operations
-- Config examples:
--   src/simulators/CloudShield/.../AppliedLicenseOperationConfiguration.cs
--   ux_applied_license_operations_logical_operation_id
--
-- Provider apply: double-apply is a no-op success (LicenseExtensionApplier.cs).
-- Reconciliation probes GET by LogicalOperationId (IProviderOperationStatusClient).

DROP TABLE IF EXISTS guide09_cs_applied_license_operations CASCADE;
DROP TABLE IF EXISTS guide09_cs_entitlements CASCADE;

CREATE TABLE guide09_cs_entitlements (
    id                    uuid PRIMARY KEY,
    entitlement_key       varchar(128) NOT NULL UNIQUE,
    customer_account_id   varchar(128) NOT NULL,
    product_code          varchar(64) NOT NULL,
    product_group         varchar(64) NULL,
    display_name          varchar(256) NOT NULL,
    expires_at_utc        timestamptz NOT NULL
);

CREATE TABLE guide09_cs_applied_license_operations (
    id                     uuid PRIMARY KEY,
    logical_operation_id   uuid NOT NULL,
    attempt_id             uuid NOT NULL,
    entitlement_key        varchar(128) NOT NULL,
    previous_expiry        timestamptz NOT NULL,
    new_expiry             timestamptz NOT NULL,
    status                 varchar(32) NOT NULL,
    applied_at_utc         timestamptz NOT NULL
);

CREATE UNIQUE INDEX ux_guide09_cs_applied_logical
    ON guide09_cs_applied_license_operations (logical_operation_id);

-- First apply succeeds:
INSERT INTO guide09_cs_entitlements
VALUES (gen_random_uuid(), 'ent-cloud-1', 'cust-1', 'CS-SEC', 'Security', 'CloudShield Pro',
        '2026-12-01 00:00:00+00');

INSERT INTO guide09_cs_applied_license_operations
VALUES (
    gen_random_uuid(),
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    gen_random_uuid(),
    'ent-cloud-1',
    '2026-12-01 00:00:00+00',
    '2027-03-01 00:00:00+00',
    'Applied',
    now());

-- Replay (selective retry keeps LogicalOperationId, new AttemptId):
INSERT INTO guide09_cs_applied_license_operations
VALUES (
    gen_random_uuid(),
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',  -- SAME logical id
    gen_random_uuid(),                       -- new attempt
    'ent-cloud-1',
    '2026-12-01 00:00:00+00',
    '2027-03-01 00:00:00+00',
    'Applied',
    now())
ON CONFLICT (logical_operation_id) DO NOTHING
RETURNING id;  -- empty → already applied → applier returns success without re-extend

-- NetworkGuard sketch (same uniqueness rule on licenses):
DROP TABLE IF EXISTS guide09_ng_applied_license_operations CASCADE;
DROP TABLE IF EXISTS guide09_ng_licenses CASCADE;

CREATE TABLE guide09_ng_licenses (
    id                   uuid PRIMARY KEY,
    license_id           varchar(128) NOT NULL UNIQUE,
    tenant_external_id   varchar(128) NOT NULL,
    sku_code             varchar(64) NOT NULL,
    license_group        varchar(64) NULL,
    valid_until_utc      timestamptz NOT NULL
);

CREATE TABLE guide09_ng_applied_license_operations (
    id                     uuid PRIMARY KEY,
    logical_operation_id   uuid NOT NULL UNIQUE,
    attempt_id             uuid NOT NULL,
    license_id             varchar(128) NOT NULL,
    previous_expiry        timestamptz NOT NULL,
    new_expiry             timestamptz NOT NULL,
    status                 varchar(32) NOT NULL,
    applied_at_utc         timestamptz NOT NULL
);


-- =============================================================================
-- 14. EXPLAIN thoughts — entitlement search & claim paths
-- =============================================================================
-- 14.1 Outbox claim — expect Index Scan on (status, available_at_utc) then
--      row locks. SKIP LOCKED means EXPLAIN won't show wait edges; under load
--      look at pg_locks / pg_stat_activity for lock contention instead.
EXPLAIN (COSTS OFF)
SELECT id
FROM guide09_outbox_messages
WHERE status = 'Pending' AND available_at_utc <= now()
ORDER BY available_at_utc
LIMIT 10;

-- 14.2 Pending approvals keyset — expect ordered scan using created_at index
--      (or filter + sort if pending set is small — ListPendingApprovals loads
--      a bounded pending set then pages in memory for role filter).
EXPLAIN (COSTS OFF)
SELECT id, created_at_utc
FROM guide09_approval_cases
WHERE status = 'Pending'
  AND (created_at_utc, id) > ('2026-11-01 10:00:01+00', 'a0000000-0000-0000-0000-000000000002')
ORDER BY created_at_utc, id
LIMIT 50;

-- 14.3 Entitlement search (simulator side)
-- Real discovery is HTTP GET to CloudShield/NetworkGuard (aggregation service),
-- not a cross-DB SQL join. Inside a simulator DB you typically filter by
-- customer_account_id / tenant_external_id with a supporting index.
CREATE INDEX ix_guide09_cs_entitlements_customer
    ON guide09_cs_entitlements (customer_account_id);

EXPLAIN (COSTS OFF)
SELECT entitlement_key, product_code, expires_at_utc
FROM guide09_cs_entitlements
WHERE customer_account_id = 'cust-1'
ORDER BY entitlement_key;

-- 14.4 Idempotent apply lookup — unique index = Index Only / unique Index Scan
EXPLAIN (COSTS OFF)
SELECT id, status, new_expiry
FROM guide09_cs_applied_license_operations
WHERE logical_operation_id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';

-- 14.5 Reconciliation mismatch check (conceptual platform SQL):
-- Find local Success in fulfillment_item_results while saga outcome still Failed
-- (ReconciliationGate also encodes this in application code).
-- EXPLAIN joins on logical_operation_id — both sides uniquely indexed.


-- =============================================================================
-- 15. Interview drills (answer from this guide + repo paths)
-- =============================================================================
-- Q: Why FOR UPDATE SKIP LOCKED instead of SELECT FOR UPDATE?
-- A: Multiple OutboxPublisher workers must claim disjoint batches without
--    queueing behind each other. SKIP LOCKED is the Postgres work-queue idiom.
--    Path: src/EntitleX.Infrastructure/Outbox/OutboxClaimer.cs
--
-- Q: Where is idempotency enforced in the database?
-- A: processed_messages PK; api_idempotency_records PK; retry_item_claims PK;
--    unique logical_operation_id on saga outcomes, fulfillment results, and
--    simulator applied_license_operations.
--
-- Q: Why timestamptz everywhere?
-- A: Domain uses DateTimeOffset Utc; saga timeouts and leases compare absolute
--    instants. Local time zones do not belong in fulfillment ledgers.
--
-- Q: Why three databases?
-- A: Control plane vs vendor simulators — separate DbContexts, separate failure
--    domains, anti-corruption via HTTP adapters (ProviderHttpClientRegistration).
--
-- Q: How does selective retry avoid double-extend?
-- A: Preserve LogicalOperationId; provider unique index makes re-apply a no-op;
--    only Failed items are copied (CreateSelectiveRetry); Uncertain blocked by
--    ReconciliationGate.
--
-- Q: OFFSET vs keyset?
-- A: Pending approvals / audit prefer (created_at, id) / (occurred_at, id)
--    cursors — see ListPendingApprovalsQueryHandler and performance.md.

-- =============================================================================
-- Cleanup (optional sandbox)
-- =============================================================================
-- DROP TABLE IF EXISTS guide09_type_lab CASCADE;
-- DROP TABLE IF EXISTS guide09_license_extension_request_items CASCADE;
-- DROP TABLE IF EXISTS guide09_retry_history CASCADE;
-- DROP TABLE IF EXISTS guide09_license_extension_requests CASCADE;
-- DROP TABLE IF EXISTS guide09_approval_decisions CASCADE;
-- DROP TABLE IF EXISTS guide09_approval_stages CASCADE;
-- DROP TABLE IF EXISTS guide09_approval_cases CASCADE;
-- DROP TABLE IF EXISTS guide09_saga_item_outcomes CASCADE;
-- DROP TABLE IF EXISTS guide09_saga_transitions CASCADE;
-- DROP TABLE IF EXISTS guide09_license_extension_sagas CASCADE;
-- DROP TABLE IF EXISTS guide09_outbox_messages CASCADE;
-- DROP TABLE IF EXISTS guide09_processed_messages CASCADE;
-- DROP TABLE IF EXISTS guide09_fulfillment_item_results CASCADE;
-- DROP TABLE IF EXISTS guide09_api_idempotency_records CASCADE;
-- DROP TABLE IF EXISTS guide09_retry_item_claims CASCADE;
-- DROP TABLE IF EXISTS guide09_audit_events CASCADE;
-- DROP TABLE IF EXISTS guide09_approval_reminder_records CASCADE;
-- DROP TABLE IF EXISTS guide09_expiration_notification_records CASCADE;
-- DROP TABLE IF EXISTS guide09_cs_applied_license_operations CASCADE;
-- DROP TABLE IF EXISTS guide09_cs_entitlements CASCADE;
-- DROP TABLE IF EXISTS guide09_ng_applied_license_operations CASCADE;
-- DROP TABLE IF EXISTS guide09_ng_licenses CASCADE;

-- END guide 09

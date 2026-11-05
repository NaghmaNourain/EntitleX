# Scheduled Lambdas (Phase 9)

EntitleX ships two scheduled jobs under `src/lambdas/`:

| Project | Purpose |
|---------|---------|
| `EntitleX.ApprovalReminder` | Pending approval ≥ threshold → email responsible role → record reminder → after delay apply **configurable** terminal saga state |
| `EntitleX.ExpirationNotification` | Near-expiry entitlements → email Renewal Representatives (customer / product / refs / expiry / portal link) |

Email uses the same `Email:*` stack as the API: **SMTP → Mailpit** locally, or **`Email:Provider=Ses`** → LocalStack / AWS SES.

## Local host runners

Both projects are executable hosts. Default behaviour is **run once and exit** (EventBridge-compatible).

```bash
# Approval reminders (Mailpit SMTP by default)
dotnet run --project src/lambdas/EntitleX.ApprovalReminder

# Expiration — CloudShield daily schedule stub
dotnet run --project src/lambdas/EntitleX.ExpirationNotification -- --provider CloudShield

# Expiration — NetworkGuard weekly schedule stub
dotnet run --project src/lambdas/EntitleX.ExpirationNotification -- --provider NetworkGuard

# Optional soak loops
dotnet run --project src/lambdas/EntitleX.ApprovalReminder -- --loop
dotnet run --project src/lambdas/EntitleX.ExpirationNotification -- --loop --provider CloudShield
```

Requires platform Postgres (`ConnectionStrings:EntitleXPlatform`), migrated through `ScheduledNotifications`, and (for expiry) reachable provider simulators.

## Configuration

### ApprovalReminder

| Key | Default | Meaning |
|-----|---------|---------|
| `ApprovalReminder:PendingThresholdDays` | `7` | Pending stage age before reminder |
| `ApprovalReminder:EscalationDelayHours` | `24` | Delay after recorded reminder before terminal transition |
| `ApprovalReminder:TerminalSagaState` | `Escalated` | Saga state applied (parsed as `SagaState`; **not hardcoded** in the Lambda) |
| `ApprovalReminder:BatchSize` | `100` | Max pending cases per pass |

### ExpirationNotification

| Key | Default | Meaning |
|-----|---------|---------|
| `ExpirationNotification:CloudShieldWarningDays` | `30` | CloudShield warning horizon |
| `ExpirationNotification:NetworkGuardWarningDays` | `45` | NetworkGuard warning horizon |
| `ExpirationNotification:CloudShieldScanIntervalHours` | `24` | Cadence / window bucket (daily) |
| `ExpirationNotification:NetworkGuardScanIntervalHours` | `168` | Cadence / window bucket (weekly) |
| `ExpirationNotification:CustomerWatchlist` | demo CUST-* | Customers to discover (providers have no list-all) |

## Idempotency

- **Reminders:** unique `(approval_case_id, stage_order)` + `window_key` in `approval_reminder_records`
- **Expiry emails:** unique `window_key` in `expiration_notification_records`  
  (`expiry:{provider}:{entitlementId}:{day|ISO-week}`)

Re-running the same schedule window is a no-op.

## EventBridge schedule stubs (Terraform in Phase 12)

```json
{
  "ApprovalReminder": {
    "ScheduleExpression": "rate(1 hour)",
    "Target": "EntitleX.ApprovalReminder::HandlerAsync",
    "Description": "Scan pending approvals; remind at ≥7d; escalate after reminder+24h"
  },
  "ExpirationCloudShield": {
    "ScheduleExpression": "rate(24 hours)",
    "Target": "EntitleX.ExpirationNotification::HandlerAsync",
    "Input": { "providers": ["CloudShield"] }
  },
  "ExpirationNetworkGuard": {
    "ScheduleExpression": "rate(7 days)",
    "Target": "EntitleX.ExpirationNotification::HandlerAsync",
    "Input": { "providers": ["NetworkGuard"] }
  }
}
```

Stub file also checked in at `infra/eventbridge/schedules.stub.json`. Full Lambda packaging / IAM / VPC wiring is deferred to Phase 12 Terraform.

## Related

- `docs/implementation-progress.md` — Phase 9 status
- `docs/saga-workflow.md` — approval timeout hooks (Phase 8 worker still applies elapsed saga timeouts)
- Mailpit UI: `http://localhost:8025`

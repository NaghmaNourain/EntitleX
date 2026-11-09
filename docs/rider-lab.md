# Rider lab run configurations

Shared configs in [`.run/`](../.run/) start the local portfolio stack without treating the SPA as a .NET startup project.

## Use this

1. Free host ports if another stack owns them (`untitled-postgres-1` → `:5432`, `untitled-keycloak-1` → `:8080`):
   ```bash
   docker stop untitled-postgres-1 untitled-keycloak-1 2>/dev/null || true
   ```
2. In Rider, select **Lab Full Stack** → Run.
3. Open http://localhost:5173 (API `:5080`, CloudShield `127.0.0.1:5081`, NetworkGuard `127.0.0.1:5082`).

## What Lab Full Stack starts

| Piece | Port / role |
|-------|-------------|
| Lab Infra (Compose) | Postgres 5432, Keycloak 8080, Kafka 9092, LocalStack 4566, Mailpit, Jaeger, Prometheus, Grafana |
| CloudShield.Api | `127.0.0.1:5081` |
| NetworkGuard.Api | `127.0.0.1:5082` |
| EntitleX.Api | `localhost:5080` |
| frontend | Vite `localhost:5173` |
| OutboxPublisher / FulfillmentWorker / ResultsWorker / ReconciliationWorker / NotificationService | messaging + repair |

Not included (run-once): ApprovalReminder / ExpirationNotification lambdas.

## Smaller compounds

- **API + Frontend** — UI against an already-running infra + simulators
- Individual `.run/*.run.xml` entries for single-process debug

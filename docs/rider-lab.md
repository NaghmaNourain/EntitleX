# Rider lab run configurations

Shared configs live in both [`.run/`](../.run/) and [`.idea/runConfigurations/`](../.idea/runConfigurations/) (the latter is what most Rider versions show in the run dropdown).

## Use this

1. Be on the branch that contains these files and pull:
   ```bash
   git fetch origin
   git checkout cursor/ai-request-insights-040d
   git pull
   ls .idea/runConfigurations/Lab_Full_Stack.xml .run/Lab_Full_Stack.run.xml
   ```
2. In Rider: **File → Close Solution**, then reopen `EntitleX.sln` (or **File → Invalidate Caches** if the dropdown is stale).
3. Free host ports if another stack owns them (`untitled-postgres-1` → `:5432`, `untitled-keycloak-1` → `:8080`):
   ```bash
   docker stop untitled-postgres-1 untitled-keycloak-1 2>/dev/null || true
   ```
4. Run-configuration dropdown (top right) → **Lab Full Stack** → Run.  
   This is a **Shell** config (same type as Lab Infra) that runs `scripts/lab-full-stack.sh`.  
   Also listed under **Run → Edit Configurations…**.
5. Open http://localhost:5173 (API `:5080`, CloudShield `127.0.0.1:5081`, NetworkGuard `127.0.0.1:5082`).
6. Observability UIs (Compose): Grafana http://localhost:3001 (admin/admin), Jaeger http://localhost:16686, Prometheus targets http://localhost:9090/targets — see [observability.md](observability.md).

## What Lab Full Stack starts

`scripts/lab-full-stack.sh` brings up Compose, then starts these processes until you Ctrl+C:

| Piece | Port / role |
|-------|-------------|
| Lab Infra (Compose) | Postgres 5432, Keycloak 8080, Kafka 9092, LocalStack 4566, Mailpit, Jaeger, Prometheus, Grafana |
| CloudShield.Api | `127.0.0.1:5081` |
| NetworkGuard.Api | `127.0.0.1:5082` |
| EntitleX.Api | `localhost:5080` |
| frontend | Vite `localhost:5173` |
| OutboxPublisher / FulfillmentWorker / ResultsWorker / ReconciliationWorker / NotificationService | messaging + repair |

Not included (run-once): ApprovalReminder / ExpirationNotification lambdas.

## Other configs

- **Lab Infra (Compose)** — Docker only
- **Lab Full Stack (Compound)** — multi-debug compound (may be hidden in some Rider setups; prefer Shell **Lab Full Stack**)
- **API + Frontend** — UI against already-running infra + simulators
- Individual Launch Settings / npm configs for single-process debug

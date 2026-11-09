#!/usr/bin/env bash
# One-shot local lab: Compose infra + simulators + API + workers + Vite SPA.
# Intended for Rider "Lab Full Stack" (Shell) run configuration.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

PIDS=()

cleanup() {
  echo ""
  echo "==> stopping lab processes (${#PIDS[@]} children)"
  for pid in "${PIDS[@]:-}"; do
    kill "$pid" 2>/dev/null || true
  done
  wait 2>/dev/null || true
}
trap cleanup EXIT INT TERM

if docker ps --format '{{.Names}}' 2>/dev/null | grep -qE '^untitled-(postgres|keycloak)'; then
  echo "WARN: untitled-* containers may own :5432 / :8080."
  echo "      Run: docker stop untitled-postgres-1 untitled-keycloak-1"
fi

"$ROOT/scripts/lab-infra-up.sh"

start() {
  local name="$1"
  shift
  echo "==> starting $name"
  "$@" &
  PIDS+=("$!")
}

start "CloudShield.Api" \
  dotnet run --project "$ROOT/src/simulators/CloudShield/CloudShield.Api/CloudShield.Api.csproj" --launch-profile http
start "NetworkGuard.Api" \
  dotnet run --project "$ROOT/src/simulators/NetworkGuard/NetworkGuard.Api/NetworkGuard.Api.csproj" --launch-profile http
start "EntitleX.Api" \
  dotnet run --project "$ROOT/src/EntitleX.Api/EntitleX.Api.csproj" --launch-profile http
start "OutboxPublisher" \
  dotnet run --project "$ROOT/src/workers/EntitleX.OutboxPublisher/EntitleX.OutboxPublisher.csproj"
start "FulfillmentWorker" \
  dotnet run --project "$ROOT/src/workers/EntitleX.FulfillmentWorker/EntitleX.FulfillmentWorker.csproj"
start "ResultsWorker" \
  dotnet run --project "$ROOT/src/workers/EntitleX.ResultsWorker/EntitleX.ResultsWorker.csproj"
start "ReconciliationWorker" \
  dotnet run --project "$ROOT/src/workers/EntitleX.ReconciliationWorker/EntitleX.ReconciliationWorker.csproj"
start "NotificationService" \
  dotnet run --project "$ROOT/src/workers/EntitleX.NotificationService/EntitleX.NotificationService.csproj"

if [[ ! -d "$ROOT/frontend/node_modules" ]]; then
  echo "==> frontend/node_modules missing — running npm ci"
  (cd "$ROOT/frontend" && npm ci)
fi
start "frontend" bash -lc "cd \"$ROOT/frontend\" && npm run dev"

echo ""
echo "==> Lab Full Stack running"
echo "    SPA          http://localhost:5173"
echo "    API          http://localhost:5080"
echo "    CloudShield  http://127.0.0.1:5081"
echo "    NetworkGuard http://127.0.0.1:5082"
echo "    Grafana      http://localhost:3001 (admin/admin)"
echo "    Jaeger       http://localhost:16686"
echo "    Prometheus   http://localhost:9090/targets"
echo "    Stop with Ctrl+C in this Run tool window."
wait

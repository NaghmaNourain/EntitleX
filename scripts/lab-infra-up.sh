#!/usr/bin/env bash
# Start Docker Compose lab dependencies (Postgres, Kafka, Keycloak, LocalStack, Mailpit, OTel stack).
# Idempotent: safe to re-run. Does not start API / SPA / simulators / workers.
# Forces recreate of Prometheus/Grafana/Jaeger so scrape + datasource provisioning always match the repo.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if ! command -v docker >/dev/null 2>&1; then
  echo "docker not found on PATH" >&2
  exit 1
fi

echo "==> docker compose up -d (EntitleX lab infra)"
docker compose up -d

echo "==> refresh observability stack (Prometheus scrape + Grafana Jaeger DS + dashboards)"
# Recreate so bind-mounted prometheus.yml / provisioning / dashboards are applied even if
# containers were already running from an older checkout.
docker compose up -d --force-recreate --no-deps jaeger prometheus grafana

# Hot-reload Prometheus config when lifecycle API is enabled (no-op if container just started).
if curl -sf -X POST http://127.0.0.1:9090/-/reload >/dev/null 2>&1; then
  echo "prometheus config reloaded"
else
  echo "prometheus reload skipped (not ready yet or lifecycle unavailable)"
fi

echo "==> waiting for postgres health"
for _ in $(seq 1 60); do
  if docker compose exec -T postgres pg_isready -U entitlex -d postgres >/dev/null 2>&1; then
    echo "postgres is ready"
    break
  fi
  sleep 1
done

echo "==> waiting for prometheus ready"
for _ in $(seq 1 30); do
  if curl -sf http://127.0.0.1:9090/-/ready >/dev/null 2>&1; then
    echo "prometheus is ready"
    break
  fi
  sleep 1
done

echo "==> published ports (expect 5432 and 8080 on entitlex-* containers)"
docker ps --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}' | grep -E 'entitlex-|NAMES' || true

if ! docker ps --format '{{.Names}} {{.Ports}}' | grep -q 'entitlex-postgres.*0.0.0.0:5432'; then
  echo "WARN: entitlex-postgres is not publishing host :5432 — another container may own the port (e.g. untitled-postgres-1)." >&2
  echo "      Stop the other stack, then re-run this script." >&2
fi

if ! docker ps --format '{{.Names}} {{.Ports}}' | grep -q 'entitlex-keycloak.*0.0.0.0:8080'; then
  echo "WARN: entitlex-keycloak is not publishing host :8080 — another container may own the port." >&2
fi

echo ""
echo "==> lab infra up — observability:"
echo "    Grafana     http://localhost:3001  (admin/admin) — Prometheus + Jaeger datasources"
echo "    Jaeger UI   http://localhost:16686"
echo "    Prometheus  http://localhost:9090/targets  (Api/sims 5080-5082, workers 9180-9184)"

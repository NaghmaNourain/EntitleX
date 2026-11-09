# EntitleX container images

Build from the **repository root**:

```bash
# API
docker build -f infra/docker/images/Dockerfile.api -t entitlex-api:local .

# Workers
docker build -f infra/docker/images/Dockerfile.worker \
  --build-arg PROJECT_PATH=src/workers/EntitleX.OutboxPublisher/EntitleX.OutboxPublisher.csproj \
  --build-arg DLL_NAME=EntitleX.OutboxPublisher.dll \
  -t entitlex-outbox-publisher:local .

docker build -f infra/docker/images/Dockerfile.worker \
  --build-arg PROJECT_PATH=src/workers/EntitleX.FulfillmentWorker/EntitleX.FulfillmentWorker.csproj \
  --build-arg DLL_NAME=EntitleX.FulfillmentWorker.dll \
  -t entitlex-fulfillment-worker:local .

docker build -f infra/docker/images/Dockerfile.worker \
  --build-arg PROJECT_PATH=src/workers/EntitleX.ReconciliationWorker/EntitleX.ReconciliationWorker.csproj \
  --build-arg DLL_NAME=EntitleX.ReconciliationWorker.dll \
  -t entitlex-reconciliation-worker:local .

docker build -f infra/docker/images/Dockerfile.worker \
  --build-arg PROJECT_PATH=src/workers/EntitleX.NotificationService/EntitleX.NotificationService.csproj \
  --build-arg DLL_NAME=EntitleX.NotificationService.dll \
  -t entitlex-notification-service:local .

# Simulators
docker build -f infra/docker/images/Dockerfile.simulator \
  --build-arg PROJECT_PATH=src/simulators/CloudShield/CloudShield.Api/CloudShield.Api.csproj \
  --build-arg DLL_NAME=CloudShield.Api.dll \
  -t entitlex-cloudshield:local .

docker build -f infra/docker/images/Dockerfile.simulator \
  --build-arg PROJECT_PATH=src/simulators/NetworkGuard/NetworkGuard.Api/NetworkGuard.Api.csproj \
  --build-arg DLL_NAME=NetworkGuard.Api.dll \
  -t entitlex-networkguard:local .

# Frontend (listens on :8080 as non-root `nginx` user)
docker build -f infra/docker/images/Dockerfile.frontend -t entitlex-frontend:local .
```

All five Dockerfiles run as a non-root user and define a `HEALTHCHECK` (HTTP `/health` or `/healthz` for hosts; process/file checks for workers/lambdas).

Credentials are **never** baked into images. Inject via ECS task secrets / env from Secrets Manager at runtime.

# Deployment

EntitleX supports two footprints: **local Compose** (primary for development) and **optional AWS Terraform** (lab/staging packaging). Do not treat Terraform as required for day-to-day work.

## Compose vs AWS

| Concern | Local Compose | Optional AWS (`infra/terraform/`) |
|---------|---------------|-----------------------------------|
| Postgres | `postgres:16` `:5432` | RDS PostgreSQL (encrypted; password → Secrets Manager) |
| Kafka | Apache Kafka KRaft `:9092` | **Not provisioned** — bring your own bootstrap via Secrets Manager |
| SQS / SES | LocalStack `:4566` | Real SQS + SES modules |
| Email UI | Mailpit `:8025` | SES |
| Tracing / metrics | Jaeger, Prometheus, Grafana | CloudWatch logs/alarms; OTel → ADOT/X-Ray optional |
| App hosts | `dotnet run` / Vite | ECS Fargate (gated by `enable_ecs_services`) |
| Lambdas | Local run-once hosts | Lambda + EventBridge (gated) |
| Cost | Workstation only | See [infra/terraform/COST.md](../infra/terraform/COST.md) |

### Local path (recommended)

```bash
docker compose up -d
# then run API, simulators, workers, lambdas, frontend — see root README
```

Compose services: Keycloak, Postgres, Kafka, LocalStack, Mailpit, Jaeger, Prometheus, Grafana.  
App processes are **not** started by Compose by default.

### Optional AWS path

```bash
cd infra/terraform
cp terraform.tfvars.example terraform.tfvars   # non-secret values only
terraform init
terraform plan
# terraform apply   # only when you intend to spend AWS budget
```

Modules: VPC, RDS, SQS, SNS/SES, ECS, Lambda, EventBridge, IAM, Secrets Manager shells, CloudWatch, S3 audit. Details: [infra/terraform/README.md](../infra/terraform/README.md).

**Destroy idle labs** (`terraform destroy`). Prefer Compose for daily development. NAT Gateway is the largest idle cost driver — disable when private egress is unnecessary.

## Container images

Dockerfiles under `infra/docker/images/` (build from repo root). See [infra/docker/images/README.md](../infra/docker/images/README.md).

| Dockerfile | Targets |
|------------|---------|
| `Dockerfile.api` | EntitleX.Api |
| `Dockerfile.worker` | OutboxPublisher / FulfillmentWorker / ReconciliationWorker / NotificationService (build-args) |
| `Dockerfile.simulator` | CloudShield / NetworkGuard |
| `Dockerfile.lambda` | ApprovalReminder / ExpirationNotification |
| `Dockerfile.frontend` | Vite build + nginx |

CI builds a subset of images without pushing (see below).

## CORS and SPA hosting

- **CORS is Development-only** (`FrontendDev` origins `localhost:5173` / `127.0.0.1:5173`). Non-Development hosts do **not** register browser CORS policies.
- Production / Compose SPA assumes **same-origin** behind nginx (or ALB path routing): the browser talks to one origin; nginx serves the Vite build and proxies `/api` + `/health*` to the API. See `infra/docker/images/nginx-frontend.conf` (extend with `location /api/` → API upstream when co-hosting).
- Cross-origin SPA + API in Production is intentionally unsupported without a new ADR.

## EF Core migrations (Production)

`Database:MigrateOnStartup` defaults to **`false`** in `appsettings.json` (Development sets it `true` for lab convenience).

Production / staging path:

1. Keep `Database:MigrateOnStartup=false` on API tasks.
2. Run migrations from CI/CD or a one-shot migrate job before/alongside deploy:

```bash
dotnet ef database update \
  --project src/EntitleX.Infrastructure \
  --startup-project src/EntitleX.Api \
  --connection "$ENTITLEX_PLATFORM_CONNECTION_STRING"
```

3. Confirm readiness with `GET /health/ready` (status + dependency names only).

## Secrets

**Never commit secrets** (JWT signing keys, AWS access keys, connection-string passwords, SES credentials).

| Secret | Local | AWS |
|--------|-------|-----|
| `Oidc:Authority` / client secrets | Keycloak Compose (`admin`/`admin`, user `demo`) — lab only | Entra/Auth0/Cognito tenant; set `Oidc__*` via env / Secrets Manager (never commit) |
| `Jwt:SigningKey` / `Jwt__SigningKey` | Dev appsettings only when DevToken enabled (≥32 chars) | Not required when OIDC-only; DevToken must be off in Production |
| Connection strings | Compose defaults (`entitlex`/`entitlex`) | Compose from RDS secret → `entitlex/connection-strings` JSON |
| SQS/SES keys | LocalStack `test`/`test` | Task/Lambda IAM roles — no static keys in images |
| Kafka bootstrap | `localhost:9092` | Secret `entitlex/kafka` (MSK/self-managed not created by Terraform) |
| Grafana / Postgres / Keycloak Compose | `admin`/`admin`, `entitlex`/`entitlex` | Lab only; change for any shared environment |

LocalStack enables Secrets Manager in Compose but app code uses config/env locally.

## CI/CD

### Continuous integration — `.github/workflows/ci.yml`

Triggers: `push`, `pull_request` (concurrency cancel-in-progress).

| Job | What |
|-----|------|
| `build-test` | Restore, vulnerable-package audit (fail High+), Release build (`TreatWarningsAsErrors`), unit tests with XPlat coverage (soft Domain/Application floor), integration + API WebApplicationFactory + E2E scaffolding |
| `static-analysis` | Hard `dotnet format --verify-no-changes`, analyzer build, `terraform fmt -check` |
| `gitleaks` | Secret scan (`gitleaks-action`) |
| `frontend` | Node 22, `npm install`, oxlint, Vitest unit/a11y tests, `npm run build` |
| `docker-images` | Build API / worker / CloudShield / frontend (`push: false`); Trivy CRITICAL/HIGH on each loaded image; images run as non-root + `HEALTHCHECK` |

Also: `.github/workflows/codeql.yml` (C# + JS/TS on `main` / PRs + weekly) and `.github/dependabot.yml` (NuGet, npm, Actions, Docker — weekly).

Integration tests use `DockerFact` — skip cleanly when Docker is unavailable.

### Manual AWS deploy — `.github/workflows/deploy-aws.yml`

`workflow_dispatch` only (environment protection recommended). OIDC → `AWS_DEPLOY_ROLE_ARN`. Plan by default; apply gated by input.

| Input | Default | Purpose |
|-------|---------|---------|
| `push_images` | `false` | Build/push API, workers, simulators to ECR |
| `enable_ecs_services` | `false` | Create/update ECS services |
| `enable_alb` | `false` | Public ALB (cost) + attach API target group when ECS is on |
| `apply` | `false` | Run `terraform apply` |

Populate Secrets Manager before enabling ECS. ECR repositories are created when `enable_ecr=true` (default).

## EventBridge schedules (stub + Terraform)

Stub: `infra/eventbridge/schedules.stub.json`

| Rule | Schedule | Target |
|------|----------|--------|
| `entitlex-approval-reminder` | `rate(1 hour)` | ApprovalReminder |
| `entitlex-expiration-cloudshield` | `rate(24 hours)` | ExpirationNotification `{ providers: ["CloudShield"] }` |
| `entitlex-expiration-networkguard` | `rate(7 days)` | `{ providers: ["NetworkGuard"] }` |

Local hosts: [lambdas.md](lambdas.md).

## Cost pointer

Read **[infra/terraform/COST.md](../infra/terraform/COST.md)** before `terraform apply`. Guidance: tear down when idle; keep ECS/Lambda gated off until images and secrets exist; avoid NAT unless required.

## HA / DR

Lab defaults keep `rds_multi_az=false`. For a non-lab checklist see `infra/terraform/terraform.tfvars.staging.example` and the operator runbook **[runbooks/dr-rds.md](runbooks/dr-rds.md)** (RPO/RTO guidance, Multi-AZ enable steps, snapshot restore drill).

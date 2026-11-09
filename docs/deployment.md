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
| `build-test` | Restore, Release build, unit + integration + E2E scaffolding tests |
| `static-analysis` | Soft `dotnet format` gate, analyzer build, `terraform fmt -check` |
| `frontend` | Node 22, `npm ci`, `npm run build` |
| `docker-images` | Build API / sample worker / CloudShield / frontend images (`push: false`) |

Integration tests use `DockerFact` — skip cleanly when Docker is unavailable.

### Manual AWS deploy — `.github/workflows/deploy-aws.yml`

`workflow_dispatch` only (environment protection recommended). OIDC → `AWS_DEPLOY_ROLE_ARN`. Plan by default; apply gated by input. Reminder step: populate Secrets Manager and push images before `enable_ecs_services=true`.

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

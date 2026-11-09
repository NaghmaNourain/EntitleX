# 14 — Docker & Containers Complete Guide (EntitleX)

Educational walkthrough of **EntitleX** local Docker Compose, multi-stage image builds, init scripts, networking/volumes/healthchecks, how API/workers/simulators run, and the **ECS Fargate** production packaging shape.

> EntitleX does **not** use Kubernetes. Local = Docker Compose; optional cloud = ECS Fargate (`infra/terraform/modules/ecs`). This guide covers Docker deeply from the real repo, then a short **Why not Kubernetes (yet)** section and a light **K8s → ECS** concept map.

---

## Table of contents

1. [Mental model: what Compose gives EntitleX](#1-mental-model-what-compose-gives-entitlex)
2. [Walking `docker-compose.yml` service by service](#2-walking-docker-composeyml-service-by-service)
3. [Init scripts: Postgres databases & LocalStack SQS](#3-init-scripts-postgres-databases--localstack-sqs)
4. [Networking, volumes, healthchecks](#4-networking-volumes-healthchecks)
5. [Multi-stage Dockerfiles (`.NET` + React)](#5-multi-stage-dockerfiles-net--react)
6. [How API, workers, and simulators actually run](#6-how-api-workers-and-simulators-actually-run)
7. [Observability stack: Jaeger, Prometheus, Grafana](#7-observability-stack-jaeger-prometheus-grafana)
8. [ECS Fargate as the production shape](#8-ecs-fargate-as-the-production-shape)
9. [Why not Kubernetes (yet)](#9-why-not-kubernetes-yet)
10. [Light K8s → ECS concept mapping](#10-light-k8s--ecs-concept-mapping)
11. [Day-2 ops checklist & interview prompts](#11-day-2-ops-checklist--interview-prompts)

**Real paths cited throughout**

| Path | Role |
|------|------|
| `docker-compose.yml` | Local infra services |
| `infra/docker/postgres/init-databases.sh` | Create `entitlex_platform`, `cloudshield`, `networkguard` |
| `infra/docker/localstack/init-sqs.sh` | Create `entitlex-fulfillment` + DLQ |
| `infra/docker/images/Dockerfile.*` | API, worker, simulator, lambda, frontend |
| `infra/docker/images/nginx-frontend.conf` | SPA reverse-proxy hints |
| `infra/docker/prometheus/prometheus.yml` | Scrape API + worker metrics |
| `infra/docker/grafana/provisioning/` | Datasource + dashboards |
| `infra/terraform/modules/ecs/` | Fargate cluster/services |
| `docs/deployment.md`, `infra/terraform/COST.md` | Compose vs AWS + cost |

---

## 1. Mental model: what Compose gives EntitleX

EntitleX separates **infrastructure dependencies** from **application processes**.

```
┌──────────────────────────────────────────────────────────────────┐
│ docker compose up -d                                             │
│   Postgres │ Kafka (KRaft) │ LocalStack │ Mailpit                │
│   Jaeger   │ Prometheus    │ Grafana                             │
└──────────────────────────────────────────────────────────────────┘
         ▲                    ▲                    ▲
         │                    │                    │
   API :5080          workers :9180–9183     sims :5081/:5082
   (dotnet run)         (dotnet run)           (dotnet run)
         │
   frontend :5173 (Vite; proxies /api → API)
```

**Why this split?**

- Compose stays fast and cheap: no need to rebuild app images for every C# change.
- Integration tests use Testcontainers / `DockerFact` against the same dependency shapes.
- Optional AWS packaging mirrors the dependency set with managed services (RDS, SQS, SES, CloudWatch) and runs apps on **ECS Fargate** — not on a Kubernetes control plane.

**Golden rule:** credentials are never baked into images. Local uses Compose defaults / `appsettings.Development.json`; AWS injects `Jwt__SigningKey` and connection strings from Secrets Manager into the ECS task definition (`infra/terraform/modules/ecs/main.tf`).

---

## 2. Walking `docker-compose.yml` service by service

Project name: `entitlex` (`name: entitlex` at the top of the file).

### 2.1 `postgres` — three logical databases, one container

```yaml
image: postgres:16-alpine
ports: ["5432:5432"]
environment:
  POSTGRES_USER: entitlex
  POSTGRES_PASSWORD: entitlex
  POSTGRES_DB: postgres
volumes:
  - postgres_data:/var/lib/postgresql/data
  - ./infra/docker/postgres/init-databases.sh:/docker-entrypoint-initdb.d/01-init-databases.sh:ro
healthcheck:
  test: ["CMD-SHELL", "pg_isready -U entitlex -d postgres"]
```

**Why one Postgres container with three DBs?**

| Database | Owner | Purpose |
|----------|-------|---------|
| `entitlex_platform` | Control plane | Requests, approvals, saga, outbox, idempotency |
| `cloudshield` | CloudShield simulator | Vendor ledger + `AppliedLicenseOperation` claims |
| `networkguard` | NetworkGuard simulator | Same pattern, separate blast radius |

This mirrors the architectural choice that **simulators own their data** — see ADR modular API and provider uniqueness constraints in `docs/idempotency.md`.

### 2.2 `kafka` — Apache Kafka 3.8 in KRaft mode

```yaml
image: apache/kafka:3.8.0
KAFKA_PROCESS_ROLES: broker,controller   # no ZooKeeper
KAFKA_ADVERTISED_LISTENERS: PLAINTEXT://localhost:9092
KAFKA_NUM_PARTITIONS: 3
KAFKA_AUTO_CREATE_TOPICS_ENABLE: "true"
```

**EntitleX topic defaults** (`src/EntitleX.Messaging/Options/MessagingOptions.cs`):

| Topic | Purpose |
|-------|---------|
| `cloudshield.license-extension.commands` | Commands for CloudShield consumers |
| `networkguard.license-extension.commands` | Commands for NetworkGuard consumers |
| `entitlex.provider.results` | Completed/failed results → orchestrator |

Partition key for provider commands = **entitlement id** (ordering per entitlement). Consumer groups: `cloudshield-commands`, `networkguard-commands`, `entitlex-orchestrator-results`.

**Why KRaft locally?** Fewer moving parts than ZooKeeper for a portfolio lab. Production AWS path does **not** auto-provision MSK — bootstrap comes from Secrets Manager `entitlex/kafka`.

### 2.3 `localstack` — SQS (+ SES and friends) on `:4566`

```yaml
image: localstack/localstack:3.8
SERVICES: s3,sqs,sns,ses,lambda,secretsmanager,sts,iam
ports: ["4566:4566"]
volumes:
  - ./infra/docker/localstack/init-sqs.sh:/etc/localstack/init/ready.d/01-init-sqs.sh:ro
```

Creates:

- `entitlex-fulfillment-dlq`
- `entitlex-fulfillment` with `RedrivePolicy` `maxReceiveCount=5`, visibility 30s

App defaults: `Messaging:Sqs:ServiceUrl=http://localhost:4566`, access key `test`/`test` (**LocalStack only**). On AWS, workers use the ECS **task role** — no static keys.

### 2.4 `mailpit` — catch-all SMTP for notifications

| Port | Use |
|------|-----|
| `1025` | SMTP |
| `8025` | Web UI |

Local email provider points here; AWS tasks set `Email__Provider=Ses`.

### 2.5 `jaeger` — OpenTelemetry traces

| Port | Use |
|------|-----|
| `4317` | OTLP gRPC |
| `4318` | OTLP HTTP |
| `16686` | UI |

`COLLECTOR_OTLP_ENABLED=true`. API/workers export OTLP when configured (`docs/observability.md`).

### 2.6 `prometheus` + `grafana`

- Prometheus `:9090` scrapes API `/metrics` and workers `:9180–9183` via `host.docker.internal` (`extra_hosts: host-gateway`).
- Grafana `:3001` → container `:3000`, admin/admin, provisioning under `infra/docker/grafana/`.

---

## 3. Init scripts: Postgres databases & LocalStack SQS

### 3.1 `infra/docker/postgres/init-databases.sh`

Runs **once** on first volume init (`/docker-entrypoint-initdb.d/`):

```bash
SELECT 'CREATE DATABASE entitlex_platform' WHERE NOT EXISTS (...) \gexec
SELECT 'CREATE DATABASE cloudshield' ...
SELECT 'CREATE DATABASE networkguard' ...
```

**Gotcha:** if you change the script after the volume exists, it will **not** re-run. Reset with:

```bash
docker compose down -v   # destroys postgres_data — local data loss
docker compose up -d
```

Migrations still run from each app on startup / Development seed paths.

### 3.2 `infra/docker/localstack/init-sqs.sh`

Uses `awslocal` after LocalStack is ready:

1. Create DLQ `entitlex-fulfillment-dlq`
2. Read DLQ ARN
3. Create `entitlex-fulfillment` with redrive to DLQ (`maxReceiveCount=5`)

This matches AWS `infra/terraform/modules/sqs/main.tf` (SSE + DenyInsecureTransport on real AWS).

---

## 4. Networking, volumes, healthchecks

### 4.1 Networking

Compose creates a default bridge network for project `entitlex`. Services resolve each other by **service name** (`postgres`, `kafka`, `localstack`) **inside** the network.

Apps running on the **host** (`dotnet run`) use `localhost` published ports:

| Service | Host port |
|---------|-----------|
| Postgres | 5432 |
| Kafka | 9092 |
| LocalStack | 4566 |
| Mailpit SMTP/UI | 1025 / 8025 |
| Jaeger UI / OTLP | 16686 / 4317 / 4318 |
| Prometheus | 9090 |
| Grafana | 3001 |

**Kafka advertised listeners:** `PLAINTEXT://localhost:9092` is correct for host processes. If you later run workers *inside* Compose, you would need a dual-listener pattern (`localhost` + `kafka`) — EntitleX currently keeps apps on the host by default.

**Prometheus scrape of host metrics:** `extra_hosts: host.docker.internal:host-gateway` lets the Prometheus container reach API/workers on the host.

### 4.2 Volumes

| Volume | Purpose |
|--------|---------|
| `postgres_data` | Durable local DBs |
| `kafka_data` | Broker log dirs |
| `localstack_data` | LocalStack state |
| `mailpit_data` | Captured mail |
| `prometheus_data` | TSDB |
| `grafana_data` | Dashboards/users |

Named volumes survive `docker compose down` but not `down -v`.

### 4.3 Healthchecks

| Service | Check | Why it matters |
|---------|-------|----------------|
| Postgres | `pg_isready` | Apps/migrations wait for accept |
| Kafka | `kafka-topics.sh --list` | Topics API up (start_period 20s) |
| LocalStack | `curl .../_localstack/health` | Init scripts run after ready |

Healthchecks are how you avoid racey "connection refused" on cold start. EntitleX does not currently `depends_on: condition: service_healthy` for app containers because apps are host processes — but the checks still help humans and future Compose app services.

---

## 5. Multi-stage Dockerfiles (.NET + React)

All images build from the **repository root**. See `infra/docker/images/README.md`.

### 5.1 API — `Dockerfile.api`

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props global.json EntitleX.sln ./
COPY src/ ./src/
RUN dotnet restore src/EntitleX.Api/EntitleX.Api.csproj \
 && dotnet publish ... -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "EntitleX.Api.dll"]
```

**Why multi-stage?** SDK image is huge; final runtime image only needs published binaries. **Why copy `src/` broadly?** Shared projects (`Domain`, `Application`, `Infrastructure`, `Messaging`, `BuildingBlocks`) are referenced by the API — restore/publish needs them.

### 5.2 Workers — `Dockerfile.worker` (build-args)

```dockerfile
ARG PROJECT_PATH=src/workers/EntitleX.OutboxPublisher/...
ARG DLL_NAME=EntitleX.OutboxPublisher.dll
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
ENTRYPOINT ["sh", "-c", "dotnet \"$ENTITLEX_ENTRY_DLL\""]
```

One Dockerfile covers:

- `EntitleX.OutboxPublisher`
- `EntitleX.FulfillmentWorker`
- `EntitleX.ReconciliationWorker`
- `EntitleX.NotificationService`

Workers use the **runtime** base image (not `aspnet`) unless they host Kestrel metrics endpoints — still fine with `runtime` + explicit metric servers in-process.

### 5.3 Simulators — `Dockerfile.simulator`

Same pattern as workers but `aspnet:8.0` + `ASPNETCORE_URLS=http://+:8080` for CloudShield / NetworkGuard HTTP APIs.

### 5.4 Lambdas — `Dockerfile.lambda`

```dockerfile
FROM public.ecr.aws/lambda/dotnet:8 AS final
CMD ["EntitleX.ApprovalReminder::EntitleX.ApprovalReminder.Function::FunctionHandler"]
```

Also runnable as a console host locally via `dotnet run` (see `docs/lambdas.md`).

### 5.5 Frontend — `Dockerfile.frontend`

```dockerfile
FROM node:22-alpine AS build
RUN npm ci && npm run build

FROM nginx:1.27-alpine AS final
COPY infra/docker/images/nginx-frontend.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
```

**Concept:** static SPA in nginx; API remains a separate service. Local Vite (`npm run dev`) proxies `/api` to `:5080` instead.

### 5.6 Build commands (from repo root)

```bash
docker build -f infra/docker/images/Dockerfile.api -t entitlex-api:local .

docker build -f infra/docker/images/Dockerfile.worker \
  --build-arg PROJECT_PATH=src/workers/EntitleX.FulfillmentWorker/EntitleX.FulfillmentWorker.csproj \
  --build-arg DLL_NAME=EntitleX.FulfillmentWorker.dll \
  -t entitlex-fulfillment-worker:local .

docker build -f infra/docker/images/Dockerfile.frontend -t entitlex-frontend:local .
```

CI builds a subset with `push: false` (`.github/workflows/ci.yml` job `docker-images`).

---

## 6. How API, workers, and simulators actually run

### 6.1 Local recommended path (Compose deps + host apps)

```bash
docker compose up -d

dotnet run --project src/simulators/CloudShield/CloudShield.Api      # :5081
dotnet run --project src/simulators/NetworkGuard/NetworkGuard.Api    # :5082
dotnet run --project src/EntitleX.Api                                # :5080

dotnet run --project src/workers/EntitleX.OutboxPublisher            # metrics :9180
dotnet run --project src/workers/EntitleX.FulfillmentWorker          # :9181
dotnet run --project src/workers/EntitleX.ReconciliationWorker       # :9182
dotnet run --project src/workers/EntitleX.NotificationService        # :9183

cd frontend && npm ci && npm run dev                                 # :5173
```

### 6.2 Message path (containers + processes)

```
API (approval final)
  └─ same PG txn → outbox_messages
OutboxPublisher
  └─ SQS entitlex-fulfillment          (LocalStack container)
FulfillmentWorker
  └─ Kafka provider command topics     (Kafka container)
CloudShield / NetworkGuard consumers
  └─ apply + publish results topic
ProviderResultProcessor (fulfillment host)
  └─ saga settlement in entitlex_platform
```

### 6.3 What is *not* in Compose by default

API, workers, simulators, and Vite are **intentionally** omitted from `docker-compose.yml` so developers get fast inner-loop builds. Images exist for CI and ECS; local `dotnet run` remains primary.

---

## 7. Observability stack: Jaeger, Prometheus, Grafana

| Signal | Local sink | Notes |
|--------|------------|-------|
| Traces | Jaeger via OTLP | Correlation with `X-Correlation-ID` / W3C `traceparent` |
| Metrics | Prometheus | Low-cardinality labels only (`docs/observability.md`) |
| Logs | Serilog console | Enrich with Service / CorrelationId |
| Dashboards | Grafana provisioning | `infra/docker/grafana/` |

**Prometheus host scrape pattern:** because apps listen on the host, the Prometheus container must use `host.docker.internal` (Linux: `extra_hosts: host-gateway`).

On AWS lab: CloudWatch log groups `/entitlex/{env}/ecs/{service}` retention 14d + SQS depth/DLQ alarms (`modules/cloudwatch`).

---

## 8. ECS Fargate as the production shape

Optional Terraform (`enable_ecs_services=true`) creates:

1. **ECS cluster** with Container Insights  
2. **Task definitions** (Fargate, `awsvpc`, CPU/memory per service)  
3. **Services** in **private subnets**, `assign_public_ip=false`, shared app SG  
4. Env + Secrets injection (`Jwt__SigningKey`, `ConnectionStrings__EntitleXPlatform`)  
5. `awslogs` driver → CloudWatch  

Service map from `infra/terraform/main.tf` `locals.ecs_services`:

| Service | CPU | Memory | Port |
|---------|-----|--------|------|
| api | 512 | 1024 | 8080 |
| outbox | 256 | 512 | — |
| fulfillment | 512 | 1024 | — |
| reconciliation | 256 | 512 | — |
| notification | 256 | 512 | — |
| cloudshield | 256 | 512 | 8080 |
| networkguard | 256 | 512 | 8080 |

**12-factor expectations fulfilled by EntitleX packaging:**

- Config via env/secrets, not baked into the image  
- Stateless tasks (state in RDS / SQS / Kafka)  
- Logs to stdout → awslogs  
- Graceful shutdown via .NET generic host (`SIGTERM`)  

**Still follow-ups for a real production cut:** public ALB + ACM + DNS, MSK or equivalent, tighter SG egress, secret rotation, autoscaling policies.

---

## 9. Why not Kubernetes (yet)

EntitleX is a **portfolio control-plane** with a clear async topology (outbox → SQS → Kafka → simulators). For that shape, Compose + ECS Fargate wins on:

1. **Ops budget** — no control-plane upgrades, etcd, CNI, ingress controllers to learn/operate.  
2. **Cost narrative** — `COST.md` already warns about NAT/RDS/Fargate; EKS adds cluster $ + node/Fargate profile complexity.  
3. **Skill signal** — demonstrating transactional outbox, selective retry, reconciliation, and IAM least privilege matters more here than Helm charts.  
4. **Parity** — LocalStack + Compose already mirror SQS; ECS task roles map cleanly to the same IAM story.  
5. **Extraction path** — modular API ADR keeps the door open; if team/org standards later require K8s, workers/simulators are already separate deployables.

**When K8s would become attractive:** multi-cloud portability mandates, existing platform teams with Helm/operators/service mesh, or dozens of independently versioned services needing custom controllers. EntitleX is not there yet — and saying so in an interview is a Staff-level judgment call, not a gap.

---

## 10. Light K8s → ECS concept mapping

| Kubernetes idea | EntitleX / ECS analogue | Notes |
|-----------------|-------------------------|-------|
| Cluster | ECS cluster | `modules/ecs` `aws_ecs_cluster` |
| Namespace | Name prefix + environment | `name_prefix` / `environment` vars |
| Deployment | ECS service + task definition | desired_count, Fargate launch type |
| Pod | Task (awsvpc ENI) | One primary container per EntitleX service today |
| Service (ClusterIP/LB) | ALB / NLB (not in TF yet) / SG + port 8080 | Lab can be internal-only |
| Ingress | ALB listener rules + ACM | Documented as follow-up in COST.md |
| ConfigMap | Task `environment` block | Non-secret config |
| Secret | Secrets Manager → task `secrets` | JWT + connection strings |
| ServiceAccount + IRSA | ECS **task role** | Least-privilege AWS API access |
| kube-proxy / CNI | awsvpc ENI in VPC subnets | Private subnets + app SG |
| livenessProbe | ECS health check / ALB health (add when ALB exists) | App should expose `/health` |
| readinessProbe | ALB target health / ECS circuit | Drain on SIGTERM |
| HorizontalPodAutoscaler | ECS Service Auto Scaling | Not enabled in lab defaults |
| CronJob | EventBridge → Lambda | ApprovalReminder / ExpirationNotification |
| NetworkPolicy | Security groups | App SG ingress 8080 from VPC CIDR |
| PersistentVolume | RDS / SQS / S3 — not container disk | Tasks stay disposable |

**Compose → K8s vocabulary (local only):**

| Compose | Rough K8s |
|---------|-----------|
| `services:` | Deployments + Services |
| `volumes:` named | PVCs (EntitleX prefers managed stores) |
| `healthcheck` | probes |
| `depends_on` | ordering hints (K8s prefers readiness, not start order) |

---

## 11. Day-2 ops checklist & interview prompts

### Checklist

- [ ] `docker compose up -d` healthy (Postgres, Kafka, LocalStack)  
- [ ] Queues exist: `awslocal sqs list-queues` → fulfillment + DLQ  
- [ ] DBs exist: `\l` shows platform + two simulators  
- [ ] API `/health` and metrics scrapeable  
- [ ] OutboxPublisher moving `Pending` → `Published`  
- [ ] Images build from root Dockerfiles without embedding secrets  
- [ ] Before AWS: read `COST.md`; keep `enable_ecs_services=false` until images + secrets ready  
- [ ] Destroy idle Terraform labs  

### Interview prompts (EntitleX-grounded)

**Q: Why aren’t the API and workers in Compose?**  
A: Fast inner loop; Compose owns deps; images exist for CI/ECS.

**Q: How do you get three databases without three Postgres containers?**  
A: `init-databases.sh` creates `entitlex_platform`, `cloudshield`, `networkguard` on first boot — simulator isolation without ops sprawl locally.

**Q: Compose Kafka vs AWS?**  
A: Local KRaft broker; Terraform does not create MSK; inject bootstrap via Secrets Manager.

**Q: Compose vs ECS vs EKS for this portfolio?**  
A: Compose for free iteration; ECS Fargate for optional AWS packaging with task roles; EKS deferred — ops/cost not justified yet (see §9).

**Q: Where do container secrets come from on AWS?**  
A: Task definition `secrets` from Secrets Manager ARNs — values put outside Terraform.

---

## Appendix A — Port map (quick reference)

| Port | Process |
|------|---------|
| 5432 | Postgres |
| 9092 | Kafka |
| 4566 | LocalStack |
| 1025 / 8025 | Mailpit SMTP / UI |
| 4317 / 4318 / 16686 | Jaeger OTLP / UI |
| 9090 | Prometheus |
| 3001 | Grafana |
| 5080 | EntitleX.Api |
| 5081 / 5082 | CloudShield / NetworkGuard |
| 5173 | Vite frontend |
| 9180–9183 | Worker metrics |
| 8080 | Containerized API/sims (ECS / Docker) |

## Appendix B — Related docs

- `docs/deployment.md` — Compose vs AWS matrix  
- `docs/event-contracts.md` — SQS/Kafka sequence  
- `docs/observability.md` — OTel / Prometheus / Serilog  
- `docs/guides/13_AwsBackendDeepDive.cs` — Terraform modules deep dive  
- `infra/docker/images/README.md` — image build commands  
- `infra/terraform/COST.md` — spend controls  

---

*End of guide 14. EntitleX uses Docker Compose + ECS Fargate — not Kubernetes — by deliberate portfolio trade-off.*

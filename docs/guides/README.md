# EntitleX educational guides

Interview-style, EntitleX-grounded teaching artifacts. Most `.cs` files are **standalone and compilable** (inline types, no NuGet). Guide 09 is SQL commentary; guide 14 is Markdown (Compose + ECS — not Kubernetes).

| # | File | One-line description |
|---|------|----------------------|
| 01 | [01_CollectionsGuide.cs](01_CollectionsGuide.cs) | .NET collections framed around the license-extension control plane. |
| 02 | [02_PerformanceAndMemoryGuide.cs](02_PerformanceAndMemoryGuide.cs) | Performance & memory for outbox workers, bulk attach, and provider fan-out. |
| 03 | [03_ConcurrencyAndMultithreadingGuide.cs](03_ConcurrencyAndMultithreadingGuide.cs) | Async handlers, outbox claiming, SQS gates, selective-retry races. |
| 04 | [04_DataStructures.cs](04_DataStructures.cs) | Classic data structures applied to approvals, sagas, outbox, and retry. |
| 05 | [05_Algorithms.cs](05_Algorithms.cs) | Algorithms for license-extension control-plane problems. |
| 06 | [06_LLD_Complete_Guide.cs](06_LLD_Complete_Guide.cs) | Low-level design walkthrough of deep EntitleX components. |
| 07 | [07_AspNetCoreArchitectureGuide.cs](07_AspNetCoreArchitectureGuide.cs) | ASP.NET Core architecture tour mirroring `EntitleX.Api`. |
| 08 | [08_EfCoreGuide.cs](08_EfCoreGuide.cs) | EF Core patterns grounded in platform and simulator persistence. |
| 09 | [09_postgresql_complete_guide.sql](09_postgresql_complete_guide.sql) | PostgreSQL guide for platform + CloudShield/NetworkGuard DBs and `SKIP LOCKED`. |
| 10 | [10_ArchitectureAndDddGuide.cs](10_ArchitectureAndDddGuide.cs) | Modular monolith architecture and selective DDD aggregates. |
| 11 | [11_DistributedSystemsInDotNet.cs](11_DistributedSystemsInDotNet.cs) | Distributed-systems patterns mapped to Polly, outbox, saga, Kafka. |
| 12 | [12_CloudReliabilityGuide.cs](12_CloudReliabilityGuide.cs) | Reliability runbooks for Compose / ECS lab operations. |
| 13 | [13_AwsBackendDeepDive.cs](13_AwsBackendDeepDive.cs) | Optional AWS Terraform: VPC, RDS, ECS Fargate, SQS/DLQ, Lambda, IAM, cost. |
| 14 | [14_Docker_Kubernetes_Complete_Guide.md](14_Docker_Kubernetes_Complete_Guide.md) | Docker Compose + images deep dive; why not K8s; light K8s→ECS mapping. |
| 15 | [15_SearchKafkaMigrationGuide.cs](15_SearchKafkaMigrationGuide.cs) | Entitlement search aggregation + outbox→SQS→Kafka messaging migration. |
| 16 | [16_SecurityGuide.cs](16_SecurityGuide.cs) | JWT HS256, RBAC, approval authz, secrets hygiene, POST no-retry, OWASP map. |

## How to run a `.cs` guide

```bash
dotnet new console -n GuideDemo && cd GuideDemo
# replace Program.cs with the guide file
dotnet run
```

## Related docs

- [../architecture.md](../architecture.md), [../system-design.md](../system-design.md), [../deployment.md](../deployment.md)
- [../event-contracts.md](../event-contracts.md), [../idempotency.md](../idempotency.md), [../interview-guide.md](../interview-guide.md)
- [../../infra/terraform/COST.md](../../infra/terraform/COST.md), [../../infra/docker/images/README.md](../../infra/docker/images/README.md)

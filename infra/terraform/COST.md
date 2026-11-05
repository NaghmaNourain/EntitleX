# EntitleX optional AWS cost notes

Terraform under `infra/terraform/` is **optional**. Local development uses Docker Compose (Postgres, Kafka, LocalStack, Mailpit, Jaeger, Prometheus, Grafana) and costs nothing beyond your workstation.

## Lab footprint (defaults)

| Resource | Default | Approximate cost drivers |
|----------|---------|--------------------------|
| VPC + 2 public/private subnets | Always | Free |
| NAT Gateway | `enable_nat_gateway=true` | **Largest idle cost** (~$32/mo + data). Set `false` for cheapest labs (no private egress). |
| RDS PostgreSQL `db.t4g.micro` 20 GB | Single-AZ | ~$12–15/mo idle; Multi-AZ roughly doubles |
| ECS Fargate tasks | Off until `enable_ecs_services=true` | ~$0.04/vCPU-hr + memory; 1×0.5 vCPU API ≈ a few USD/day if left on |
| SQS + DLQ | Always (when applied) | Pennies at lab volume |
| SNS + SES | Identity + topics | SES free tier then per-email; SNS negligible |
| Secrets Manager | ~3–4 secrets | ~$0.40/secret/mo |
| S3 audit exports | Versioned + IA after 30d | Storage + PUT requests |
| Lambda + EventBridge | Off until packages provided | Free tier friendly at hourly/daily schedules |
| CloudWatch Logs (14d) + alarms | Always | Log ingest is the variable |

**Guidance:** destroy the lab stack when idle (`terraform destroy`). Prefer Compose for day-to-day work. Enable NAT only when private Fargate tasks must reach ECR/public endpoints.

## What is intentionally not provisioned

- Managed MSK / Amazon MQ (Kafka) — bring your own bootstrap servers via Secrets Manager (`entitlex/kafka`) for AWS deploys; Compose Kafka remains the local default.
- Public ALB / ACM / Route53 — add when you have a domain; lab can use port-forward / internal only.
- Hardcoded AWS access keys — use instance/task roles + Secrets Manager. CI deploy uses OIDC (see `.github/workflows/deploy-aws.yml`).

## Cost control checklist

1. `environment = "lab"` → `skip_final_snapshot`, no deletion protection.
2. `enable_ecs_services = false` until images exist.
3. `rds_multi_az = false` unless testing HA.
4. Tear down NAT when not needed.
5. Keep CloudWatch retention at 14 days in lab.

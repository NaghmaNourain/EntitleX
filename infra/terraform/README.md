# EntitleX Terraform (optional AWS)

Provisions a lab/staging-style AWS footprint for EntitleX. **Local Compose remains the primary developer path.** See [COST.md](COST.md) before applying.

## Resources

| Area | Module | Notes |
|------|--------|-------|
| VPC | `modules/vpc` | Public/private subnets, optional NAT |
| ECS Fargate | `modules/ecs` | API, workers, simulators (gated by `enable_ecs_services`) |
| RDS PostgreSQL | `modules/rds` | Encrypted; master password → Secrets Manager |
| SQS | `modules/sqs` | Fulfillment + DLQ (maxReceiveCount 5) |
| SNS / SES | `modules/messaging` | Alarm/ops topics + SES from-identity |
| Lambda | `modules/lambda` | Approval reminder + expiration (gated) |
| EventBridge | `modules/eventbridge` | Schedules mirroring `infra/eventbridge/schedules.stub.json` |
| CloudWatch | `modules/cloudwatch` | Log groups, SQS alarms, dashboard |
| IAM | `modules/iam` | Task execution/task/lambda roles (least privilege) |
| Secrets Manager | `modules/secrets` | JWT, connection strings, Kafka — **values set outside Terraform** |
| S3 | `modules/s3_audit` | Audit export bucket (SSE-KMS, versioned, TLS-only) |

## Never hardcode credentials

- No AWS access keys in `.tf` / `.tfvars` / container images.
- RDS master password: `random_password` → Secrets Manager only.
- JWT signing key: create secret shell, then `aws secretsmanager put-secret-value` (or CI OIDC).
- Connection strings: compose from RDS secret after databases are created; store under `entitlex/connection-strings` JSON keys (`EntitleXPlatform`, etc.).

## Usage

```bash
cd infra/terraform
cp terraform.tfvars.example terraform.tfvars   # edit non-secret values only
terraform init
terraform plan
# terraform apply   # only when you intend to spend AWS budget
```

Kafka/MSK is **not** created here; set `Messaging__Kafka__BootstrapServers` via the Kafka secret for AWS deploys. Local Kafka from Compose is unchanged.

Manual deploy workflow: `.github/workflows/deploy-aws.yml` (`workflow_dispatch`, environment protection recommended).

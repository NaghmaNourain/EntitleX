# Runbook: RDS Multi-AZ and disaster recovery

**Audience:** operators enabling HA for a non-lab EntitleX AWS footprint.  
**Defaults stay cheap:** `rds_multi_az = false`, `rds_backup_retention_period = 7`, `enable_alb = false`.

## Targets (lab / staging guidance)

| Metric | Lab | Staging (example) |
|--------|-----|-------------------|
| RPO | Best-effort (Compose volumes) | ≤ 5 minutes (RDS automated backups + Multi-AZ sync) |
| RTO | Recreate Compose | ≤ 30 minutes for AZ failover (Multi-AZ); hours for snapshot restore |
| Multi-AZ | Off | On (`rds_multi_az = true`) |
| Backup retention | 7 days | 14+ days (`rds_backup_retention_period`) |
| Deletion protection | Off when `environment = "lab"` | On automatically when environment ≠ `lab` |

These are **guidance**, not contractual SLOs. Wire CloudWatch alarms separately.

## Enable Multi-AZ (opt-in spend)

1. Review [COST.md](../../infra/terraform/COST.md) — Multi-AZ roughly doubles RDS idle cost.
2. Set in tfvars (see `infra/terraform/terraform.tfvars.staging.example`):
   - `environment = "staging"` (or non-`lab`)
   - `rds_multi_az = true`
   - Prefer larger `rds_backup_retention_period` (e.g. 14)
3. `terraform plan` then gated `terraform apply` via `.github/workflows/deploy-aws.yml` (`apply=true`).
4. Confirm in AWS console: DB instance Multi-AZ = Yes; secondary in another AZ of the subnet group.

## Snapshot / restore drill

1. Create a manual snapshot: `aws rds create-db-snapshot --db-instance-identifier entitlex-pg --db-snapshot-identifier entitlex-drill-YYYYMMDD`.
2. Restore to a **new** identifier (never overwrite prod in-place during a drill):  
   `aws rds restore-db-instance-from-db-snapshot --db-instance-identifier entitlex-pg-drill --db-snapshot-identifier entitlex-drill-YYYYMMDD`.
3. Point a throwaway ECS task / bastion at the drill endpoint using Secrets Manager connection-string override.
4. Run `/health/ready` and a read-only API smoke (list pending / get request).
5. Destroy the drill instance when finished; keep snapshots per retention policy.

## AZ failover (Multi-AZ)

AWS performs automatic failover on primary failure. Expect brief connection errors; Npgsql clients should retry. After failover:

1. Check RDS Events for failover completion.
2. Confirm API/workers reconnect (ECS tasks keep using the **DNS** endpoint, not a fixed IP).
3. Check outbox backlog / poison metrics in Grafana or CloudWatch.

## What this runbook does **not** cover

- **Cross-region** replicas or Aurora Global — not provisioned.
- **MSK / Kafka** HA — bootstrap is bring-your-own via Secrets Manager; Compose Kafka is local-only.
- **Point-in-time restore automation** — use AWS console/CLI; no EntitleX wrapper.
- **Secrets rotation** — RDS master secret is created by Terraform; rotate outside Terraform.

## Related

- [deployment.md](../deployment.md) — Compose vs AWS, deploy workflow
- [failure-scenarios.md](../failure-scenarios.md) — application failure modes
- [infra/terraform/COST.md](../../infra/terraform/COST.md) — spend knobs

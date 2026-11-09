output "vpc_id" {
  value       = module.vpc.vpc_id
  description = "VPC id."
}

output "private_subnet_ids" {
  value       = module.vpc.private_subnet_ids
  description = "Private subnet ids for ECS/RDS."
}

output "public_subnet_ids" {
  value       = module.vpc.public_subnet_ids
  description = "Public subnet ids for ALB."
}

output "rds_endpoint" {
  value       = module.rds.endpoint
  description = "RDS PostgreSQL endpoint hostname."
  sensitive   = true
}

output "fulfillment_queue_url" {
  value       = module.sqs.fulfillment_queue_url
  description = "SQS fulfillment queue URL."
}

output "fulfillment_dlq_url" {
  value       = module.sqs.fulfillment_dlq_url
  description = "SQS fulfillment DLQ URL."
}

output "audit_exports_bucket" {
  value       = module.s3_audit.bucket_name
  description = "S3 bucket for audit export objects."
}

output "secrets_arns" {
  value       = module.secrets.secret_arns
  description = "Secrets Manager ARNs (values never in Terraform state plaintext by design — set via CLI/CI)."
  sensitive   = true
}

output "ecs_cluster_name" {
  value       = module.ecs.cluster_name
  description = "ECS cluster name."
}

output "ecr_repository_urls" {
  value       = module.ecr.repository_urls
  description = "ECR repository URLs keyed by short name (api, outbox, ...)."
}

output "alb_dns_name" {
  value       = module.alb.alb_dns_name
  description = "Public ALB DNS name when enable_alb=true; empty otherwise."
}

output "cloudwatch_log_group_prefix" {
  value       = module.cloudwatch.log_group_prefix
  description = "CloudWatch Logs prefix for EntitleX services."
}

output "alarm_sns_topic_arn" {
  value       = module.messaging.alarm_topic_arn
  description = "SNS topic for CloudWatch alarms."
}

output "cost_notes" {
  value       = "See infra/terraform/COST.md for optional AWS spend guidance. Default lab footprint targets low single-digit USD/day when idle resources are destroyed."
  description = "Pointer to cost documentation."
}

variable "aws_region" {
  description = "AWS region for all resources."
  type        = string
  default     = "us-east-1"
}

variable "environment" {
  description = "Deployment environment name used for resource tags/names (e.g. lab, staging)."
  type        = string
  default     = "lab"
}

variable "aspnetcore_environment" {
  description = "ASPNETCORE_ENVIRONMENT for ECS/Lambda hosts. Keep separate from the lab tag; default Production so OIDC guards apply."
  type        = string
  default     = "Production"
}

variable "name_prefix" {
  description = "Prefix for resource names."
  type        = string
  default     = "entitlex"
}

variable "extra_tags" {
  description = "Additional tags applied to all resources."
  type        = map(string)
  default     = {}
}

variable "enable_nat_gateway" {
  description = "Create a NAT gateway for private subnet egress (adds cost)."
  type        = bool
  default     = true
}

variable "vpc_cidr" {
  description = "VPC CIDR block."
  type        = string
  default     = "10.40.0.0/16"
}

variable "availability_zones" {
  description = "AZs for subnets (min 2 for RDS)."
  type        = list(string)
  default     = ["us-east-1a", "us-east-1b"]
}

variable "rds_instance_class" {
  description = "RDS PostgreSQL instance class."
  type        = string
  default     = "db.t4g.micro"
}

variable "rds_allocated_storage_gb" {
  description = "RDS allocated storage (GB)."
  type        = number
  default     = 20
}

variable "rds_multi_az" {
  description = "Enable Multi-AZ for RDS (higher cost)."
  type        = bool
  default     = false
}

variable "ecs_desired_count_api" {
  description = "Desired ECS tasks for the API service."
  type        = number
  default     = 1
}

variable "ecs_desired_count_workers" {
  description = "Desired ECS tasks per worker service."
  type        = number
  default     = 1
}

variable "container_image_api" {
  description = "ECR/image URI for EntitleX.Api (set after CI push)."
  type        = string
  default     = ""
}

variable "container_image_outbox" {
  type    = string
  default = ""
}

variable "container_image_fulfillment" {
  type    = string
  default = ""
}

variable "container_image_reconciliation" {
  type    = string
  default = ""
}

variable "container_image_notification" {
  type    = string
  default = ""
}

variable "container_image_cloudshield" {
  type    = string
  default = ""
}

variable "container_image_networkguard" {
  type    = string
  default = ""
}

variable "lambda_approval_reminder_package_path" {
  description = "Local zip path for ApprovalReminder Lambda (optional until packaged)."
  type        = string
  default     = ""
}

variable "lambda_expiration_notification_package_path" {
  description = "Local zip path for ExpirationNotification Lambda."
  type        = string
  default     = ""
}

variable "jwt_signing_key_secret_arn" {
  description = "Optional existing Secrets Manager ARN for Jwt__SigningKey. If empty, Terraform creates a secret (value set outside Terraform)."
  type        = string
  default     = ""
}

variable "ses_from_address" {
  description = "Verified SES from address (must be verified in SES)."
  type        = string
  default     = "noreply@example.com"
}

variable "app_base_url" {
  description = "Public app base URL used in notification deep links."
  type        = string
  default     = "https://entitlex.example.com"
}

variable "enable_ecs_services" {
  description = "When false, creates supporting infra (VPC/RDS/SQS/etc.) without ECS services (useful before images exist)."
  type        = bool
  default     = false
}

variable "enable_ecr" {
  description = "Create ECR repositories (negligible cost; needed before image push)."
  type        = bool
  default     = true
}

variable "enable_alb" {
  description = "Create a public ALB + target group for the API (adds cost). Keep false for lab."
  type        = bool
  default     = false
}

variable "enable_alb_https" {
  description = "When true and alb_certificate_arn is set, listen on 443 and redirect HTTP→HTTPS."
  type        = bool
  default     = false
}

variable "alb_certificate_arn" {
  description = "ACM certificate ARN for HTTPS listener (optional)."
  type        = string
  default     = ""
}

variable "enable_lambda_schedules" {
  description = "Create Lambda functions + EventBridge schedules when package paths are provided."
  type        = bool
  default     = false
}

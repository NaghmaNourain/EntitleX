# Secrets are created empty (or with placeholders). Populate values with AWS CLI /
# GitHub Actions OIDC — never commit secret values to Terraform tfvars or git.

resource "aws_secretsmanager_secret" "jwt_signing_key" {
  count                   = var.jwt_signing_key_secret_arn == "" ? 1 : 0
  name                    = "${var.name_prefix}/jwt/signing-key"
  description             = "Jwt__SigningKey (≥32 chars). Set value outside Terraform."
  recovery_window_in_days = 0
}

resource "aws_secretsmanager_secret" "connection_strings" {
  name                    = "${var.name_prefix}/connection-strings"
  description             = "JSON map of ConnectionStrings__* values. Set outside Terraform after RDS is ready."
  recovery_window_in_days = 0
}

resource "aws_secretsmanager_secret" "kafka" {
  name                    = "${var.name_prefix}/kafka"
  description             = "Messaging__Kafka__BootstrapServers and auth material if any."
  recovery_window_in_days = 0
}

locals {
  jwt_arn = var.jwt_signing_key_secret_arn != "" ? var.jwt_signing_key_secret_arn : aws_secretsmanager_secret.jwt_signing_key[0].arn
}

output "secret_arns" {
  value = {
    jwt_signing_key    = local.jwt_arn
    connection_strings = aws_secretsmanager_secret.connection_strings.arn
    kafka              = aws_secretsmanager_secret.kafka.arn
    rds_master         = var.rds_master_secret_arn
  }
}

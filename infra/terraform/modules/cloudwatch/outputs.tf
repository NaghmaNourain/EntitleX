output "log_group_prefix" {
  value = "/entitlex/${var.environment}"
}

output "log_group_names" {
  value = [for g in aws_cloudwatch_log_group.services : g.name]
}

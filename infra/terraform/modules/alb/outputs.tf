output "alb_dns_name" {
  description = "Public DNS name of the ALB (empty when disabled)."
  value       = try(aws_lb.this[0].dns_name, "")
}

output "alb_arn" {
  value = try(aws_lb.this[0].arn, "")
}

output "alb_security_group_id" {
  value = try(aws_security_group.alb[0].id, "")
}

output "api_target_group_arn" {
  value = try(aws_lb_target_group.api[0].arn, "")
}

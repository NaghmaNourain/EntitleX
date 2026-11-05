output "approval_reminder_arn" {
  value = try(aws_lambda_function.approval_reminder[0].arn, null)
}

output "expiration_notification_arn" {
  value = try(aws_lambda_function.expiration_notification[0].arn, null)
}

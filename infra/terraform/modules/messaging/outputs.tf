output "alarm_topic_arn" { value = aws_sns_topic.alarms.arn }
output "ops_topic_arn" { value = aws_sns_topic.ops.arn }
output "ses_from_identity" { value = aws_ses_email_identity.from.email }
output "ses_from_identity_arn" { value = aws_ses_email_identity.from.arn }

# Mirrors infra/eventbridge/schedules.stub.json for AWS.

resource "aws_cloudwatch_event_rule" "approval_reminder" {
  count               = var.approval_reminder_arn != null ? 1 : 0
  name                = "${var.name_prefix}-approval-reminder"
  description         = "Pending approval ≥7d remind / escalate"
  schedule_expression = "rate(1 hour)"
}

resource "aws_cloudwatch_event_target" "approval_reminder" {
  count     = var.approval_reminder_arn != null ? 1 : 0
  rule      = aws_cloudwatch_event_rule.approval_reminder[0].name
  target_id = "ApprovalReminder"
  arn       = var.approval_reminder_arn
}

resource "aws_lambda_permission" "approval_reminder" {
  count         = var.approval_reminder_arn != null ? 1 : 0
  statement_id  = "AllowEventBridgeInvoke"
  action        = "lambda:InvokeFunction"
  function_name = var.approval_reminder_arn
  principal     = "events.amazonaws.com"
  source_arn    = aws_cloudwatch_event_rule.approval_reminder[0].arn
}

resource "aws_cloudwatch_event_rule" "expiration_cloudshield" {
  count               = var.expiration_notification_arn != null ? 1 : 0
  name                = "${var.name_prefix}-expiration-cloudshield"
  description         = "CloudShield near-expiry notifications"
  schedule_expression = "rate(24 hours)"
}

resource "aws_cloudwatch_event_target" "expiration_cloudshield" {
  count     = var.expiration_notification_arn != null ? 1 : 0
  rule      = aws_cloudwatch_event_rule.expiration_cloudshield[0].name
  target_id = "ExpirationCloudShield"
  arn       = var.expiration_notification_arn
  input     = jsonencode({ providers = ["CloudShield"] })
}

resource "aws_cloudwatch_event_rule" "expiration_networkguard" {
  count               = var.expiration_notification_arn != null ? 1 : 0
  name                = "${var.name_prefix}-expiration-networkguard"
  description         = "NetworkGuard near-expiry notifications"
  schedule_expression = "rate(7 days)"
}

resource "aws_cloudwatch_event_target" "expiration_networkguard" {
  count     = var.expiration_notification_arn != null ? 1 : 0
  rule      = aws_cloudwatch_event_rule.expiration_networkguard[0].name
  target_id = "ExpirationNetworkGuard"
  arn       = var.expiration_notification_arn
  input     = jsonencode({ providers = ["NetworkGuard"] })
}

resource "aws_lambda_permission" "expiration_notification" {
  count         = var.expiration_notification_arn != null ? 1 : 0
  statement_id  = "AllowEventBridgeInvokeExpiration"
  action        = "lambda:InvokeFunction"
  function_name = var.expiration_notification_arn
  principal     = "events.amazonaws.com"
  source_arn    = aws_cloudwatch_event_rule.expiration_cloudshield[0].arn
}

resource "aws_cloudwatch_log_group" "services" {
  for_each          = toset(var.service_names)
  name              = "/entitlex/${var.environment}/${each.value}"
  retention_in_days = var.log_retention_days
}

resource "aws_cloudwatch_metric_alarm" "sqs_age" {
  alarm_name          = "${var.name_prefix}-fulfillment-age"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 2
  metric_name         = "ApproximateAgeOfOldestMessage"
  namespace           = "AWS/SQS"
  period              = 60
  statistic           = "Maximum"
  threshold           = var.sqs_age_threshold_seconds
  alarm_description   = "Fulfillment queue message age too high"
  alarm_actions       = [var.alarm_topic_arn]
  ok_actions          = [var.alarm_topic_arn]

  dimensions = {
    QueueName = var.fulfillment_queue_name
  }
}

resource "aws_cloudwatch_metric_alarm" "dlq_depth" {
  alarm_name          = "${var.name_prefix}-fulfillment-dlq-depth"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "ApproximateNumberOfMessagesVisible"
  namespace           = "AWS/SQS"
  period              = 60
  statistic           = "Maximum"
  threshold           = 0
  alarm_description   = "Fulfillment DLQ has messages"
  alarm_actions       = [var.alarm_topic_arn]

  dimensions = {
    QueueName = var.fulfillment_dlq_name
  }
}

resource "aws_cloudwatch_dashboard" "overview" {
  dashboard_name = "${var.name_prefix}-${var.environment}"
  dashboard_body = jsonencode({
    widgets = [
      {
        type   = "metric"
        x      = 0
        y      = 0
        width  = 12
        height = 6
        properties = {
          title  = "SQS fulfillment age"
          region = var.aws_region
          metrics = [
            ["AWS/SQS", "ApproximateAgeOfOldestMessage", "QueueName", var.fulfillment_queue_name]
          ]
        }
      },
      {
        type   = "metric"
        x      = 12
        y      = 0
        width  = 12
        height = 6
        properties = {
          title  = "DLQ depth"
          region = var.aws_region
          metrics = [
            ["AWS/SQS", "ApproximateNumberOfMessagesVisible", "QueueName", var.fulfillment_dlq_name]
          ]
        }
      }
    ]
  })
}

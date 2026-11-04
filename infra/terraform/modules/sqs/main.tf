resource "aws_sqs_queue" "fulfillment_dlq" {
  name                      = "${var.name_prefix}-fulfillment-dlq"
  message_retention_seconds = 1209600
  sqs_managed_sse_enabled   = true
}

resource "aws_sqs_queue" "fulfillment" {
  name                       = "${var.name_prefix}-fulfillment"
  visibility_timeout_seconds = 60
  receive_wait_time_seconds  = 10
  message_retention_seconds  = 345600
  sqs_managed_sse_enabled    = true

  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.fulfillment_dlq.arn
    maxReceiveCount     = 5
  })
}

resource "aws_sqs_queue_policy" "fulfillment" {
  queue_url = aws_sqs_queue.fulfillment.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Sid       = "DenyInsecureTransport"
      Effect    = "Deny"
      Principal = "*"
      Action    = "sqs:*"
      Resource  = aws_sqs_queue.fulfillment.arn
      Condition = {
        Bool = { "aws:SecureTransport" = "false" }
      }
    }]
  })
}

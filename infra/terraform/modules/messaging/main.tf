# Alarm / ops SNS topic (subscribe email endpoints outside Terraform or via var).
resource "aws_sns_topic" "alarms" {
  name = "${var.name_prefix}-alarms"
}

resource "aws_sns_topic" "ops" {
  name = "${var.name_prefix}-ops"
}

# SES identity is environment-specific. We declare the email identity resource;
# verification must be completed in the AWS console / DNS before send works.
resource "aws_ses_email_identity" "from" {
  email = var.ses_from_address
}

# Optional SNS→SES bridge stub for transactional fan-out (notifications still
# prefer direct SES SDK from the NotificationService / Lambdas).
resource "aws_sns_topic_policy" "ops" {
  arn = aws_sns_topic.ops.arn
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Sid       = "AllowAccountPublish"
      Effect    = "Allow"
      Principal = { AWS = "*" }
      Action    = "sns:Publish"
      Resource  = aws_sns_topic.ops.arn
      Condition = {
        StringEquals = { "AWS:SourceOwner" = data.aws_caller_identity.current.account_id }
      }
    }]
  })
}

data "aws_caller_identity" "current" {}

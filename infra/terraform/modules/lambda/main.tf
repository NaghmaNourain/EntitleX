resource "aws_lambda_function" "approval_reminder" {
  count = var.enable && var.approval_reminder_package_path != "" ? 1 : 0

  function_name = "${var.name_prefix}-approval-reminder"
  role          = var.lambda_role_arn
  runtime       = "dotnet8"
  handler       = "EntitleX.ApprovalReminder::EntitleX.ApprovalReminder.Function::FunctionHandler"
  filename      = var.approval_reminder_package_path
  timeout       = 60
  memory_size   = 512

  vpc_config {
    subnet_ids         = var.subnet_ids
    security_group_ids = [var.security_group_id]
  }

  environment {
    variables = {
      ASPNETCORE_ENVIRONMENT = var.aspnetcore_environment
      Email__Provider        = "Ses"
      Email__FromAddress     = var.ses_from_address
      Email__AppBaseUrl      = var.app_base_url
    }
  }
}

resource "aws_lambda_function" "expiration_notification" {
  count = var.enable && var.expiration_notification_package_path != "" ? 1 : 0

  function_name = "${var.name_prefix}-expiration-notification"
  role          = var.lambda_role_arn
  runtime       = "dotnet8"
  handler       = "EntitleX.ExpirationNotification::EntitleX.ExpirationNotification.Function::FunctionHandler"
  filename      = var.expiration_notification_package_path
  timeout       = 120
  memory_size   = 512

  vpc_config {
    subnet_ids         = var.subnet_ids
    security_group_ids = [var.security_group_id]
  }

  environment {
    variables = {
      ASPNETCORE_ENVIRONMENT = var.aspnetcore_environment
      Email__Provider        = "Ses"
      Email__FromAddress     = var.ses_from_address
      Email__AppBaseUrl      = var.app_base_url
    }
  }
}

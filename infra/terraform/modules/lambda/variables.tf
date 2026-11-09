variable "name_prefix" { type = string }
variable "environment" { type = string }
variable "aspnetcore_environment" {
  type    = string
  default = "Production"
}
variable "enable" { type = bool }
variable "lambda_role_arn" { type = string }
variable "subnet_ids" { type = list(string) }
variable "security_group_id" { type = string }
variable "ses_from_address" { type = string }
variable "app_base_url" { type = string }
variable "approval_reminder_package_path" { type = string }
variable "expiration_notification_package_path" { type = string }

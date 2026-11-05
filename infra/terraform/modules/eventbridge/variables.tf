variable "name_prefix" { type = string }
variable "approval_reminder_arn" {
  type    = string
  default = null
}
variable "expiration_notification_arn" {
  type    = string
  default = null
}

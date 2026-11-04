variable "name_prefix" { type = string }
variable "environment" { type = string }
variable "aws_region" { type = string }
variable "service_names" { type = list(string) }
variable "log_retention_days" {
  type    = number
  default = 14
}
variable "alarm_topic_arn" { type = string }
variable "fulfillment_queue_name" { type = string }
variable "fulfillment_dlq_name" { type = string }
variable "sqs_age_threshold_seconds" {
  type    = number
  default = 300
}

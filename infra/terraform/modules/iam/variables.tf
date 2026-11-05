variable "name_prefix" { type = string }
variable "secret_arns" { type = list(string) }
variable "fulfillment_queue_arn" { type = string }
variable "fulfillment_dlq_arn" { type = string }
variable "audit_bucket_arn" { type = string }

variable "name_prefix" { type = string }
variable "environment" { type = string }
variable "aws_region" { type = string }
variable "private_subnet_ids" { type = list(string) }
variable "security_group_id" { type = string }
variable "execution_role_arn" { type = string }
variable "task_role_arn" { type = string }
variable "enable_services" { type = bool }
variable "ses_from_address" { type = string }
variable "app_base_url" { type = string }
variable "fulfillment_queue_name" { type = string }
variable "jwt_secret_arn" { type = string }
variable "connection_strings_secret_arn" { type = string }

variable "services" {
  description = "Map of ECS service definitions keyed by short name."
  type = map(object({
    image         = string
    cpu           = number
    memory        = number
    desired_count = number
    port          = optional(number)
  }))
  default = {}
}

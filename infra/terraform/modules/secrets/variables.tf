variable "name_prefix" { type = string }
variable "jwt_signing_key_secret_arn" {
  type    = string
  default = ""
}
variable "rds_master_secret_arn" { type = string }

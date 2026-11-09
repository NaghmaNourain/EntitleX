variable "name_prefix" {
  type = string
}

variable "environment" {
  type = string
}

variable "enable" {
  type    = bool
  default = true
}

variable "keep_image_count" {
  type    = number
  default = 10
}

terraform {
  required_version = ">= 1.5.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }

  # Optional remote state — configure via -backend-config or uncomment for your account.
  # backend "s3" {
  #   bucket         = "entitlex-tfstate-REPLACE"
  #   key            = "entitlex/terraform.tfstate"
  #   region         = "us-east-1"
  #   dynamodb_table = "entitlex-tf-locks"
  #   encrypt        = true
  # }
}

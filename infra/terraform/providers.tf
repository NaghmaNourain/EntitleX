provider "aws" {
  region = var.aws_region

  default_tags {
    tags = merge(
      {
        Project     = "EntitleX"
        Environment = var.environment
        ManagedBy   = "terraform"
      },
      var.extra_tags
    )
  }
}

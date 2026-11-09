locals {
  repositories = toset([
    "api",
    "outbox",
    "fulfillment",
    "reconciliation",
    "notification",
    "cloudshield",
    "networkguard",
    "frontend",
    "lambda",
  ])
}

resource "aws_ecr_repository" "this" {
  for_each             = var.enable ? local.repositories : toset([])
  name                 = "${var.name_prefix}/${each.key}"
  image_tag_mutability = "MUTABLE"
  force_delete         = var.environment == "lab"

  image_scanning_configuration {
    scan_on_push = true
  }

  tags = {
    Name        = "${var.name_prefix}/${each.key}"
    Environment = var.environment
  }
}

resource "aws_ecr_lifecycle_policy" "this" {
  for_each   = aws_ecr_repository.this
  repository = each.value.name

  policy = jsonencode({
    rules = [
      {
        rulePriority = 1
        description  = "Keep last ${var.keep_image_count} images"
        selection = {
          tagStatus   = "any"
          countType   = "imageCountMoreThan"
          countNumber = var.keep_image_count
        }
        action = {
          type = "expire"
        }
      }
    ]
  })
}

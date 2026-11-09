locals {
  fulfillment_queue_name = "${var.name_prefix}-fulfillment"
  fulfillment_dlq_name   = "${var.name_prefix}-fulfillment-dlq"

  ecs_services = var.enable_ecs_services ? {
    api = {
      image         = var.container_image_api
      cpu           = 512
      memory        = 1024
      desired_count = var.ecs_desired_count_api
      port          = 8080
    }
    outbox = {
      image         = var.container_image_outbox
      cpu           = 256
      memory        = 512
      desired_count = var.ecs_desired_count_workers
      port          = null
    }
    fulfillment = {
      image         = var.container_image_fulfillment
      cpu           = 512
      memory        = 1024
      desired_count = var.ecs_desired_count_workers
      port          = null
    }
    reconciliation = {
      image         = var.container_image_reconciliation
      cpu           = 256
      memory        = 512
      desired_count = var.ecs_desired_count_workers
      port          = null
    }
    notification = {
      image         = var.container_image_notification
      cpu           = 256
      memory        = 512
      desired_count = var.ecs_desired_count_workers
      port          = null
    }
    cloudshield = {
      image         = var.container_image_cloudshield
      cpu           = 256
      memory        = 512
      desired_count = 1
      port          = 8080
    }
    networkguard = {
      image         = var.container_image_networkguard
      cpu           = 256
      memory        = 512
      desired_count = 1
      port          = 8080
    }
  } : {}
}

module "vpc" {
  source             = "./modules/vpc"
  name_prefix        = var.name_prefix
  cidr_block         = var.vpc_cidr
  availability_zones = var.availability_zones
  enable_nat_gateway = var.enable_nat_gateway
}

# Shared app security group — created before RDS/ECS to avoid cycles.
resource "aws_security_group" "app" {
  name        = "${var.name_prefix}-app"
  description = "EntitleX ECS tasks and Lambdas"
  vpc_id      = module.vpc.vpc_id

  ingress {
    description = "HTTP within VPC"
    from_port   = 8080
    to_port     = 8080
    protocol    = "tcp"
    cidr_blocks = [var.vpc_cidr]
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = { Name = "${var.name_prefix}-app" }
}

resource "aws_security_group_rule" "app_from_alb" {
  count                    = var.enable_alb ? 1 : 0
  type                     = "ingress"
  from_port                = 8080
  to_port                  = 8080
  protocol                 = "tcp"
  security_group_id        = aws_security_group.app.id
  source_security_group_id = module.alb.alb_security_group_id
  description              = "API from ALB"
}

module "ecr" {
  source      = "./modules/ecr"
  name_prefix = var.name_prefix
  environment = var.environment
  enable      = var.enable_ecr
}

module "alb" {
  source            = "./modules/alb"
  name_prefix       = var.name_prefix
  environment       = var.environment
  enable            = var.enable_alb
  enable_https      = var.enable_alb_https
  certificate_arn   = var.alb_certificate_arn
  vpc_id            = module.vpc.vpc_id
  public_subnet_ids = module.vpc.public_subnet_ids
}

module "rds" {
  source                = "./modules/rds"
  name_prefix           = var.name_prefix
  environment           = var.environment
  vpc_id                = module.vpc.vpc_id
  subnet_ids            = module.vpc.private_subnet_ids
  ecs_security_group_id = aws_security_group.app.id
  instance_class           = var.rds_instance_class
  allocated_storage_gb     = var.rds_allocated_storage_gb
  multi_az                 = var.rds_multi_az
  backup_retention_period  = var.rds_backup_retention_period
}

module "sqs" {
  source      = "./modules/sqs"
  name_prefix = var.name_prefix
}

module "messaging" {
  source           = "./modules/messaging"
  name_prefix      = var.name_prefix
  ses_from_address = var.ses_from_address
}

module "s3_audit" {
  source        = "./modules/s3_audit"
  name_prefix   = var.name_prefix
  force_destroy = var.environment == "lab"
}

module "secrets" {
  source                     = "./modules/secrets"
  name_prefix                = var.name_prefix
  jwt_signing_key_secret_arn = var.jwt_signing_key_secret_arn
  rds_master_secret_arn      = module.rds.master_secret_arn
}

module "iam" {
  source                = "./modules/iam"
  name_prefix           = var.name_prefix
  secret_arns           = values(module.secrets.secret_arns)
  fulfillment_queue_arn = module.sqs.fulfillment_queue_arn
  fulfillment_dlq_arn   = module.sqs.fulfillment_dlq_arn
  audit_bucket_arn      = module.s3_audit.bucket_arn
}

module "cloudwatch" {
  source                 = "./modules/cloudwatch"
  name_prefix            = var.name_prefix
  environment            = var.environment
  aws_region             = var.aws_region
  service_names          = ["api", "outbox", "fulfillment", "reconciliation", "notification", "cloudshield", "networkguard", "lambda"]
  alarm_topic_arn        = module.messaging.alarm_topic_arn
  fulfillment_queue_name = local.fulfillment_queue_name
  fulfillment_dlq_name   = local.fulfillment_dlq_name
}

module "ecs" {
  source                        = "./modules/ecs"
  name_prefix                   = var.name_prefix
  environment                   = var.environment
  aspnetcore_environment        = var.aspnetcore_environment
  aws_region                    = var.aws_region
  private_subnet_ids            = module.vpc.private_subnet_ids
  security_group_id             = aws_security_group.app.id
  execution_role_arn            = module.iam.ecs_task_execution_role_arn
  task_role_arn                 = module.iam.ecs_task_role_arn
  enable_services               = var.enable_ecs_services
  services                      = local.ecs_services
  ses_from_address              = var.ses_from_address
  app_base_url                  = var.app_base_url
  fulfillment_queue_name        = local.fulfillment_queue_name
  jwt_secret_arn                = module.secrets.secret_arns.jwt_signing_key
  connection_strings_secret_arn = module.secrets.secret_arns.connection_strings
  api_target_group_arn          = var.enable_alb && var.enable_ecs_services ? module.alb.api_target_group_arn : ""
}

module "lambda" {
  source                               = "./modules/lambda"
  name_prefix                          = var.name_prefix
  environment                          = var.environment
  aspnetcore_environment               = var.aspnetcore_environment
  enable                               = var.enable_lambda_schedules
  lambda_role_arn                      = module.iam.lambda_role_arn
  subnet_ids                           = module.vpc.private_subnet_ids
  security_group_id                    = aws_security_group.app.id
  ses_from_address                     = var.ses_from_address
  app_base_url                         = var.app_base_url
  approval_reminder_package_path       = var.lambda_approval_reminder_package_path
  expiration_notification_package_path = var.lambda_expiration_notification_package_path
}

module "eventbridge" {
  source                      = "./modules/eventbridge"
  name_prefix                 = var.name_prefix
  approval_reminder_arn       = module.lambda.approval_reminder_arn
  expiration_notification_arn = module.lambda.expiration_notification_arn
}

resource "aws_ecs_cluster" "this" {
  name = "${var.name_prefix}-${var.environment}"

  setting {
    name  = "containerInsights"
    value = "enabled"
  }
}

resource "aws_cloudwatch_log_group" "ecs" {
  for_each          = var.enable_services ? toset(keys(var.services)) : toset([])
  name              = "/entitlex/${var.environment}/ecs/${each.key}"
  retention_in_days = 14
}

resource "aws_ecs_task_definition" "service" {
  for_each                 = var.enable_services ? var.services : {}
  family                   = "${var.name_prefix}-${each.key}"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = each.value.cpu
  memory                   = each.value.memory
  execution_role_arn       = var.execution_role_arn
  task_role_arn            = var.task_role_arn

  container_definitions = jsonencode([
    {
      name      = each.key
      image     = each.value.image
      essential = true
      portMappings = each.value.port != null ? [{
        containerPort = each.value.port
        hostPort      = each.value.port
        protocol      = "tcp"
      }] : []
      environment = [
        { name = "ASPNETCORE_ENVIRONMENT", value = var.environment },
        { name = "Email__Provider", value = "Ses" },
        { name = "Email__FromAddress", value = var.ses_from_address },
        { name = "Email__AppBaseUrl", value = var.app_base_url },
        { name = "Messaging__Sqs__FulfillmentQueueName", value = var.fulfillment_queue_name },
        { name = "Messaging__Sqs__Region", value = var.aws_region }
      ]
      secrets = [
        {
          name      = "Jwt__SigningKey"
          valueFrom = var.jwt_secret_arn
        },
        {
          name      = "ConnectionStrings__EntitleXPlatform"
          valueFrom = "${var.connection_strings_secret_arn}:EntitleXPlatform::"
        }
      ]
      logConfiguration = {
        logDriver = "awslogs"
        options = {
          awslogs-group         = aws_cloudwatch_log_group.ecs[each.key].name
          awslogs-region        = var.aws_region
          awslogs-stream-prefix = each.key
        }
      }
    }
  ])
}

resource "aws_ecs_service" "service" {
  for_each        = var.enable_services ? var.services : {}
  name            = "${var.name_prefix}-${each.key}"
  cluster         = aws_ecs_cluster.this.id
  task_definition = aws_ecs_task_definition.service[each.key].arn
  desired_count   = each.value.desired_count
  launch_type     = "FARGATE"

  network_configuration {
    subnets          = var.private_subnet_ids
    security_groups  = [var.security_group_id]
    assign_public_ip = false
  }

  lifecycle {
    ignore_changes = [desired_count]
  }
}

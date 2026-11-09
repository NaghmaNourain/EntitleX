resource "aws_db_subnet_group" "this" {
  name       = "${var.name_prefix}-pg"
  subnet_ids = var.subnet_ids
  tags       = { Name = "${var.name_prefix}-pg" }
}

resource "aws_security_group" "rds" {
  name        = "${var.name_prefix}-rds"
  description = "PostgreSQL access from ECS tasks"
  vpc_id      = var.vpc_id

  ingress {
    description     = "Postgres from ECS"
    from_port       = 5432
    to_port         = 5432
    protocol        = "tcp"
    security_groups = [var.ecs_security_group_id]
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = { Name = "${var.name_prefix}-rds" }
}

# Password is generated and stored in Secrets Manager — never hardcoded in tfvars.
resource "random_password" "master" {
  length  = 32
  special = false
}

resource "aws_secretsmanager_secret" "rds_master" {
  name                    = "${var.name_prefix}/rds/master"
  recovery_window_in_days = 0
}

resource "aws_secretsmanager_secret_version" "rds_master" {
  secret_id = aws_secretsmanager_secret.rds_master.id
  secret_string = jsonencode({
    username = "entitlex"
    password = random_password.master.result
    host     = aws_db_instance.this.address
    port     = aws_db_instance.this.port
    dbname   = "postgres"
    engine   = "postgres"
  })
}

resource "aws_db_instance" "this" {
  identifier     = "${var.name_prefix}-pg"
  engine         = "postgres"
  engine_version = "16.4"
  instance_class = var.instance_class

  allocated_storage     = var.allocated_storage_gb
  max_allocated_storage = var.allocated_storage_gb * 2
  storage_encrypted     = true

  db_subnet_group_name   = aws_db_subnet_group.this.name
  vpc_security_group_ids = [aws_security_group.rds.id]
  publicly_accessible    = false
  multi_az               = var.multi_az

  username = "entitlex"
  password = random_password.master.result

  # Application creates entitlex_platform / cloudshield / networkguard databases via migration/init.
  db_name = "postgres"

  backup_retention_period = var.backup_retention_period
  skip_final_snapshot     = var.environment == "lab"
  deletion_protection     = var.environment != "lab"
  apply_immediately       = var.environment == "lab"

  tags = { Name = "${var.name_prefix}-pg" }
}

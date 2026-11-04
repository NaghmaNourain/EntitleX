output "endpoint" { value = aws_db_instance.this.address }
output "port" { value = aws_db_instance.this.port }
output "security_group_id" { value = aws_security_group.rds.id }
output "master_secret_arn" { value = aws_secretsmanager_secret.rds_master.arn }
output "instance_id" { value = aws_db_instance.this.id }

resource "aws_db_subnet_group" "rds" {
  name        = "${var.project}-${var.environment}-rds-subnet-group"
  description = "Subnet group for payment gateway Aurora PostgreSQL"
  subnet_ids  = var.subnet_ids

  tags = merge(var.tags, {
    Name        = "${var.project}-${var.environment}-rds-subnet-group"
    Environment = var.environment
  })
}

resource "random_password" "master" {
  length           = 32
  special          = true
  override_special = "!#$%&*()-_=+[]{}<>:?"
}

resource "aws_secretsmanager_secret" "db_credentials" {
  name                    = "${var.project}/${var.environment}/rds/credentials"
  description             = "Master credentials for ${var.project} Aurora PostgreSQL"
  kms_key_id              = var.kms_key_arn
  recovery_window_in_days = 0

  tags = merge(var.tags, {
    Name        = "${var.project}-${var.environment}-db-credentials"
    Environment = var.environment
  })
}

resource "aws_secretsmanager_secret_version" "db_credentials" {
  secret_id = aws_secretsmanager_secret.db_credentials.id
  secret_string = jsonencode({
    engine   = "postgres"
    host     = aws_rds_cluster.aurora.endpoint
    port     = 5432
    database = var.database_name
    username = var.master_username
    password = random_password.master.result
  })
}

resource "aws_rds_cluster_parameter_group" "aurora_pg16" {
  name        = "${var.project}-${var.environment}-pg16-params"
  family      = "aurora-postgresql16"
  description = "Cluster parameter group for Aurora PostgreSQL 16"

  parameter {
    name  = "rds.force_ssl"
    value = "1"
  }

  tags = merge(var.tags, {
    Name        = "${var.project}-${var.environment}-pg16-params"
    Environment = var.environment
  })
}

resource "aws_rds_cluster" "aurora" {
  cluster_identifier              = "${var.project}-${var.environment}-aurora"
  engine                          = "aurora-postgresql"
  engine_mode                     = "provisioned"
  engine_version                  = "16.4"
  database_name                   = var.database_name
  master_username                 = var.master_username
  master_password                 = random_password.master.result
  db_subnet_group_name            = aws_db_subnet_group.rds.name
  vpc_security_group_ids          = [var.security_group_id]
  db_cluster_parameter_group_name = aws_rds_cluster_parameter_group.aurora_pg16.name

  storage_encrypted   = true
  kms_key_id          = var.kms_key_arn
  deletion_protection = var.deletion_protection
  skip_final_snapshot = var.environment != "prod"

  backup_retention_period      = var.backup_retention_period
  preferred_backup_window      = "02:00-03:00"
  preferred_maintenance_window = "sun:04:00-sun:05:00"

  serverlessv2_scaling_configuration {
    min_capacity = var.min_capacity
    max_capacity = var.max_capacity
  }

  tags = merge(var.tags, {
    Name        = "${var.project}-${var.environment}-aurora"
    Environment = var.environment
  })
}

resource "aws_rds_cluster_instance" "instances" {
  count                = var.instance_count
  identifier           = "${var.project}-${var.environment}-aurora-instance-${count.index + 1}"
  cluster_identifier   = aws_rds_cluster.aurora.id
  instance_class       = "db.serverless"
  engine               = aws_rds_cluster.aurora.engine
  engine_version       = aws_rds_cluster.aurora.engine_version
  db_subnet_group_name = aws_db_subnet_group.rds.name

  tags = merge(var.tags, {
    Name        = "${var.project}-${var.environment}-aurora-instance-${count.index + 1}"
    Environment = var.environment
  })
}

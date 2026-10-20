resource "random_password" "mq_admin" {
  length           = 32
  special          = true
  override_special = "!#$%&*()-_=+[]{}<>:?"
}

resource "aws_secretsmanager_secret" "mq_credentials" {
  name                    = "${var.project}/${var.environment}/mq/credentials"
  description             = "Master credentials for ${var.project} Amazon MQ RabbitMQ"
  kms_key_id              = var.kms_key_arn
  recovery_window_in_days = 0

  tags = merge(var.tags, {
    Name        = "${var.project}-${var.environment}-mq-credentials"
    Environment = var.environment
  })
}

resource "aws_secretsmanager_secret_version" "mq_credentials" {
  secret_id = aws_secretsmanager_secret.mq_credentials.id
  secret_string = jsonencode({
    engine   = "rabbitmq"
    username = var.admin_username
    password = random_password.mq_admin.result
  })
}

resource "aws_mq_broker" "rabbitmq" {
  broker_name        = "${var.project}-${var.environment}-rabbitmq"
  engine_type        = "RabbitMQ"
  engine_version     = var.engine_version
  host_instance_type = var.host_instance_type
  deployment_mode    = var.deployment_mode

  subnet_ids          = var.deployment_mode == "CLUSTER_MULTI_AZ" ? var.subnet_ids : [var.subnet_ids[0]]
  security_groups     = [var.security_group_id]
  publicly_accessible = false

  auto_minor_version_upgrade = true

  encryption_options {
    use_aws_owned_key = false
    kms_key_id        = var.kms_key_arn
  }

  user {
    username = var.admin_username
    password = random_password.mq_admin.result
  }

  tags = merge(var.tags, {
    Name        = "${var.project}-${var.environment}-rabbitmq"
    Environment = var.environment
  })
}

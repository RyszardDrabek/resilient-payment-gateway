output "broker_id" {
  description = "Amazon MQ RabbitMQ broker identifier"
  value       = aws_mq_broker.rabbitmq.id
}

output "broker_arn" {
  description = "Amazon MQ RabbitMQ broker ARN"
  value       = aws_mq_broker.rabbitmq.arn
}

output "primary_ssl_endpoint" {
  description = "Primary AMQPS TLS endpoint URL"
  value       = aws_mq_broker.rabbitmq.instances[0].endpoints[0]
}

output "credentials_secret_arn" {
  description = "ARN of Secrets Manager secret containing RabbitMQ administrative credentials"
  value       = aws_secretsmanager_secret.mq_credentials.arn
}

output "credentials_secret_name" {
  description = "Name of Secrets Manager secret containing RabbitMQ administrative credentials"
  value       = aws_secretsmanager_secret.mq_credentials.name
}

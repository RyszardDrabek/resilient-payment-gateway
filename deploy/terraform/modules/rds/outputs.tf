output "cluster_endpoint" {
  description = "Writer endpoint for Aurora PostgreSQL cluster"
  value       = aws_rds_cluster.aurora.endpoint
}

output "cluster_reader_endpoint" {
  description = "Reader endpoint for Aurora PostgreSQL cluster"
  value       = aws_rds_cluster.aurora.reader_endpoint
}

output "database_name" {
  description = "Default database name"
  value       = aws_rds_cluster.aurora.database_name
}

output "database_port" {
  description = "Database connection port"
  value       = aws_rds_cluster.aurora.port
}

output "credentials_secret_arn" {
  description = "ARN of Secrets Manager secret containing database master credentials"
  value       = aws_secretsmanager_secret.db_credentials.arn
}

output "credentials_secret_name" {
  description = "Name of Secrets Manager secret containing database master credentials"
  value       = aws_secretsmanager_secret.db_credentials.name
}

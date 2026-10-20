output "eks_key_arn" {
  description = "KMS Key ARN for EKS secrets envelope encryption"
  value       = aws_kms_key.eks.arn
}

output "rds_key_arn" {
  description = "KMS Key ARN for RDS storage encryption"
  value       = aws_kms_key.rds.arn
}

output "mq_key_arn" {
  description = "KMS Key ARN for RabbitMQ message broker storage encryption"
  value       = aws_kms_key.mq.arn
}

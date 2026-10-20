output "vpc_id" {
  description = "VPC ID"
  value       = module.vpc.vpc_id
}

output "eks_cluster_name" {
  description = "EKS Cluster Name"
  value       = module.eks.cluster_name
}

output "eks_cluster_endpoint" {
  description = "EKS Cluster API Endpoint"
  value       = module.eks.cluster_endpoint
}

output "gateway_irsa_role_arn" {
  description = "IRSA Role ARN to annotate payment gateway ServiceAccount"
  value       = module.eks.gateway_irsa_role_arn
}

output "rds_cluster_endpoint" {
  description = "Aurora Serverless v2 PostgreSQL writer endpoint"
  value       = module.rds.cluster_endpoint
}

output "rds_credentials_secret_name" {
  description = "Secrets Manager secret name for database credentials"
  value       = module.rds.credentials_secret_name
}

output "mq_primary_ssl_endpoint" {
  description = "Amazon MQ RabbitMQ AMQPS primary endpoint"
  value       = module.mq.primary_ssl_endpoint
}

output "mq_credentials_secret_name" {
  description = "Secrets Manager secret name for RabbitMQ credentials"
  value       = module.mq.credentials_secret_name
}

output "helm_values_snippet" {
  description = "Copy-pasteable values snippet for deploy/helm/payment-gateway/values.yaml"
  value       = <<-EOT
    externalDatabase:
      enabled: true
      host: "${module.rds.cluster_endpoint}"
      port: 5432
      database: "payment_gateway"
      secretName: "${module.rds.credentials_secret_name}"

    externalRabbitmq:
      enabled: true
      host: "${module.mq.primary_ssl_endpoint}"
      port: 5671
      useSsl: true
      secretName: "${module.mq.credentials_secret_name}"

    serviceAccount:
      annotations:
        eks.amazonaws.com/role-arn: "${module.eks.gateway_irsa_role_arn}"
  EOT
}

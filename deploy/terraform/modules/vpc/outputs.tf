output "vpc_id" {
  description = "The ID of the VPC"
  value       = aws_vpc.main.id
}

output "vpc_cidr" {
  description = "The CIDR block of the VPC"
  value       = aws_vpc.main.cidr_block
}

output "public_subnet_ids" {
  description = "List of public subnet IDs"
  value       = aws_subnet.public[*].id
}

output "private_app_subnet_ids" {
  description = "List of private application subnet IDs for EKS worker nodes"
  value       = aws_subnet.private_app[*].id
}

output "private_data_subnet_ids" {
  description = "List of isolated private data subnet IDs for RDS and MQ"
  value       = aws_subnet.private_data[*].id
}

output "alb_security_group_id" {
  description = "Security group ID for Application Load Balancers"
  value       = aws_security_group.alb.id
}

output "eks_nodes_security_group_id" {
  description = "Security group ID for EKS worker nodes"
  value       = aws_security_group.eks_nodes.id
}

output "db_security_group_id" {
  description = "Security group ID for RDS Aurora PostgreSQL"
  value       = aws_security_group.db.id
}

output "mq_security_group_id" {
  description = "Security group ID for RabbitMQ Broker"
  value       = aws_security_group.mq.id
}

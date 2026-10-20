variable "environment" {
  description = "Environment name (e.g. dev, prod)"
  type        = string
}

variable "project" {
  description = "Project name"
  type        = string
  default     = "payment-gateway"
}

variable "database_name" {
  description = "Name of the default PostgreSQL database"
  type        = string
  default     = "payment_gateway"
}

variable "master_username" {
  description = "Master database username"
  type        = string
  default     = "postgres"
}

variable "subnet_ids" {
  description = "Isolated private data subnet IDs for Aurora cluster"
  type        = list(string)
}

variable "security_group_id" {
  description = "Security group ID allowing port 5432 from EKS worker nodes"
  type        = string
}

variable "kms_key_arn" {
  description = "KMS Key ARN for storage encryption"
  type        = string
}

variable "min_capacity" {
  description = "Minimum Aurora Serverless v2 Capacity Units (ACUs)"
  type        = number
  default     = 0.5
}

variable "max_capacity" {
  description = "Maximum Aurora Serverless v2 Capacity Units (ACUs)"
  type        = number
  default     = 4.0
}

variable "backup_retention_period" {
  description = "Days to retain automated backups (PITR)"
  type        = number
  default     = 7
}

variable "deletion_protection" {
  description = "Enable deletion protection on production clusters"
  type        = bool
  default     = false
}

variable "instance_count" {
  description = "Number of Aurora instances in the cluster (1 for dev, 2+ for prod HA)"
  type        = number
  default     = 1
}

variable "tags" {
  description = "Resource tags"
  type        = map(string)
  default     = {}
}

variable "environment" {
  description = "Environment name (e.g. dev, prod)"
  type        = string
}

variable "project" {
  description = "Project name"
  type        = string
  default     = "payment-gateway"
}

variable "engine_version" {
  description = "RabbitMQ broker engine version"
  type        = string
  default     = "3.13"
}

variable "host_instance_type" {
  description = "Broker instance class"
  type        = string
  default     = "mq.t3.micro"
}

variable "deployment_mode" {
  description = "Broker deployment mode: SINGLE_INSTANCE or CLUSTER_MULTI_AZ"
  type        = string
  default     = "SINGLE_INSTANCE"
}

variable "subnet_ids" {
  description = "Subnet IDs for broker deployment (single subnet for SINGLE_INSTANCE, 3 subnets for CLUSTER_MULTI_AZ)"
  type        = list(string)
}

variable "security_group_id" {
  description = "Security group ID allowing port 5671 and 443 from EKS worker nodes"
  type        = string
}

variable "kms_key_arn" {
  description = "KMS Key ARN for storage encryption"
  type        = string
}

variable "admin_username" {
  description = "Broker administrative username"
  type        = string
  default     = "gateway_admin"
}

variable "tags" {
  description = "Resource tags"
  type        = map(string)
  default     = {}
}

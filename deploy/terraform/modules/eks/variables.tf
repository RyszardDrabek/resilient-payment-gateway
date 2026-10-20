variable "environment" {
  description = "Environment name (e.g. dev, prod)"
  type        = string
}

variable "project" {
  description = "Project name"
  type        = string
  default     = "payment-gateway"
}

variable "cluster_version" {
  description = "Kubernetes control plane version"
  type        = string
  default     = "1.31"
}

variable "vpc_id" {
  description = "VPC ID where the cluster is deployed"
  type        = string
}

variable "subnet_ids" {
  description = "Subnet IDs for EKS control plane and worker nodes (private app subnets)"
  type        = list(string)
}

variable "node_security_group_id" {
  description = "Additional security group to attach to node groups"
  type        = string
}

variable "kms_key_arn" {
  description = "KMS Key ARN for Kubernetes secrets envelope encryption"
  type        = string
}

variable "instance_types" {
  description = "EC2 instance types for the worker node group"
  type        = list(string)
  default     = ["t3.medium"]
}

variable "capacity_type" {
  description = "Capacity type for worker node group: ON_DEMAND or SPOT"
  type        = string
  default     = "ON_DEMAND"
}

variable "desired_size" {
  description = "Desired number of worker nodes"
  type        = number
  default     = 2
}

variable "min_size" {
  description = "Minimum number of worker nodes"
  type        = number
  default     = 1
}

variable "max_size" {
  description = "Maximum number of worker nodes"
  type        = number
  default     = 5
}

variable "tags" {
  description = "Resource tags"
  type        = map(string)
  default     = {}
}

variable "environment" {
  description = "Deployment environment name (e.g. dev, prod)"
  type        = string
}

variable "project" {
  description = "Project name"
  type        = string
  default     = "payment-gateway"
}

variable "tags" {
  description = "Resource tags"
  type        = map(string)
  default     = {}
}

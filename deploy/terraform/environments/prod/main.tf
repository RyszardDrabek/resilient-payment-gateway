terraform {
  required_version = ">= 1.5.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.5"
    }
    tls = {
      source  = "hashicorp/tls"
      version = "~> 4.0"
    }
  }
}

provider "aws" {
  region = var.aws_region

  default_tags {
    tags = {
      Project     = var.project
      Environment = var.environment
      ManagedBy   = "Terraform"
      Compliance  = "PCI-DSS"
    }
  }
}

module "kms" {
  source = "../../modules/kms"

  project     = var.project
  environment = var.environment
}

module "vpc" {
  source = "../../modules/vpc"

  project     = var.project
  environment = var.environment
  vpc_cidr    = var.vpc_cidr
  azs         = var.azs

  public_subnet_cidrs       = ["10.20.1.0/24", "10.20.2.0/24", "10.20.3.0/24"]
  private_app_subnet_cidrs   = ["10.20.11.0/24", "10.20.12.0/24", "10.20.13.0/24"]
  private_data_subnet_cidrs  = ["10.20.21.0/24", "10.20.22.0/24", "10.20.23.0/24"]

  # High-Availability: 1 NAT Gateway per AZ (3 total)
  single_nat_gateway = false
}

module "eks" {
  source = "../../modules/eks"

  project                = var.project
  environment            = var.environment
  cluster_version        = "1.31"
  vpc_id                 = module.vpc.vpc_id
  subnet_ids             = module.vpc.private_app_subnet_ids
  node_security_group_id = module.vpc.eks_nodes_security_group_id
  kms_key_arn            = module.kms.eks_key_arn

  instance_types = ["m6i.large", "c6i.large"]
  capacity_type  = "ON_DEMAND"
  desired_size   = 3
  min_size       = 3
  max_size       = 12
}

module "rds" {
  source = "../../modules/rds"

  project           = var.project
  environment       = var.environment
  database_name     = "payment_gateway"
  subnet_ids        = module.vpc.private_data_subnet_ids
  security_group_id = module.vpc.db_security_group_id
  kms_key_arn       = module.kms.rds_key_arn

  # Production Aurora Serverless v2 Multi-AZ HA scaling
  min_capacity            = 1.0
  max_capacity            = 16.0
  instance_count          = 2
  backup_retention_period = 35
  deletion_protection     = true
}

module "mq" {
  source = "../../modules/mq"

  project           = var.project
  environment       = var.environment
  engine_version    = "3.13"
  host_instance_type = "mq.m5.large"
  deployment_mode   = "CLUSTER_MULTI_AZ"
  subnet_ids        = module.vpc.private_data_subnet_ids
  security_group_id = module.vpc.mq_security_group_id
  kms_key_arn       = module.kms.mq_key_arn
}

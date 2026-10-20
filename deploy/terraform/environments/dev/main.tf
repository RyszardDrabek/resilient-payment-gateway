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

  public_subnet_cidrs       = ["10.10.1.0/24", "10.10.2.0/24", "10.10.3.0/24"]
  private_app_subnet_cidrs  = ["10.10.11.0/24", "10.10.12.0/24", "10.10.13.0/24"]
  private_data_subnet_cidrs = ["10.10.21.0/24", "10.10.22.0/24", "10.10.23.0/24"]

  # Single NAT Gateway to optimize dev environment cost
  single_nat_gateway = true
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

  instance_types = ["t3.medium"]
  capacity_type  = "SPOT"
  desired_size   = 2
  min_size       = 1
  max_size       = 4
}

module "rds" {
  source = "../../modules/rds"

  project           = var.project
  environment       = var.environment
  database_name     = "payment_gateway"
  subnet_ids        = module.vpc.private_data_subnet_ids
  security_group_id = module.vpc.db_security_group_id
  kms_key_arn       = module.kms.rds_key_arn

  # Dev Aurora Serverless v2 capacity limits
  min_capacity            = 0.5
  max_capacity            = 2.0
  instance_count          = 1
  backup_retention_period = 7
  deletion_protection     = false
}

module "mq" {
  source = "../../modules/mq"

  project            = var.project
  environment        = var.environment
  engine_version     = "3.13"
  host_instance_type = "mq.t3.micro"
  deployment_mode    = "SINGLE_INSTANCE"
  subnet_ids         = module.vpc.private_data_subnet_ids
  security_group_id  = module.vpc.mq_security_group_id
  kms_key_arn        = module.kms.mq_key_arn
}

# ADR-023: Cloud Infrastructure as Code (IaC) with Terraform for AWS EKS, Aurora Serverless & Amazon MQ

## Status

Accepted — 2026-10-20

## Context

While the gateway container workload is packageable via Docker Compose (Phase 1) and Helm charts (Phase 8), running an enterprise, PCI-DSS compliant financial payment processor in the cloud requires automated, audited, and reproducible infrastructure as code (IaC):

1. **Network Segmentation & Isolation**: Payment card industry standards (PCI-DSS Requirement 1 & 2) mandate strict separation between public ingress, application compute, and database storage tiers with default-deny firewall policies.
2. **Managed Kubernetes Orchestration (EKS)**: To avoid operational overhead and patching of master nodes, managed control planes with auto-scaling node groups and IAM Roles for Service Accounts (IRSA) are needed for least-privilege pod access.
3. **Managed Storage & Messaging with High Availability**: Production state requires automated multi-AZ replication, automated point-in-time recovery (PITR), and storage encryption using Customer Managed Keys (CMKs) in AWS KMS.
4. **Environment Portability & Cost Efficiency**: The infrastructure definition must support both a cost-optimized `dev` environment (single-NAT, single-AZ serverless Aurora, spot compute) and an enterprise `prod` environment (multi-AZ Aurora cluster, 3-AZ NAT gateways, on-demand compute, deletion protection).

## Decision

The gateway implements a modular Infrastructure as Code (IaC) architecture using **Terraform / OpenTofu** targeted at **Amazon Web Services (AWS)**:

### 1. Network Topology (`modules/vpc`)
- **3 Availability Zones (AZ)** per region for resilience.
- **Three-tier subnet architecture**:
  - `Public subnets`: Internet Gateway, Application Load Balancers (ALBs), NAT Gateways.
  - `Private App subnets`: EKS worker nodes, pods, and private endpoints. Tagged for Kubernetes internal load balancers (`kubernetes.io/role/internal-elb = 1`).
  - `Private Data subnets`: RDS Aurora PostgreSQL and Amazon MQ RabbitMQ. Completely isolated with no outbound internet access.
- Security group chaining: ALB -> EKS Node Security Group -> RDS / RabbitMQ Security Groups (only port 5432 and 5671 allowed from EKS).

### 2. Managed Kubernetes (`modules/eks`)
- Amazon EKS cluster running Kubernetes 1.31 with KMS envelope encryption for cluster secrets.
- EKS Managed Node Groups with auto-scaling (min/max/desired).
- OIDC Identity Provider association enabling **IAM Roles for Service Accounts (IRSA)** for least-privilege AWS IAM policies without hardcoded access keys.
- EKS Add-ons: Amazon VPC CNI, CoreDNS, Kube-Proxy, and AWS EBS CSI driver.

### 3. Managed Relational Database (`modules/rds`)
- **Amazon Aurora Serverless v2 PostgreSQL** (compatible with PostgreSQL 16 and `pgvector`).
- ACU auto-scaling (min 0.5 ACU up to max ACUs depending on environment).
- Encryption at rest via KMS CMK, automated daily backups with PITR (7 days in dev, 35 days in prod).
- Master credentials securely generated and stored in **AWS Secrets Manager**.

### 4. Managed Message Broker (`modules/mq`)
- **Amazon MQ for RabbitMQ** with private VPC deployment.
- Broker instance types sized for environment (single-instance for dev, cluster deployment for prod).
- Encrypted in transit (TLS 5671) and at rest (KMS).

### 5. KMS Security & Secrets (`modules/kms`)
- Dedicated Customer Managed Keys (CMKs) with key rotation enabled.
- Separate key aliases for EKS secrets, RDS storage, and message queues.

### 6. Environment Layout & Helm Glue
- `deploy/terraform/environments/dev`: Cost-conscious staging environment.
- `deploy/terraform/environments/prod`: Hardened, highly-available production environment.
- Outputs expose structured connection parameters (`database_endpoint`, `rabbitmq_endpoint`, `irsa_role_arns`) matching Helm `values.yaml` inputs.

## Consequences

- **Reproducibility**: Infrastructure can be audited, reviewed via Git pull requests, and provisioned identically across environments.
- **PCI-DSS Compliance**: Automated network segregation, KMS envelope encryption, and non-internet-routable database subnets.
- **Operational Integration**: Seamless bridge between Terraform outputs and Helm values for deployment automation.

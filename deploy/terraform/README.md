# Payment Gateway Cloud Infrastructure (AWS Terraform)

This directory contains production-ready **Infrastructure as Code (IaC)** using **Terraform / OpenTofu** for deploying the Resilient Payment Gateway stack onto **Amazon Web Services (AWS)** in compliance with PCI-DSS network segmentation and encryption requirements.

---

## 🏛️ Architecture Overview

```
                      Internet
                         │
                         ▼
             [ AWS Internet Gateway ]
                         │
        ┌────────────────┴────────────────┐
        ▼                                 ▼
┌──────────────────┐             ┌──────────────────┐
│  Public Subnet   │             │  Public Subnet   │
│  (ALB / Ingress) │             │  (NAT Gateways)  │
└────────┬─────────┘             └────────┬─────────┘
         │                                │
         ▼                                ▼
┌───────────────────────────────────────────────────┐
│              Private App Subnets                  │
│                                                   │
│   [ Amazon EKS Managed Kubernetes Cluster ]       │
│    ├── API Pods (IRSA -> Secrets Manager)         │
│    ├── Worker Pods (Outbox, Payouts, Billing)     │
│    └── Portal Pods                                │
└────────────────────────┬──────────────────────────┘
                         │ (Ports 5432 / 5671 TLS only)
                         ▼
┌───────────────────────────────────────────────────┐
│        Isolated Private Data Subnets              │
│       (No Internet Ingress or Egress)             │
│                                                   │
│   [ Amazon Aurora Serverless v2 PostgreSQL ]      │
│   • Multi-AZ, pgvector, KMS CMK encrypted        │
│                                                   │
│   [ Amazon MQ for RabbitMQ ]                      │
│   • AMQPS TLS (5671), KMS CMK encrypted           │
└───────────────────────────────────────────────────┘
```

---

## 📦 Directory Structure

```
deploy/terraform/
├── modules/
│   ├── kms/       # Customer Managed Keys (CMKs) with automated rotation
│   ├── vpc/       # 3-tier subnets (Public, Private App, Private Data), NATs & Security Groups
│   ├── eks/       # EKS 1.31 control plane, managed node groups, OIDC & IRSA
│   ├── rds/       # Aurora Serverless v2 PostgreSQL (pgvector, KMS, Secrets Manager)
│   └── mq/        # Amazon MQ RabbitMQ broker with TLS in private subnets
└── environments/
    ├── dev/       # Cost-optimized single-NAT, spot instances, 0.5-2.0 ACU Aurora
    └── prod/      # Multi-AZ HA, 3 NAT gateways, on-demand compute, 1.0-16.0 ACU Aurora
```

---

## 🚀 Quickstart

### Prerequisites
- [Terraform >= 1.5.0](https://www.terraform.io/downloads) (or [OpenTofu](https://opentofu.org/))
- [AWS CLI v2](https://aws.amazon.com/cli/) configured with deployment credentials (`aws configure`)
- [kubectl](https://kubernetes.io/docs/tasks/tools/)
- [Helm v3](https://helm.sh/)

### 1. Initialize and Validate
```bash
cd deploy/terraform/environments/dev
terraform init
terraform validate
```

### 2. Plan and Deploy
```bash
terraform plan -out=tfplan
terraform apply tfplan
```

### 3. Connect kubectl to EKS
```bash
aws eks update-kubeconfig --region eu-west-1 --name $(terraform output -raw eks_cluster_name)
kubectl get nodes
```

### 4. Deploy Gateway Helm Chart with Terraform Outputs
Terraform outputs a ready-to-use snippet for `deploy/helm/payment-gateway/values.yaml`:

```bash
terraform output helm_values_snippet
```

Apply the values to Helm:
```bash
helm upgrade --install payment-gateway ../../../helm/payment-gateway \
  -n payment-gateway --create-namespace \
  --set externalDatabase.enabled=true \
  --set externalDatabase.host=$(terraform output -raw rds_cluster_endpoint) \
  --set externalDatabase.port=5432 \
  --set externalRabbitmq.enabled=true \
  --set externalRabbitmq.host=$(terraform output -raw mq_primary_ssl_endpoint) \
  --set serviceAccount.annotations."eks\.amazonaws\.com/role-arn"=$(terraform output -raw gateway_irsa_role_arn)
```

---

## 🔒 Security & PCI-DSS Compliance Highlights

| Control | Implementation |
|---|---|
| **Network Segmentation (Req 1)** | Isolated private data subnets without internet routes. Security groups strictly chained (ALB -> EKS -> DB/MQ). |
| **KMS Key Management (Req 3)** | Dedicated CMKs for EKS secrets envelope encryption, RDS storage, and MQ storage with annual automated key rotation. |
| **Secrets Zero-Trust (Req 8)** | Dynamic database and broker passwords generated and stored directly in AWS Secrets Manager; IRSA grants least-privilege pod access. |
| **Encrypted Transit (Req 4)** | TLS 1.3 for HTTPS ingress; AMQPS TLS (5671) for messaging; enforced SSL for PostgreSQL (`rds.force_ssl = 1`). |

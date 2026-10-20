resource "aws_kms_key" "eks" {
  description             = "${var.project}-${var.environment}-eks-secrets-cmk"
  deletion_window_in_days = 30
  enable_key_rotation     = true

  tags = merge(var.tags, {
    Name        = "${var.project}-${var.environment}-eks-kms"
    Environment = var.environment
    Component   = "Security"
  })
}

resource "aws_kms_alias" "eks" {
  name          = "alias/${var.project}-${var.environment}-eks"
  target_key_id = aws_kms_key.eks.key_id
}

resource "aws_kms_key" "rds" {
  description             = "${var.project}-${var.environment}-rds-storage-cmk"
  deletion_window_in_days = 30
  enable_key_rotation     = true

  tags = merge(var.tags, {
    Name        = "${var.project}-${var.environment}-rds-kms"
    Environment = var.environment
    Component   = "Database"
  })
}

resource "aws_kms_alias" "rds" {
  name          = "alias/${var.project}-${var.environment}-rds"
  target_key_id = aws_kms_key.rds.key_id
}

resource "aws_kms_key" "mq" {
  description             = "${var.project}-${var.environment}-mq-storage-cmk"
  deletion_window_in_days = 30
  enable_key_rotation     = true

  tags = merge(var.tags, {
    Name        = "${var.project}-${var.environment}-mq-kms"
    Environment = var.environment
    Component   = "Messaging"
  })
}

resource "aws_kms_alias" "mq" {
  name          = "alias/${var.project}-${var.environment}-mq"
  target_key_id = aws_kms_key.mq.key_id
}

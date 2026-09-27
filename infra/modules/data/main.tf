resource "aws_db_subnet_group" "this" {
  name       = "${var.name}-db-subnet-group"
  subnet_ids = var.private_subnet_ids
}

resource "aws_elasticache_subnet_group" "this" {
  name       = "${var.name}-redis-subnet-group"
  subnet_ids = var.private_subnet_ids
}

resource "aws_db_instance" "postgres" {
  identifier                  = "${var.name}-db"
  engine                      = "postgres"
  engine_version              = "18"
  instance_class              = "db.t4g.micro"
  allocated_storage           = 20
  db_name                     = "form_ai"
  username                    = "form_ai_app"
  manage_master_user_password = true # RDS-managed secret, one less thing you generate/store yourself
  db_subnet_group_name        = aws_db_subnet_group.this.name
  vpc_security_group_ids      = [var.data_security_group_id]
  publicly_accessible         = false
  skip_final_snapshot         = true # fine for a portfolio project; a real prod DB would not set this
  backup_retention_period     = 3
}

resource "aws_elasticache_cluster" "redis" {
  cluster_id         = "${var.name}-redis"
  engine             = "redis"
  node_type          = "cache.t4g.micro"
  num_cache_nodes    = 1
  subnet_group_name  = aws_elasticache_subnet_group.this.name # same private subnets as RDS
  security_group_ids = [var.data_security_group_id]
}

# RDS generated and owns the master password when manage_master_user_password = true —
# it lives in its own AWS-managed secret, not one you create. Read it back out here
# so it can be folded into one connection string your app actually understands.
data "aws_secretsmanager_secret_version" "rds_master" {
  secret_id = aws_db_instance.postgres.master_user_secret[0].secret_arn
}

resource "aws_secretsmanager_secret" "app_secrets" {
  name = "${var.name}-app-secrets"
}

resource "aws_secretsmanager_secret_version" "app_secrets" {
  secret_id = aws_secretsmanager_secret.app_secrets.id
  secret_string = jsonencode({
    ConnectionString = "Host=${aws_db_instance.postgres.address};Database=form_ai;Username=form_ai_app;Password=${jsondecode(data.aws_secretsmanager_secret_version.rds_master.secret_string)["password"]}"
    JwtSecret        = var.jwt_secret # generate once with `openssl rand -base64 64`, pass as a TF_VAR, never commit it
    ClaudeApiKey     = var.claude_api_key
    SesSmtpUsername  = var.ses_smtp_username
    SesSmtpPassword  = var.ses_smtp_password
  })
}

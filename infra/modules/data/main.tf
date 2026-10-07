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
  instance_class              = "db.t3.micro"
  allocated_storage           = 20
  db_name                     = "form_ai"
  username                    = "form_ai_admin" # master: only the one-off db-bootstrap task uses it; the app connects as form_ai_app
  manage_master_user_password = true            # RDS-managed secret, one less thing you generate/store yourself
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

# The two database roles are created inside the database by the db-bootstrap task
# (docker/postgres/init/01-create-app-user.sh, see container-platform), not by Terraform:
# RDS sits in private subnets, so nothing outside the VPC can run SQL against it. Terraform
# only generates their passwords and stores them, so the same values reach both the task
# that creates the roles and the tasks that log in with them.
resource "random_password" "app_db" {
  length  = 32
  special = false # ends up in a `key=value;` connection string, where ; and = would need escaping
}

resource "random_password" "migrator_db" {
  length  = 32
  special = false
}

# Read by the API tasks' execution role (container-platform), so it must hold nothing but what
# the API needs at runtime: no DDL credentials.
resource "aws_secretsmanager_secret" "app_secrets" {
  name = "${var.name}-app-secrets"
}

resource "aws_secretsmanager_secret_version" "app_secrets" {
  secret_id = aws_secretsmanager_secret.app_secrets.id
  secret_string = jsonencode({
    ConnectionString = "Host=${aws_db_instance.postgres.address};Database=form_ai;Username=form_ai_app;Password=${random_password.app_db.result}"
    JwtSecret        = var.jwt_secret # generate once with `openssl rand -base64 64`, pass as a TF_VAR, never commit it
    SesSmtpUsername  = var.ses_smtp_username
    SesSmtpPassword  = var.ses_smtp_password
  })
}

# Read only by the db-bootstrap and db-migrate tasks (a separate execution role in container-platform)
# and never by the API, so a compromised API container cannot reach the migrator's password.
resource "aws_secretsmanager_secret" "db_job_secrets" {
  name = "${var.name}-db-job-secrets"
}

resource "aws_secretsmanager_secret_version" "db_job_secrets" {
  secret_id = aws_secretsmanager_secret.db_job_secrets.id
  secret_string = jsonencode({
    AppPassword              = random_password.app_db.result
    MigratorPassword         = random_password.migrator_db.result
    MigratorConnectionString = "Host=${aws_db_instance.postgres.address};Database=form_ai;Username=form_ai_migrator;Password=${random_password.migrator_db.result}"
  })
}

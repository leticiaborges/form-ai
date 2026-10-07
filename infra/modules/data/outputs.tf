output "rds_address" {
  value = aws_db_instance.postgres.address
}

output "redis_address" {
  value = aws_elasticache_cluster.redis.cache_nodes[0].address
}

output "app_secrets_arn" {
  value = aws_secretsmanager_secret.app_secrets.arn
}

output "db_name" {
  value = aws_db_instance.postgres.db_name
}

# RDS-managed secret holding the master user's `username` and `password`.
output "master_secret_arn" {
  value = aws_db_instance.postgres.master_user_secret[0].secret_arn
}

output "db_job_secrets_arn" {
  value = aws_secretsmanager_secret.db_job_secrets.arn
}

output "ai_app_key_secret_arn" {
  value = aws_secretsmanager_secret.ai_app_key.arn
}

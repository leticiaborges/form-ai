output "rds_address" {
  value = aws_db_instance.postgres.address
}

output "redis_address" {
  value = aws_elasticache_cluster.redis.cache_nodes[0].address
}

output "app_secrets_arn" {
  value = aws_secretsmanager_secret.app_secrets.arn
}

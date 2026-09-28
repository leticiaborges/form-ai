output "ecr_repository_arn" {
  value = aws_ecr_repository.api.arn
}

output "ecs_service_id" {
  value = aws_ecs_service.api.id
}

output "alb_dns_name" {
  value = aws_lb.this.dns_name
}

output "alb_zone_id" {
  value = aws_lb.this.zone_id
}

output "ecr_repository_url" {
  value = aws_ecr_repository.api.repository_url
}

# Needed so deploy.yml's role can be granted iam:PassRole scoped to exactly
# these two ARNs — registering a task definition revision requires PassRole
# on whatever executionRoleArn/taskRoleArn it references.
output "ecs_execution_role_arn" {
  value = aws_iam_role.ecs_execution.arn
}

output "ecs_task_role_arn" {
  value = aws_iam_role.ecs_task.arn
}

output "ecr_migrator_repository_arn" {
  value = aws_ecr_repository.migrator.arn
}

output "ecr_migrator_repository_url" {
  value = aws_ecr_repository.migrator.repository_url
}

output "ecs_cluster_name" {
  value = aws_ecs_cluster.this.name
}

output "ecs_cluster_arn" {
  value = aws_ecs_cluster.this.arn
}

output "ecs_db_jobs_execution_role_arn" {
  value = aws_iam_role.ecs_db_jobs_execution.arn
}

output "db_bootstrap_task_definition_family" {
  value = aws_ecs_task_definition.db_bootstrap.family
}

output "db_migrate_task_definition_family" {
  value = aws_ecs_task_definition.db_migrate.family
}

# Without the revision, so a policy can allow "any revision of this family".
output "db_migrate_task_definition_arn_without_revision" {
  value = aws_ecs_task_definition.db_migrate.arn_without_revision
}

output "db_jobs_log_group_arn" {
  value = aws_cloudwatch_log_group.db_jobs.arn
}

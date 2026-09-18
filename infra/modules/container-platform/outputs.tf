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
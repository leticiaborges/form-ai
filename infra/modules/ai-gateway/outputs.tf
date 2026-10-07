output "gateway_url" {
  description = "Base URL the apps use. prod's ai_gateway_url must equal this."
  value       = "http://litellm.ai.internal:4000"
}
output "ecr_repository_url" { value = aws_ecr_repository.litellm.repository_url }
output "ecr_repository_arn" { value = aws_ecr_repository.litellm.arn }
output "cluster_name" { value = aws_ecs_cluster.this.name }
output "service_name" { value = aws_ecs_service.litellm.name }
output "service_id" { value = aws_ecs_service.litellm.id }
output "execution_role_arn" { value = aws_iam_role.execution.arn }
output "task_role_arn" { value = aws_iam_role.task.arn }
output "db_bootstrap_task_definition_family" { value = aws_ecs_task_definition.db_bootstrap.family }
output "provision_task_definition_family" { value = aws_ecs_task_definition.provision_app_key.family }

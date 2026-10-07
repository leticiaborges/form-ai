output "gateway_url" { value = module.ai_gateway.gateway_url }
output "ecr_repository_url" { value = module.ai_gateway.ecr_repository_url }

output "db_bootstrap_command" {
  description = "Run once before the first start of the service: creates the litellm role and database on the shared RDS."
  value       = <<-EOT
    aws ecs run-task --cluster ${module.ai_gateway.cluster_name} --launch-type FARGATE \
      --task-definition ${module.ai_gateway.db_bootstrap_task_definition_family} \
      --network-configuration "awsvpcConfiguration={subnets=[${join(",", data.terraform_remote_state.prod.outputs.public_subnet_ids)}],securityGroups=[${data.terraform_remote_state.prod.outputs.ecs_security_group_id}],assignPublicIp=ENABLED}"
  EOT
}

output "provision_app_key_command" {
  description = "Run once the gateway is healthy: registers the form-ai-app key and its monthly budget."
  value       = <<-EOT
    aws ecs run-task --cluster ${module.ai_gateway.cluster_name} --launch-type FARGATE \
      --task-definition ${module.ai_gateway.provision_task_definition_family} \
      --network-configuration "awsvpcConfiguration={subnets=[${join(",", data.terraform_remote_state.prod.outputs.public_subnet_ids)}],securityGroups=[${data.terraform_remote_state.prod.outputs.ecs_security_group_id}],assignPublicIp=ENABLED}"
  EOT
}

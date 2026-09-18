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
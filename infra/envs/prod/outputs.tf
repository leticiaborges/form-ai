output "vpc_id" {
  description = "ID of the VPC all resources run in."
  value       = module.networking.vpc_id
}

output "alb_dns_name" {
  description = "ALB's own DNS name (aws_route53_record.api aliases to this)."
  value       = module.container_platform.alb_dns_name
}

output "ecr_repository_url" {
  description = "Push target for the API image, e.g. in deploy.yml."
  value       = module.container_platform.ecr_repository_url
}

output "rds_address" {
  description = "RDS PostgreSQL endpoint hostname."
  value       = module.data.rds_address
}

output "redis_address" {
  description = "ElastiCache Redis endpoint hostname (SignalR backplane)."
  value       = module.data.redis_address
}

output "cloudfront_domain_name" {
  description = "CloudFront distribution's own domain name (aws_route53_record.frontend aliases to this)."
  value       = module.frontend.cloudfront_distribution_domain_name
}

output "frontend_bucket_name" {
  description = "S3 bucket the frontend build is synced into."
  value       = module.frontend.bucket_name
}

output "api_url" {
  description = "Public URL of the API."
  value       = "https://${var.api_domain_name}"
}

output "frontend_url" {
  description = "Public URL of the frontend."
  value       = "https://${var.frontend_domain_name}"
}

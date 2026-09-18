variable "name" {
  description = "Resource name prefix."
  type        = string
}

variable "region" {
  description = "AWS region, used for the awslogs log configuration."
  type        = string
}

variable "vpc_id" {
  description = "VPC ID (from the networking module)."
  type        = string
}

variable "public_subnet_ids" {
  description = "Public subnet IDs the ALB and ECS tasks run in (from the networking module)."
  type        = list(string)
}

variable "alb_security_group_id" {
  description = "Security group ID for the ALB (from the networking module)."
  type        = string
}

variable "ecs_security_group_id" {
  description = "Security group ID for the ECS tasks (from the networking module)."
  type        = string
}

variable "acm_certificate_arn" {
  description = "ACM certificate ARN for the ALB's HTTPS listener (from the dns-tls module)."
  type        = string
}

variable "app_secrets_arn" {
  description = "ARN of the Secrets Manager secret holding ConnectionString/JwtSecret/ClaudeApiKey (from the data module)."
  type        = string
}

variable "redis_address" {
  description = "ElastiCache Redis endpoint hostname (from the data module)."
  type        = string
}

variable "frontend_base_url" {
  description = "Public URL of the frontend, used for Email__FrontendBaseUrl."
  type        = string
}

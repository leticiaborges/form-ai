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
  description = "ARN of the Secrets Manager secret holding ConnectionString/JwtSecret/AiApiKey (from the data module)."
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

variable "rds_address" {
  description = "RDS PostgreSQL endpoint hostname (from the data module)."
  type        = string
}

variable "db_name" {
  description = "Name of the application database (from the data module)."
  type        = string
}

variable "master_secret_arn" {
  description = "ARN of the RDS-managed secret holding the master user's username and password (from the data module)."
  type        = string
}

variable "db_job_secrets_arn" {
  description = "ARN of the secret holding the role passwords and the migrator connection string (from the data module)."
  type        = string
}

variable "ses_email_smtphost" {
  description = "SMTP Host."
  type        = string
}

variable "ses_email_smtpport" {
  description = "SMTP Port."
  type        = number
}

variable "ses_email_smtpfromaddress" {
  description = "SMTP From Address."
  type        = string
}

variable "ses_email_smtpfromname" {
  description = "SMTP From Name."
  type        = string
}

variable "ai_gateway_url" {
  description = "Base URL of the AI gateway (ai-gateway root output gateway_url)."
  type        = string
}

variable "ai_app_key_secret_arn" {
  description = "ARN of the secret holding the gateway app key (from the data module)."
  type        = string
}

variable "origin_verify_secret" {
  type        = string
  sensitive   = true
  description = "Value of the X-Origin-Verify header that CloudFront sends to the API origin."
}

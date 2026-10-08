variable "name" {
  description = "Resource name prefix."
  type        = string
}

variable "data_security_group_id" {
  description = "Security group ID shared by RDS and ElastiCache (from the networking module)."
  type        = string
}

variable "private_subnet_ids" {
  description = "Private subnet IDs RDS and ElastiCache are placed in (from the networking module)."
  type        = list(string)
}

variable "jwt_secret" {
  description = "JWT signing secret, folded into app_secrets."
  type        = string
  sensitive   = true
}

variable "ses_smtp_username" {
  description = "AWS SES SMTP username"
  type        = string
  sensitive   = true
}

variable "ses_smtp_password" {
  description = "AWS SES SMTP password"
  type        = string
  sensitive   = true
}

resource "random_password" "ai_app_key" {
  length  = 48
  special = false
}

resource "aws_secretsmanager_secret" "ai_app_key" {
  name = "${var.name}-ai-app-key"
}

resource "aws_secretsmanager_secret_version" "ai_app_key" {
  secret_id     = aws_secretsmanager_secret.ai_app_key.id
  secret_string = "sk-${random_password.ai_app_key.result}" # LiteLLM requires the sk- prefix
}

variable "demo_account_password" {
  description = "Demo account password"
  type        = string
  sensitive   = true
}

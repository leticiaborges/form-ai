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

variable "claude_api_key" {
  description = "Anthropic API key, folded into app_secrets."
  type        = string
  sensitive   = true
}

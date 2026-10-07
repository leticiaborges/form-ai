variable "region" {
  description = "AWS region for all resources. us-east-1 is also required for the CloudFront ACM cert regardless of this value."
  type        = string
  default     = "us-east-1"
}

variable "name" {
  description = "Resource name prefix for this environment, for example: formai-prod."
  type        = string
  default     = "formai-prod"
}

variable "vpc_cidr" {
  description = "CIDR block for the VPC."
  type        = string
  default     = "10.0.0.0/16"
}

variable "public_subnets" {
  description = "Public subnets (ALB + ECS tasks), keyed by availability zone suffix."
  type        = map(string)
  default = {
    a = "10.0.1.0/24"
    b = "10.0.2.0/24"
  }
}

variable "private_subnets" {
  description = "Private subnets (RDS + ElastiCache), keyed by availability zone suffix."
  type        = map(string)
  default = {
    a = "10.0.11.0/24"
    b = "10.0.12.0/24"
  }
}

variable "hosted_zone_id" {
  description = "Route 53 hosted zone ID for the domain registered in Phase 1."
  type        = string
}

variable "api_domain_name" {
  description = "Domain the API/ALB answers on, e.g. api.yourdomain.com."
  type        = string
}

variable "frontend_domain_name" {
  description = "Domain the frontend/CloudFront distribution answers on, e.g. app.yourdomain.com."
  type        = string
}

variable "jwt_secret" {
  description = "JWT signing secret. Generate once with `openssl rand -base64 64`, pass as TF_VAR_jwt_secret, never commit it."
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

variable "ai_gateway_url" {
  description = "Where the AI gateway answers inside the VPC. Must equal the ai-gateway root's gateway_url output."
  type        = string
  default     = "http://litellm.ai.internal:4000"
}

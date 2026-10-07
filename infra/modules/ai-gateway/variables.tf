variable "name" {
  description = "Resource name prefix, e.g. formai-ai-gateway."
  type        = string
}

variable "region" {
  type = string
}

variable "vpc_id" {
  type = string
}

variable "subnet_ids" {
  description = "Public subnets the gateway task runs in (no NAT gateway, so it needs a public IP to reach the providers)."
  type        = list(string)
}

variable "gateway_security_group_id" {
  description = "Security group of the gateway service: ingress 4000 from the API's SG, egress open. Created by prod's networking module."
  type        = string
}

variable "client_security_group_id" {
  description = "Security group the one-off tasks (db bootstrap, key provisioning) run in: the API's SG, already allowed into RDS and into the gateway."
  type        = string
}

variable "rds_address" {
  type = string
}

variable "master_secret_arn" {
  description = "RDS-managed secret with the master user's username/password. Read only by the one-off DB bootstrap task."
  type        = string
}

variable "app_key_secret_arn" {
  description = "Secret holding the form-ai-app key value (owned by prod). Read only by the provision task."
  type        = string
}

variable "anthropic_api_key" {
  type      = string
  sensitive = true
}

variable "openai_api_key" {
  type      = string
  sensitive = true
}

variable "desired_count" {
  description = "0 for the first apply (no image exists yet), then 1."
  type        = number
  default     = 1
}

variable "cpu_architecture" {
  description = "ARM64 is ~20% cheaper; use X86_64 if the base image has no arm64 variant."
  type        = string
  default     = "ARM64"
}

variable "app_key_max_budget_usd" {
  description = "Monthly budget of the form-ai-app key (LiteLLM's backstop)."
  type        = string
  default     = "10"
}

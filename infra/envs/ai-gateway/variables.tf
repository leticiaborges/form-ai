variable "region" {
  type    = string
  default = "us-east-1"
}
variable "name" {
  description = "Resource name prefix. Must start with formai- (the Terraform CI role may only manage formai-* IAM roles)."
  type        = string
  default     = "formai-ai-gateway"
}
variable "anthropic_api_key" {
  description = "Pass as TF_VAR_anthropic_api_key, never commit it."
  type        = string
  sensitive   = true
}
variable "openai_api_key" {
  description = "Pass as TF_VAR_openai_api_key, never commit it."
  type        = string
  sensitive   = true
}
variable "desired_count" {
  description = "0 for the first apply (no image yet), then 1."
  type        = number
  default     = 1
}

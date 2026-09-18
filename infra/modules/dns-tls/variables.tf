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

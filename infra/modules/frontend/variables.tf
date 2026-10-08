variable "name" {
  description = "Resource name prefix."
  type        = string
}

variable "frontend_domain_name" {
  description = "Domain the CloudFront distribution answers on, e.g. app.yourdomain.com."
  type        = string
}

variable "cloudfront_acm_certificate_arn" {
  description = "ACM certificate ARN for the CloudFront distribution, must be in us-east-1 (from the dns-tls module)."
  type        = string
}

variable "api_domain_name" {
  description = "Domain the API/ALB answers on, e.g. api.yourdomain.com. Used as the CloudFront origin for /api/* and /hubs/*, so the cookie-based session is same-origin in prod."
  type        = string
}

variable "origin_verify_secret" {
  type        = string
  sensitive   = true
  description = "Value of the X-Origin-Verify header that CloudFront sends to the API origin."
}

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

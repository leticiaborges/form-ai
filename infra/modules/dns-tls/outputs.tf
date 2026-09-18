output "api_certificate_arn" {
  value = aws_acm_certificate_validation.api.certificate_arn
}

output "cloudfront_certificate_arn" {
  value = aws_acm_certificate_validation.cloudfront.certificate_arn
}

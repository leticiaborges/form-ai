resource "aws_acm_certificate" "api" {
  domain_name       = var.api_domain_name
  validation_method = "DNS"
}

resource "aws_route53_record" "api_cert_validation" {
  for_each = { for dvo in aws_acm_certificate.api.domain_validation_options : dvo.domain_name => dvo }
  zone_id  = var.hosted_zone_id
  name     = each.value.resource_record_name
  type     = each.value.resource_record_type
  records  = [each.value.resource_record_value]
  ttl      = 60
}

resource "aws_acm_certificate_validation" "api" {
  certificate_arn         = aws_acm_certificate.api.arn
  validation_record_fqdns = [for r in aws_route53_record.api_cert_validation : r.fqdn]
}

# CloudFront requires its cert in us-east-1 specifically — this deployment's
# provider is already us-east-1 (see envs/prod/main.tf), so no separate
# aliased provider is needed here, unlike a deployment based in another region.
resource "aws_acm_certificate" "cloudfront" {
  domain_name       = var.frontend_domain_name
  validation_method = "DNS"
}

resource "aws_route53_record" "cloudfront_cert_validation" {
  for_each = { for dvo in aws_acm_certificate.cloudfront.domain_validation_options : dvo.domain_name => dvo }
  zone_id  = var.hosted_zone_id
  name     = each.value.resource_record_name
  type     = each.value.resource_record_type
  records  = [each.value.resource_record_value]
  ttl      = 60
}

resource "aws_acm_certificate_validation" "cloudfront" {
  certificate_arn         = aws_acm_certificate.cloudfront.arn
  validation_record_fqdns = [for r in aws_route53_record.cloudfront_cert_validation : r.fqdn]
}

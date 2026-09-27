provider "aws" {
  region = var.region
}

module "networking" {
  source = "../../modules/networking"

  name            = var.name
  region          = var.region
  vpc_cidr        = var.vpc_cidr
  public_subnets  = var.public_subnets
  private_subnets = var.private_subnets
}

module "data" {
  source = "../../modules/data"

  name                   = var.name
  data_security_group_id = module.networking.data_security_group_id
  private_subnet_ids     = module.networking.private_subnet_ids
  jwt_secret             = var.jwt_secret
  claude_api_key         = var.claude_api_key
}

# Cert-only: creating the ALB's DNS-validated cert here has no dependency on the
# ALB itself, so it can't form a cycle with container_platform below.
module "dns_tls" {
  source = "../../modules/dns-tls"

  hosted_zone_id       = var.hosted_zone_id
  api_domain_name      = var.api_domain_name
  frontend_domain_name = var.frontend_domain_name
}

module "container_platform" {
  source = "../../modules/container-platform"

  name                  = var.name
  region                = var.region
  vpc_id                = module.networking.vpc_id
  public_subnet_ids     = module.networking.public_subnet_ids
  alb_security_group_id = module.networking.alb_security_group_id
  ecs_security_group_id = module.networking.ecs_security_group_id
  acm_certificate_arn   = module.dns_tls.api_certificate_arn
  app_secrets_arn       = module.data.app_secrets_arn
  redis_address         = module.data.redis_address
  frontend_base_url     = "https://${var.frontend_domain_name}"
}

module "frontend" {
  source = "../../modules/frontend"

  name                           = var.name
  frontend_domain_name           = var.frontend_domain_name
  cloudfront_acm_certificate_arn = module.dns_tls.cloudfront_certificate_arn
  api_domain_name                = var.api_domain_name
}

# The alias record needs the ALB's dns_name/zone_id, which only exists once
# container_platform is created — so it's declared here, not inside dns_tls,
# to avoid a module dependency cycle (dns_tls -> container_platform -> dns_tls).
resource "aws_route53_record" "api" {
  zone_id = var.hosted_zone_id
  name    = var.api_domain_name
  type    = "A"
  alias {
    name                   = module.container_platform.alb_dns_name
    zone_id                = module.container_platform.alb_zone_id
    evaluate_target_health = true
  }
}

# Same reasoning as above, for the frontend distribution. CloudFront's hosted
# zone ID is a fixed, well-known value for every distribution.
resource "aws_route53_record" "frontend" {
  zone_id = var.hosted_zone_id
  name    = var.frontend_domain_name
  type    = "A"
  alias {
    name                   = module.frontend.cloudfront_distribution_domain_name
    zone_id                = "Z2FDTNDATAQYW2"
    evaluate_target_health = false
  }
}

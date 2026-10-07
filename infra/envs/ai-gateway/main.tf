
provider "aws" {
  region = var.region
}

data "terraform_remote_state" "prod" {
  backend = "s3"
  config = {
    bucket = "formai-terraform-state"
    key    = "prod/terraform.tfstate"
    region = "us-east-1"
  }
}

module "ai_gateway" {
  source = "../../modules/ai-gateway"

  name                      = var.name
  region                    = var.region
  vpc_id                    = data.terraform_remote_state.prod.outputs.vpc_id
  subnet_ids                = data.terraform_remote_state.prod.outputs.public_subnet_ids
  gateway_security_group_id = data.terraform_remote_state.prod.outputs.ai_gateway_security_group_id
  client_security_group_id  = data.terraform_remote_state.prod.outputs.ecs_security_group_id
  rds_address               = data.terraform_remote_state.prod.outputs.rds_address
  master_secret_arn         = data.terraform_remote_state.prod.outputs.master_secret_arn
  app_key_secret_arn        = data.terraform_remote_state.prod.outputs.ai_app_key_secret_arn
  anthropic_api_key         = var.anthropic_api_key
  openai_api_key            = var.openai_api_key
  desired_count             = var.desired_count
}

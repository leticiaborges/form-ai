data "aws_caller_identity" "current" {}

locals {
  account_id               = data.aws_caller_identity.current.account_id
  github_oidc_provider_arn = "arn:aws:iam::${local.account_id}:oidc-provider/token.actions.githubusercontent.com"
}

# Deploy role for ai-gateway-deploy.yml. Lives in this root (not prod) so the gateway stays separable.
resource "aws_iam_role" "github_actions_ai_gateway_deploy" {
  name = "formai-github-actions-ai-gateway-deploy"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Federated = local.github_oidc_provider_arn }
      Action    = "sts:AssumeRoleWithWebIdentity"
      Condition = {
        StringEquals = {
          "token.actions.githubusercontent.com:aud" = "sts.amazonaws.com"
          "token.actions.githubusercontent.com:sub" = "repo:leticiaborges/form-ai:ref:refs/heads/main"
        }
      }
    }]
  })
}

data "aws_iam_policy_document" "ai_gateway_deploy" {
  statement {
    sid       = "ECRAuth"
    actions   = ["ecr:GetAuthorizationToken"]
    resources = ["*"]
  }
  statement {
    sid = "ECRPush"
    actions = [
      "ecr:BatchCheckLayerAvailability", "ecr:PutImage",
      "ecr:InitiateLayerUpload", "ecr:UploadLayerPart", "ecr:CompleteLayerUpload"
    ]
    resources = [module.ai_gateway.ecr_repository_arn]
  }
  statement {
    sid       = "ECSDeploy"
    actions   = ["ecs:UpdateService", "ecs:DescribeServices"]
    resources = [module.ai_gateway.service_id]
  }
  statement {
    sid       = "ECSTaskDefinitionRegister"
    actions   = ["ecs:RegisterTaskDefinition", "ecs:DescribeTaskDefinition"]
    resources = ["*"] # neither action supports resource-level scoping
  }
  statement {
    sid       = "PassGatewayRoles"
    actions   = ["iam:PassRole"]
    resources = [module.ai_gateway.execution_role_arn, module.ai_gateway.task_role_arn]
  }
}

resource "aws_iam_role_policy" "ai_gateway_deploy" {
  name   = "formai-github-actions-ai-gateway-deploy-policy"
  role   = aws_iam_role.github_actions_ai_gateway_deploy.id
  policy = data.aws_iam_policy_document.ai_gateway_deploy.json
}

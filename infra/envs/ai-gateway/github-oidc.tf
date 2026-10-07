# Deploy role for ai-gateway-deploy.yml. Lives in this root (not prod) so the gateway stays separable.
resource "aws_iam_role" "github_actions_ai_gateway_deploy" {
  name = "formai-github-actions-ai-gateway-deploy"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Federated = "arn:aws:iam::058264176602:oidc-provider/token.actions.githubusercontent.com" }
      Action    = "sts:AssumeRoleWithWebIdentity"
      Condition = {
        StringEquals = { "token.actions.githubusercontent.com:aud" = "sts.amazonaws.com" }
        StringLike   = { "token.actions.githubusercontent.com:sub" = "repo:leticiaborges/form-ai:*" }
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

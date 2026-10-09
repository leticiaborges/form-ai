resource "aws_iam_role" "github_actions_deploy" {
  name = "formai-github-actions-deploy"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Federated = "arn:aws:iam::058264176602:oidc-provider/token.actions.githubusercontent.com" }
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

# --- Role 1: app deploys (deploy.yml) — tightly scoped ---
data "aws_iam_policy_document" "deploy_permissions" {
  statement {
    sid       = "ECRAuth"
    actions   = ["ecr:GetAuthorizationToken"]
    resources = ["*"] # this action has no resource-level scoping in AWS's model
  }
  statement {
    sid = "ECRPush"
    actions = [
      "ecr:BatchCheckLayerAvailability", "ecr:PutImage",
      "ecr:InitiateLayerUpload", "ecr:UploadLayerPart", "ecr:CompleteLayerUpload"
    ]
    resources = [
      module.container_platform.ecr_repository_arn,
      module.container_platform.ecr_migrator_repository_arn,
    ]
  }
  statement {
    sid       = "ECSDeploy"
    actions   = ["ecs:UpdateService", "ecs:DescribeServices"]
    resources = [module.container_platform.ecs_service_id]
  }
  statement {
    sid       = "ECSTaskDefinitionRegister"
    actions   = ["ecs:RegisterTaskDefinition", "ecs:DescribeTaskDefinition"]
    resources = ["*"] # neither action supports resource-level scoping in AWS's model
  }
  statement {
    sid     = "PassEcsRoles"
    actions = ["iam:PassRole"]
    resources = [
      module.container_platform.ecs_execution_role_arn,
      module.container_platform.ecs_task_role_arn,
      module.container_platform.ecs_db_jobs_execution_role_arn,
    ]
  }
  # Migrations only. The db-bootstrap task uses the RDS master credentials and is
  # deliberately left out: it is run by hand with an admin's own AWS credentials.
  statement {
    sid       = "RunMigrations"
    actions   = ["ecs:RunTask"]
    resources = ["${module.container_platform.db_migrate_task_definition_arn_without_revision}:*"]
    condition {
      test     = "ArnEquals"
      variable = "ecs:cluster"
      values   = [module.container_platform.ecs_cluster_arn]
    }
  }
  statement {
    sid       = "ReadMigrationTasks"
    actions   = ["ecs:DescribeTasks"]
    resources = ["${replace(module.container_platform.ecs_cluster_arn, ":cluster/", ":task/")}/*"]
  }
  statement {
    sid       = "ReadMigrationLogs"
    actions   = ["logs:GetLogEvents"]
    resources = ["${module.container_platform.db_jobs_log_group_arn}:*"]
  }
  # Frontend: `aws s3 sync --delete` of the build, then a CloudFront invalidation.
  statement {
    sid       = "FrontendBucketList"
    actions   = ["s3:ListBucket"]
    resources = [module.frontend.bucket_arn]
  }
  statement {
    sid       = "FrontendBucketWrite"
    actions   = ["s3:PutObject", "s3:DeleteObject"]
    resources = ["${module.frontend.bucket_arn}/*"]
  }
  statement {
    sid       = "FrontendInvalidate"
    actions   = ["cloudfront:CreateInvalidation"]
    resources = [module.frontend.cloudfront_distribution_arn]
  }
}

resource "aws_iam_role_policy" "deploy" {
  name   = "formai-github-actions-deploy-policy"
  role   = aws_iam_role.github_actions_deploy.id
  policy = data.aws_iam_policy_document.deploy_permissions.json
}

# --- Role 2: Terraform apply (infra.yml, push to main) — broad but not admin ---
resource "aws_iam_role" "github_actions_terraform" {
  name = "formai-github-actions-terraform"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Federated = "arn:aws:iam::058264176602:oidc-provider/token.actions.githubusercontent.com" }
      Action    = "sts:AssumeRoleWithWebIdentity"
      Condition = {
        StringEquals = {
          "token.actions.githubusercontent.com:aud" = "sts.amazonaws.com"
          # Only jobs that run in the `production` environment (infra.yml apply jobs).
          "token.actions.githubusercontent.com:sub" = "repo:leticiaborges/form-ai:environment:production"
        }
      }
    }]
  })
}

resource "aws_iam_role_policy_attachment" "terraform_power_user" {
  role       = aws_iam_role.github_actions_terraform.name
  policy_arn = "arn:aws:iam::aws:policy/PowerUserAccess"
}

resource "aws_iam_role_policy" "terraform_iam_scoped" {
  name = "formai-github-actions-terraform-iam"
  role = aws_iam_role.github_actions_terraform.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Action = [
        "iam:CreateRole", "iam:DeleteRole", "iam:GetRole", "iam:UpdateAssumeRolePolicy",
        "iam:PutRolePolicy", "iam:DeleteRolePolicy", "iam:GetRolePolicy",
        "iam:AttachRolePolicy", "iam:DetachRolePolicy", "iam:PassRole",
        "iam:TagRole", "iam:ListRolePolicies", "iam:ListAttachedRolePolicies"
      ]
      Resource = "arn:aws:iam::058264176602:role/formai-*"
    }]
  })
}

# --- Role 3: Terraform plan (infra.yml, pull requests) — read-only ---
# A plan can run PR code (e.g. an `external` data source), so PRs never get the apply role.
resource "aws_iam_role" "github_actions_terraform_plan" {
  name = "formai-github-actions-terraform-plan"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Federated = "arn:aws:iam::058264176602:oidc-provider/token.actions.githubusercontent.com" }
      Action    = "sts:AssumeRoleWithWebIdentity"
      Condition = {
        StringEquals = {
          "token.actions.githubusercontent.com:aud" = "sts.amazonaws.com"
          "token.actions.githubusercontent.com:sub" = "repo:leticiaborges/form-ai:pull_request"
        }
      }
    }]
  })
}

resource "aws_iam_role_policy_attachment" "terraform_plan_read_only" {
  role       = aws_iam_role.github_actions_terraform_plan.name
  policy_arn = "arn:aws:iam::aws:policy/ReadOnlyAccess"
}

data "aws_iam_policy_document" "terraform_plan" {
  # ReadOnlyAccess already reads the state; the S3 lock file (use_lockfile) needs writes.
  statement {
    sid       = "StateLock"
    actions   = ["s3:PutObject", "s3:DeleteObject"]
    resources = ["arn:aws:s3:::formai-terraform-state/*.tflock"]
  }
  # Refreshing aws_secretsmanager_secret_version reads the value, which ReadOnlyAccess leaves out.
  # The same values are already in the state this role reads.
  statement {
    sid       = "RefreshSecretVersions"
    actions   = ["secretsmanager:GetSecretValue"]
    resources = ["arn:aws:secretsmanager:us-east-1:058264176602:secret:formai-*"]
  }
}

resource "aws_iam_role_policy" "terraform_plan" {
  name   = "formai-github-actions-terraform-plan-policy"
  role   = aws_iam_role.github_actions_terraform_plan.id
  policy = data.aws_iam_policy_document.terraform_plan.json
}

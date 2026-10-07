# Plan: deploy the LiteLLM gateway to AWS (Fargate + a second database on the existing RDS)

Implements the recommendation of [phase-38](./phase-38.md): **B1 (Fargate ARM, 0.5 vCPU / 1 GB, 1 task) + A1 (new `litellm` database and role on the existing RDS instance)**. Estimated extra cost: **~$18–20/month** (Fargate ~$14, public IPv4 ~$3.65, Cloud Map private zone ~$0.50, two secrets ~$0.80, logs/ECR cents).

You execute this by hand. Part 1 is the code changes (one commit is fine), Part 2 is the ordered runbook. Nothing is deployed today (you ran `terraform destroy`), so every change below goes in as the *first* apply: no migrations of live state, no downtime.

## Design in one page

```
Internet ──► ALB ──► API task (ecs SG) ──► http://litellm.ai.internal:4000 ──► LiteLLM task (gateway SG) ──► Anthropic / OpenAI
                         │                                                          │
                         └──────────────► RDS  form_ai (form_ai_app)                └──► RDS  litellm (role litellm)
```

Decisions and why:

1. **Own Terraform root + own state** (`infra/envs/ai-gateway`, module `infra/modules/ai-gateway`). The gateway will serve other apps later; extracting it must be "move two folders + `docker/litellm/`". The root *reads* `prod` through `terraform_remote_state`, and `prod` knows the gateway only as a URL string, so there is no cross-root cycle. Apply order is always `prod` first, then `ai-gateway`.
2. **Own ECS cluster, ECR repo, log group, secret, IAM roles, deploy workflow and OIDC role.** Nothing is shared with the API except the VPC, subnets and RDS instance.
3. **Reached by name, not IP.** A Fargate task gets a new IP on every deploy, so the service registers in a **Cloud Map private DNS namespace** (`ai.internal`). The API calls `http://litellm.ai.internal:4000`. Plain HTTP inside the VPC, protected by a security group (4000 only from the API's SG) and by the app key.
4. **Public subnet + public IP, no ingress.** There is no NAT gateway (cost), and the gateway must call Anthropic/OpenAI, so like the API it sits in a public subnet with a public IP. The security group allows nothing in except the API's SG. It is never put behind the ALB.
5. **The app key is owned by the consumer.** `prod` generates the `form-ai-app` key (a random `sk-…` value in its own secret). The API reads it as `Ai__ApiKey`; the gateway only *registers* it with a one-off task running the existing `provision-app-key.sh`. That keeps the dependency one-directional, and every future app can mint its own key the same way. The master key never leaves the gateway's secret.
6. **The gateway's database is separate in every way that matters.** Own database `litellm`, own owner role `litellm`, `PUBLIC` has no access, and `form_ai_app` / `form_ai_migrator` have no access to it (and vice versa). The existing `01-create-app-user.sh` already revokes `PUBLIC` from `form_ai`, so `litellm` cannot connect there. ADR 0007's least-privilege model is untouched. Moving it to its own RDS later is a `pg_dump` / `pg_restore` plus a new `DATABASE_URL` (see phase-38 answer).
7. **One-off tasks run in the API's security group** (like the existing `db-bootstrap`): it is already allowed into RDS, and the provision task needs to reach the gateway from the same network position the API has, which doubles as a connectivity test.

---

# Part 1: Repository changes

## 1.1 Fix `docker/litellm/config.yaml` (before anything else)

`README.md` says prompts and responses are not stored, but the config stores them. On AWS this would put user source text (and base64 PDFs) into the shared RDS instance.

```yaml
litellm_settings:
  turn_off_message_logging: true

general_settings:
  master_key: os.environ/LITELLM_MASTER_KEY
  database_url: os.environ/DATABASE_URL
  store_prompts_in_spend_logs: false
```

Re-run `bash docker/litellm/smoke.sh` locally afterwards (costs a few cents) to confirm nothing depended on stored prompts.

## 1.2 New: `docker/litellm/Dockerfile`

Fargate cannot bind-mount `config.yaml` the way Compose does, so the config is baked into a small image. Compose keeps using the stock image plus the mount; nothing changes locally.

```dockerfile
FROM ghcr.io/berriai/litellm:v1.103.1
COPY config.yaml /app/config.yaml
CMD ["--config", "/app/config.yaml", "--port", "4000"]
```

Keep the tag in step with `docker-compose.yml`. There is no `RUN`, so building for ARM needs no emulation. **Verify the base image has an arm64 variant first**: `docker buildx imagetools inspect ghcr.io/berriai/litellm:v1.103.1` must list `linux/arm64`. If it does not, set `cpu_architecture = "X86_64"` (variable in 1.4) and build for `linux/amd64` (~$3/month more).

## 1.3 New: `docker/litellm/init/01-create-litellm-db.sh`

Same idea as `docker/postgres/init/01-create-app-user.sh`: runs as the RDS master user in a one-off task, idempotent, so a re-run only re-syncs the password.

```sh
#!/bin/sh
# Creates the gateway's own role and database on the shared RDS instance:
#   litellm  owns the `litellm` database and runs LiteLLM's own schema migrations at startup.
# Idempotent: a re-run only re-syncs the password. Needs POSTGRES_USER (RDS master) and
# LITELLM_DB_PASSWORD; connects through PGHOST / PGPASSWORD / PGSSLMODE.
set -e

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
  -v litellm_password="$LITELLM_DB_PASSWORD" \
  -v admin_role="$POSTGRES_USER" <<-'EOSQL'
SELECT 'CREATE ROLE litellm NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION'
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'litellm') \gexec

ALTER ROLE litellm WITH LOGIN PASSWORD :'litellm_password';

-- RDS's master is not a superuser: it can only create a database owned by a role it can SET ROLE to.
GRANT litellm TO :"admin_role" WITH SET TRUE;

-- CREATE DATABASE cannot run inside a transaction block; \gexec runs it as its own statement.
SELECT 'CREATE DATABASE litellm OWNER litellm'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'litellm') \gexec

REVOKE ALL ON DATABASE litellm FROM PUBLIC;
EOSQL
```

Why the owner role matters: LiteLLM runs Prisma migrations on startup, so it needs to create tables. On PG 15+ the database owner can create in `public`, so no extra grants are needed.

## 1.4 New module `infra/modules/ai-gateway/`

Inputs are plain values only (no `module.data` / `container_platform` internals), so it can move to another repo.

**`variables.tf`**

```hcl
variable "name" {
  description = "Resource name prefix, e.g. formai-ai-gateway."
  type        = string
}
variable "region" {
  type = string
}
variable "vpc_id" {
  type = string
}
variable "subnet_ids" {
  description = "Public subnets the gateway task runs in (no NAT gateway, so it needs a public IP to reach the providers)."
  type        = list(string)
}
variable "gateway_security_group_id" {
  description = "Security group of the gateway service: ingress 4000 from the API's SG, egress open. Created by prod's networking module."
  type        = string
}
variable "client_security_group_id" {
  description = "Security group the one-off tasks (db bootstrap, key provisioning) run in: the API's SG, already allowed into RDS and into the gateway."
  type        = string
}
variable "rds_address" {
  type = string
}
variable "master_secret_arn" {
  description = "RDS-managed secret with the master user's username/password. Read only by the one-off DB bootstrap task."
  type        = string
}
variable "app_key_secret_arn" {
  description = "Secret holding the form-ai-app key value (owned by prod). Read only by the provision task."
  type        = string
}
variable "anthropic_api_key" {
  type      = string
  sensitive = true
}
variable "openai_api_key" {
  type      = string
  sensitive = true
}
variable "desired_count" {
  description = "0 for the first apply (no image exists yet), then 1."
  type        = number
  default     = 1
}
variable "cpu_architecture" {
  description = "ARM64 is ~20% cheaper; use X86_64 if the base image has no arm64 variant."
  type        = string
  default     = "ARM64"
}
variable "app_key_max_budget_usd" {
  description = "Monthly budget of the form-ai-app key (LiteLLM's backstop)."
  type        = string
  default     = "10"
}
```

**`main.tf`**

```hcl
resource "aws_ecr_repository" "litellm" {
  name                 = "${var.name}-litellm"
  image_tag_mutability = "IMMUTABLE"
  image_scanning_configuration { scan_on_push = true }
}

resource "aws_ecs_cluster" "this" {
  name = "${var.name}-cluster"
}

resource "aws_cloudwatch_log_group" "litellm" {
  name              = "/ecs/${var.name}"
  retention_in_days = 14
}

# --- Secrets -------------------------------------------------------------------------------
# One secret for everything only the gateway (and its one-off jobs) may read. Generated values
# live in Terraform state (encrypted S3), like the app's DB passwords.
resource "random_password" "master_key" {
  length  = 48
  special = false
}

# Encrypts credentials LiteLLM stores in its database. NEVER change it once data exists.
resource "random_password" "salt_key" {
  length  = 48
  special = false
}

resource "random_password" "db" {
  length  = 32
  special = false # goes into a URL (DATABASE_URL), where reserved characters would need escaping
}

resource "aws_secretsmanager_secret" "gateway" {
  name = "${var.name}-secrets"
}

resource "aws_secretsmanager_secret_version" "gateway" {
  secret_id = aws_secretsmanager_secret.gateway.id
  secret_string = jsonencode({
    MasterKey       = "sk-${random_password.master_key.result}" # LiteLLM requires the sk- prefix
    SaltKey         = random_password.salt_key.result
    DbPassword      = random_password.db.result
    DatabaseUrl     = "postgresql://litellm:${random_password.db.result}@${var.rds_address}:5432/litellm?sslmode=require&connection_limit=5&pool_timeout=60"
    AnthropicApiKey = var.anthropic_api_key
    OpenAiApiKey    = var.openai_api_key
  })
}

# --- IAM -----------------------------------------------------------------------------------
data "aws_iam_policy_document" "ecs_tasks_assume" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }
  }
}

# Execution role of the long-running gateway: pull the image, write logs, read ITS secret only.
resource "aws_iam_role" "execution" {
  name               = "${var.name}-execution-role"
  assume_role_policy = data.aws_iam_policy_document.ecs_tasks_assume.json
}

resource "aws_iam_role_policy_attachment" "execution" {
  role       = aws_iam_role.execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

resource "aws_iam_role_policy" "execution_secrets" {
  name = "${var.name}-execution-secrets"
  role = aws_iam_role.execution.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = "secretsmanager:GetSecretValue"
      Resource = aws_secretsmanager_secret.gateway.arn
    }]
  })
}

# Separate role for the one-off jobs on purpose: only it may read the RDS master credentials
# and the app key, and the gateway's own role must not be able to.
resource "aws_iam_role" "jobs_execution" {
  name               = "${var.name}-jobs-execution-role"
  assume_role_policy = data.aws_iam_policy_document.ecs_tasks_assume.json
}

resource "aws_iam_role_policy_attachment" "jobs_execution" {
  role       = aws_iam_role.jobs_execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

resource "aws_iam_role_policy" "jobs_execution_secrets" {
  name = "${var.name}-jobs-execution-secrets"
  role = aws_iam_role.jobs_execution.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = "secretsmanager:GetSecretValue"
      Resource = [var.master_secret_arn, aws_secretsmanager_secret.gateway.arn, var.app_key_secret_arn]
    }]
  })
}

# Runtime AWS permissions of the containers themselves: none needed.
resource "aws_iam_role" "task" {
  name               = "${var.name}-task-role"
  assume_role_policy = data.aws_iam_policy_document.ecs_tasks_assume.json
}

# --- Service discovery ---------------------------------------------------------------------
# Gives the gateway a stable name inside the VPC (litellm.ai.internal) although the task's IP
# changes on every deploy. The namespace creates a private Route 53 zone (~$0.50/month).
resource "aws_service_discovery_private_dns_namespace" "this" {
  name = "ai.internal"
  vpc  = var.vpc_id
}

resource "aws_service_discovery_service" "litellm" {
  name = "litellm"
  dns_config {
    namespace_id   = aws_service_discovery_private_dns_namespace.this.id
    routing_policy = "MULTIVALUE"
    dns_records {
      type = "A"
      ttl  = 10 # short: a replaced task must be found quickly
    }
  }
  health_check_custom_config {
    failure_threshold = 1
  }
}

# --- The gateway service -------------------------------------------------------------------
resource "aws_ecs_task_definition" "litellm" {
  family                   = var.name
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = "512"  # 0.25 vCPU / 0.5 GB is too small for LiteLLM
  memory                   = "1024"
  execution_role_arn       = aws_iam_role.execution.arn
  task_role_arn            = aws_iam_role.task.arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = var.cpu_architecture
  }

  container_definitions = jsonencode([{
    name         = "litellm"
    image        = "${aws_ecr_repository.litellm.repository_url}:latest"
    essential    = true
    portMappings = [{ containerPort = 4000 }]
    secrets = [
      { name = "LITELLM_MASTER_KEY", valueFrom = "${aws_secretsmanager_secret.gateway.arn}:MasterKey::" },
      { name = "LITELLM_SALT_KEY", valueFrom = "${aws_secretsmanager_secret.gateway.arn}:SaltKey::" },
      { name = "DATABASE_URL", valueFrom = "${aws_secretsmanager_secret.gateway.arn}:DatabaseUrl::" },
      { name = "ANTHROPIC_API_KEY", valueFrom = "${aws_secretsmanager_secret.gateway.arn}:AnthropicApiKey::" },
      { name = "OPENAI_API_KEY", valueFrom = "${aws_secretsmanager_secret.gateway.arn}:OpenAiApiKey::" },
    ]
    # Same probe as docker-compose.yml. startPeriod is long because the first start runs
    # LiteLLM's database migrations.
    healthCheck = {
      command     = ["CMD", "python", "-c", "import urllib.request; urllib.request.urlopen('http://localhost:4000/health/liveliness')"]
      interval    = 15
      timeout     = 5
      retries     = 5
      startPeriod = 120
    }
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        "awslogs-group"         = aws_cloudwatch_log_group.litellm.name
        "awslogs-region"        = var.region
        "awslogs-stream-prefix" = "litellm"
      }
    }
  }])

  # ai-gateway-deploy.yml registers a new revision with the freshly built image on every deploy;
  # `:latest` here only bootstraps the very first start. Same reasoning as the API task definition.
  lifecycle {
    ignore_changes = [container_definitions]
  }
}

resource "aws_ecs_service" "litellm" {
  name            = var.name
  cluster         = aws_ecs_cluster.this.id
  task_definition = aws_ecs_task_definition.litellm.arn
  desired_count   = var.desired_count
  launch_type     = "FARGATE"

  network_configuration {
    subnets          = var.subnet_ids
    security_groups  = [var.gateway_security_group_id]
    assign_public_ip = true # no NAT gateway: needed to reach Anthropic/OpenAI
  }

  service_registries {
    registry_arn = aws_service_discovery_service.litellm.arn
  }

  # The deploy workflow moves the service to the revision it just registered. Without this,
  # the next `terraform apply` would put it back on the bootstrap revision (`:latest`).
  lifecycle {
    ignore_changes = [task_definition]
  }
}

# --- One-off jobs --------------------------------------------------------------------------
# Started by hand with an admin's own AWS credentials, in the API's security group (the one RDS
# and the gateway already accept). The deploy role is deliberately not allowed to run them.
resource "aws_cloudwatch_log_group" "jobs" {
  name              = "/ecs/${var.name}-jobs"
  retention_in_days = 14
}

# Creates the `litellm` role and database on the shared RDS instance. Run once before the
# first start of the service, and again to rotate the password.
resource "aws_ecs_task_definition" "db_bootstrap" {
  family                   = "${var.name}-db-bootstrap"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = "256"
  memory                   = "512"
  execution_role_arn       = aws_iam_role.jobs_execution.arn
  task_role_arn            = aws_iam_role.task.arn

  container_definitions = jsonencode([{
    name       = "bootstrap"
    image      = "public.ecr.aws/docker/library/postgres:18-alpine" # only for psql; the mirror avoids Docker Hub's pull limit
    essential  = true
    entryPoint = ["sh", "-c"]
    command    = [file("${path.module}/../../../docker/litellm/init/01-create-litellm-db.sh")]
    environment = [
      { name = "PGHOST", value = var.rds_address },
      { name = "PGSSLMODE", value = "require" },
    ]
    secrets = [
      { name = "POSTGRES_USER", valueFrom = "${var.master_secret_arn}:username::" },
      { name = "PGPASSWORD", valueFrom = "${var.master_secret_arn}:password::" },
      { name = "LITELLM_DB_PASSWORD", valueFrom = "${aws_secretsmanager_secret.gateway.arn}:DbPassword::" },
    ]
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        "awslogs-group"         = aws_cloudwatch_log_group.jobs.name
        "awslogs-region"        = var.region
        "awslogs-stream-prefix" = "db-bootstrap"
      }
    }
  }])
}

# Registers the form-ai-app key (models, monthly budget) with the running gateway by running the
# same docker/litellm/provision-app-key.sh used locally. Idempotent: re-run to change the budget.
resource "aws_ecs_task_definition" "provision_app_key" {
  family                   = "${var.name}-provision-app-key"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = "256"
  memory                   = "512"
  execution_role_arn       = aws_iam_role.jobs_execution.arn
  task_role_arn            = aws_iam_role.task.arn

  container_definitions = jsonencode([{
    name       = "provision"
    image      = "public.ecr.aws/docker/library/alpine:3"
    essential  = true
    entryPoint = ["sh", "-c"]
    # The script needs bash, curl and python3; the script text arrives as an env var and is
    # written to a file so it runs unchanged.
    command = ["apk add --no-cache bash curl python3 >/dev/null && printf '%s\\n' \"$PROVISION_SCRIPT\" > /provision.sh && bash /provision.sh"]
    environment = [
      { name = "PROVISION_SCRIPT", value = file("${path.module}/../../../docker/litellm/provision-app-key.sh") },
      { name = "LITELLM_URL", value = "http://litellm.ai.internal:4000" },
      { name = "LITELLM_APP_MAX_BUDGET_USD", value = var.app_key_max_budget_usd },
    ]
    secrets = [
      { name = "LITELLM_MASTER_KEY", valueFrom = "${aws_secretsmanager_secret.gateway.arn}:MasterKey::" },
      { name = "LITELLM_APP_KEY", valueFrom = var.app_key_secret_arn },
    ]
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        "awslogs-group"         = aws_cloudwatch_log_group.jobs.name
        "awslogs-region"        = var.region
        "awslogs-stream-prefix" = "provision"
      }
    }
  }])
}
```

**`outputs.tf`**

```hcl
output "gateway_url" {
  description = "Base URL the apps use. prod's ai_gateway_url must equal this."
  value       = "http://litellm.ai.internal:4000"
}
output "ecr_repository_url" { value = aws_ecr_repository.litellm.repository_url }
output "ecr_repository_arn" { value = aws_ecr_repository.litellm.arn }
output "cluster_name" { value = aws_ecs_cluster.this.name }
output "service_name" { value = aws_ecs_service.litellm.name }
output "service_id" { value = aws_ecs_service.litellm.id }
output "execution_role_arn" { value = aws_iam_role.execution.arn }
output "task_role_arn" { value = aws_iam_role.task.arn }
output "db_bootstrap_task_definition_family" { value = aws_ecs_task_definition.db_bootstrap.family }
output "provision_task_definition_family" { value = aws_ecs_task_definition.provision_app_key.family }
```

Why `ignore_changes = [task_definition]` on the service: without it, every later `terraform apply` would roll the gateway back to the bootstrap revision, whose image tag (`latest`) is not what the workflow deploys.

## 1.5 New root `infra/envs/ai-gateway/`

**`backend.tf`** (own state key: this is what makes it separable)

```hcl
terraform {
  backend "s3" {
    bucket       = "formai-terraform-state"
    key          = "ai-gateway/terraform.tfstate"
    region       = "us-east-1"
    encrypt      = true
    use_lockfile = true
  }
  required_providers {
    aws    = { source = "hashicorp/aws", version = "~> 5.0" }
    random = { source = "hashicorp/random", version = "~> 3.0" }
  }
}
```

**`variables.tf`**

```hcl
variable "region" {
  type    = string
  default = "us-east-1"
}
variable "name" {
  description = "Resource name prefix. Must start with formai- (the Terraform CI role may only manage formai-* IAM roles)."
  type        = string
  default     = "formai-ai-gateway"
}
variable "anthropic_api_key" {
  description = "Pass as TF_VAR_anthropic_api_key, never commit it."
  type        = string
  sensitive   = true
}
variable "openai_api_key" {
  description = "Pass as TF_VAR_openai_api_key, never commit it."
  type        = string
  sensitive   = true
}
variable "desired_count" {
  description = "0 for the first apply (no image yet), then 1."
  type        = number
  default     = 1
}
```

**`main.tf`**

```hcl
provider "aws" {
  region = var.region
}

# Read-only view of what prod owns. The only coupling between the two roots; when the gateway
# moves to another repo, replace these lookups with plain variables.
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
  client_security_group_id   = data.terraform_remote_state.prod.outputs.ecs_security_group_id
  rds_address               = data.terraform_remote_state.prod.outputs.rds_address
  master_secret_arn         = data.terraform_remote_state.prod.outputs.master_secret_arn
  app_key_secret_arn        = data.terraform_remote_state.prod.outputs.ai_app_key_secret_arn
  anthropic_api_key         = var.anthropic_api_key
  openai_api_key            = var.openai_api_key
  desired_count             = var.desired_count
}
```

**`outputs.tf`**

```hcl
output "gateway_url" { value = module.ai_gateway.gateway_url }
output "ecr_repository_url" { value = module.ai_gateway.ecr_repository_url }

output "db_bootstrap_command" {
  description = "Run once before the first start of the service: creates the litellm role and database on the shared RDS."
  value       = <<-EOT
    aws ecs run-task --cluster ${module.ai_gateway.cluster_name} --launch-type FARGATE \
      --task-definition ${module.ai_gateway.db_bootstrap_task_definition_family} \
      --network-configuration "awsvpcConfiguration={subnets=[${join(",", data.terraform_remote_state.prod.outputs.public_subnet_ids)}],securityGroups=[${data.terraform_remote_state.prod.outputs.ecs_security_group_id}],assignPublicIp=ENABLED}"
  EOT
}

output "provision_app_key_command" {
  description = "Run once the gateway is healthy: registers the form-ai-app key and its monthly budget."
  value       = <<-EOT
    aws ecs run-task --cluster ${module.ai_gateway.cluster_name} --launch-type FARGATE \
      --task-definition ${module.ai_gateway.provision_task_definition_family} \
      --network-configuration "awsvpcConfiguration={subnets=[${join(",", data.terraform_remote_state.prod.outputs.public_subnet_ids)}],securityGroups=[${data.terraform_remote_state.prod.outputs.ecs_security_group_id}],assignPublicIp=ENABLED}"
  EOT
}
```

**`github-oidc.tf`** (deploy role for the gateway workflow; same trust pattern as `envs/prod/github-oidc.tf`)

```hcl
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
```

## 1.6 Changes in `infra/modules/networking`

Two new security-group pieces: the gateway's SG, and RDS accepting 5432 from it. They live here (not in the gateway root) because `aws_security_group.data` declares its rules **inline**; a rule added from another root would be deleted by the next `prod` apply.

In `main.tf`, add the gateway SG and one more ingress block inside `aws_security_group.data`:

```hcl
resource "aws_security_group" "ai_gateway" {
  name   = "${var.name}-ai-gateway-sg"
  vpc_id = aws_vpc.this.id

  # Only the API's tasks (and the one-off jobs, which run in the same SG) may call the gateway.
  ingress {
    from_port       = 4000
    to_port         = 4000
    protocol        = "tcp"
    security_groups = [aws_security_group.ecs_tasks.id]
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"] # Anthropic, OpenAI, RDS
  }
}
```

```hcl
  # inside aws_security_group.data, next to the existing 5432 rule
  ingress {
    from_port       = 5432
    to_port         = 5432
    protocol        = "tcp"
    security_groups = [aws_security_group.ai_gateway.id]
  }
```

In `outputs.tf`:

```hcl
output "ai_gateway_security_group_id" {
  value = aws_security_group.ai_gateway.id
}
```

## 1.7 Changes in `infra/modules/data`

Remove the Claude key and add the app key's own secret.

- `variables.tf`: delete `claude_api_key`.
- `main.tf`: delete the `ClaudeApiKey = var.claude_api_key` line from `aws_secretsmanager_secret_version.app_secrets`, and add:

```hcl
# The key the API uses against the AI gateway (LiteLLM alias form-ai-app). Generated here because
# the *consumer* owns its credential; the gateway only registers it (ai-gateway root, provision
# task). A secret of its own (not a key in app_secrets) so the gateway's one-off job can read
# this value without being able to read the JWT secret or the DB connection string.
resource "random_password" "ai_app_key" {
  length  = 48
  special = false
}

resource "aws_secretsmanager_secret" "ai_app_key" {
  name = "${var.name}-ai-app-key"
}

resource "aws_secretsmanager_secret_version" "ai_app_key" {
  secret_id     = aws_secretsmanager_secret.ai_app_key.id
  secret_string = "sk-${random_password.ai_app_key.result}" # LiteLLM requires the sk- prefix
}
```

- `outputs.tf`:

```hcl
output "ai_app_key_secret_arn" {
  value = aws_secretsmanager_secret.ai_app_key.arn
}
```

## 1.8 Changes in `infra/modules/container-platform`

- `variables.tf`: add

```hcl
variable "ai_gateway_url" {
  description = "Base URL of the AI gateway (ai-gateway root output gateway_url)."
  type        = string
}

variable "ai_app_key_secret_arn" {
  description = "ARN of the secret holding the gateway app key (from the data module)."
  type        = string
}
```

Also fix the `app_secrets_arn` description (it still mentions `ClaudeApiKey`).

- `main.tf`, task definition `api`:
  - in `environment`, add `{ name = "Ai__GatewayUrl", value = var.ai_gateway_url }`. Aliases and the 80 s timeout already default correctly in `appsettings.json`.
  - in `secrets`, **replace** the `Claude__ApiKey` line with `{ name = "Ai__ApiKey", valueFrom = var.ai_app_key_secret_arn }` (a plain-string secret is referenced by its bare ARN, no `:Key::` suffix).
- `aws_iam_role_policy.ecs_execution_secrets`: `Resource = [var.app_secrets_arn, var.ai_app_key_secret_arn]`.
- `aws_lb.this`: add `idle_timeout = 120`. The generation request can last up to 90 s and the ALB default is 60 s, so without this the user gets a 504 from a perfectly healthy generation. This closes the "Load balancer idle timeout: not set yet" row in `docker/litellm/README.md`.

Why this is safe to do only now: the API task definition has `ignore_changes = [container_definitions]`, so on an existing deployment these env/secret edits would never be applied. Because nothing exists, the first apply creates the definition with the right values.

## 1.9 Changes in `infra/envs/prod`

- `variables.tf`: delete `claude_api_key`; add

```hcl
variable "ai_gateway_url" {
  description = "Where the AI gateway answers inside the VPC. Must equal the ai-gateway root's gateway_url output."
  type        = string
  default     = "http://litellm.ai.internal:4000"
}
```

- `main.tf`: in `module "data"` delete `claude_api_key = var.claude_api_key`; in `module "container_platform"` add

```hcl
  ai_gateway_url        = var.ai_gateway_url
  ai_app_key_secret_arn = module.data.ai_app_key_secret_arn
```

Prod does **not** depend on the gateway root: the URL is a name that will resolve once the gateway registers.

- `outputs.tf`: the gateway root reads these through remote state (`vpc_id` and `rds_address` already exist):

```hcl
output "public_subnet_ids" {
  value = module.networking.public_subnet_ids
}
output "ecs_security_group_id" {
  value = module.networking.ecs_security_group_id
}
output "ai_gateway_security_group_id" {
  value = module.networking.ai_gateway_security_group_id
}
output "master_secret_arn" {
  description = "RDS-managed master credentials secret (read by the gateway root's one-off DB bootstrap task)."
  value       = module.data.master_secret_arn
}
output "ai_app_key_secret_arn" {
  value = module.data.ai_app_key_secret_arn
}
```

## 1.10 New workflow `.github/workflows/ai-gateway-deploy.yml`

Independent from `deploy.yml`, manual like it. Builds for ARM (change `--platform` if you chose X86_64).

```yaml
name: AI gateway deploy

on:
  workflow_dispatch:

permissions:
  id-token: write
  contents: read

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - uses: aws-actions/configure-aws-credentials@v6
        with:
          role-to-assume: arn:aws:iam::058264176602:role/formai-github-actions-ai-gateway-deploy
          aws-region: us-east-1
      - uses: aws-actions/amazon-ecr-login@v2
        id: ecr
      - uses: docker/setup-buildx-action@v3
      - name: Build and push the gateway image
        run: |
          IMAGE="${{ steps.ecr.outputs.registry }}/formai-ai-gateway-litellm:${{ github.sha }}"
          docker buildx build --platform linux/arm64 --push -f docker/litellm/Dockerfile -t "$IMAGE" docker/litellm
      - run: aws ecs describe-task-definition --task-definition formai-ai-gateway --query taskDefinition > task-definition.json
      - uses: aws-actions/amazon-ecs-render-task-definition@v1
        id: render
        with:
          task-definition: task-definition.json
          container-name: litellm
          image: ${{ steps.ecr.outputs.registry }}/formai-ai-gateway-litellm:${{ github.sha }}
      - uses: aws-actions/amazon-ecs-deploy-task-definition@v2
        with:
          task-definition: ${{ steps.render.outputs.task-definition }}
          service: formai-ai-gateway
          cluster: formai-ai-gateway-cluster
          wait-for-service-stability: true
```

The names (`formai-ai-gateway-litellm`, `formai-ai-gateway`, `formai-ai-gateway-cluster`) come from the root's default `name`.

## 1.11 Changes in `.github/workflows/infra.yml`

1. In both existing jobs delete `TF_VAR_claude_api_key: ${{ secrets.CLAUDE_API_KEY }}`.
2. Add two jobs for the second root. The apply job must wait for the prod apply (it reads prod's state):

```yaml
  plan-ai-gateway:
    if: github.event_name == 'pull_request'
    runs-on: ubuntu-latest
    defaults: { run: { working-directory: infra/envs/ai-gateway } }
    env:
      TF_VAR_anthropic_api_key: ${{ secrets.ANTHROPIC_API_KEY }}
      TF_VAR_openai_api_key: ${{ secrets.OPENAI_API_KEY }}
    steps:
      # same steps as `plan`, with working-directory above and the comment text "Terraform Plan (ai-gateway)"

  apply-ai-gateway:
    if: github.event_name == 'push'
    needs: apply
    runs-on: ubuntu-latest
    environment: production
    defaults: { run: { working-directory: infra/envs/ai-gateway } }
    env:
      TF_VAR_anthropic_api_key: ${{ secrets.ANTHROPIC_API_KEY }}
      TF_VAR_openai_api_key: ${{ secrets.OPENAI_API_KEY }}
    steps:
      # same steps as `apply`
```

Also fix the plan comment path (`infra/envs/prod/plan.txt`) to be per job when you copy it.

3. GitHub repo secrets: add `ANTHROPIC_API_KEY`, `OPENAI_API_KEY`; remove `CLAUDE_API_KEY`.

Caveat: until `prod` has been applied once, `plan-ai-gateway` on a PR fails (remote state is empty). That is expected on the very first PR only.

## 1.12 Documentation (same change, per CLAUDE.md "Keeping the docs true")

- `docker/litellm/README.md`: add an "AWS" section (URL `http://litellm.ai.internal:4000`, where keys live, how to deploy and re-provision); set the "Load balancer idle timeout" row to *120 s, set in Terraform*; keep the "prompts are not stored" sentence (now true).
- `CLAUDE.md` "Local infrastructure": one sentence that production runs the gateway on Fargate with its own database on the shared RDS (`infra/envs/ai-gateway`).
- `docs/known-gaps.md`: add the gaps this design accepts: single gateway task (no HA, a replacement takes ~1–2 min and generation returns 503 meanwhile); plain HTTP between API and gateway inside the VPC; gateway in a public subnet with a public IP (no NAT); shares CPU/memory of a `db.t4g.micro` with the app; spend-log retention is not limited.
- **ADR `docs/adr/0010-ai-gateway-on-fargate-with-shared-rds.md`**: a paragraph or two. Decision: Fargate ARM + separate database/role on the existing RDS + own Terraform root. Alternatives rejected: dedicated RDS (+$15/month, kept as the later escape hatch via `pg_dump`), EC2 with Postgres (ops burden for ~$2/month saving), sidecar in the API task (couples the gateway to the API and cannot be shared), Fargate Spot (reclaims cause 503s). Consequence: the shared micro instance carries both databases.

---

# Part 2: Runbook (run in this order)

Use Git Bash (the commands use `\` continuations). All Terraform commands need `AWS_PROFILE`/credentials of an admin. Do not skip the `-var desired_count=0` in step 3.

**0. Local checks**
- `terraform fmt -recursive infra` and `terraform validate` in `envs/prod` and `envs/ai-gateway` (the latter needs `terraform init` first; validate does not read remote state).
- `docker buildx imagetools inspect ghcr.io/berriai/litellm:v1.103.1` shows `linux/arm64` (see 1.2).
- Add the GitHub secrets from 1.11.

**1. Apply `prod`** (creates networking incl. the gateway SG, RDS, Redis, the API, the app key secret)
```bash
cd infra/envs/prod
export TF_VAR_jwt_secret=... TF_VAR_ses_smtp_username=... TF_VAR_ses_smtp_password=...
terraform init && terraform plan -out tfplan && terraform apply tfplan
```
If it fails with *"secret ... is scheduled for deletion"*, your earlier `destroy` left Secrets Manager's 30-day recovery window running. Remove the stale secret: `aws secretsmanager delete-secret --secret-id <name> --force-delete-without-recovery`, then re-apply.

**2. Bootstrap the app database roles** with the existing `terraform output db_bootstrap_command` (as before). This is unchanged and independent of the gateway.

**3. Apply the gateway root with zero tasks** (no image exists yet, so nothing may try to start)
```bash
cd ../ai-gateway
export TF_VAR_anthropic_api_key=... TF_VAR_openai_api_key=...
terraform init
terraform apply -var desired_count=0
```

**4. Create the `litellm` database and role.** Run `terraform output -raw db_bootstrap_command` and execute it. Then read the log (`aws logs tail /ecs/formai-ai-gateway-jobs --since 10m`) and check the task's exit code is 0 (`aws ecs describe-tasks ... --query 'tasks[0].containers[0].exitCode'`). Expect no errors; the script is quiet on success.

**5. Push the first image by hand** (the repo is `IMMUTABLE`; the first push of `latest` works because nothing owns that tag yet, and every later deploy uses the workflow's git-SHA tags)
```bash
REPO=$(terraform output -raw ecr_repository_url)
aws ecr get-login-password --region us-east-1 | docker login --username AWS --password-stdin "${REPO%%/*}"
docker buildx build --platform linux/arm64 --push -f ../../../docker/litellm/Dockerfile -t "$REPO:latest" ../../../docker/litellm
```

**6. Start the gateway**
```bash
terraform apply -var desired_count=1
```
Watch it come up: `aws ecs describe-services --cluster formai-ai-gateway-cluster --services formai-ai-gateway --query 'services[0].events[:5]'` and `aws logs tail /ecs/formai-ai-gateway --follow`. The first start applies LiteLLM's migrations to the new database (1–2 minutes) and ends with a line like `Uvicorn running on http://0.0.0.0:4000`. If it restarts in a loop, the log says why (usually `DATABASE_URL`: see Gotchas).

**7. Register the app key**: `terraform output -raw provision_app_key_command`, run it, then read `/ecs/formai-ai-gateway-jobs`. Success prints `app key created (alias form-ai-app, budget 10 USD per month)`. This one task also proves the whole chain: Cloud Map name resolution, the SG rule, the master key and the database.

**8. Deploy the API** with the normal `deploy.yml`. From now on gateway changes go through `ai-gateway-deploy.yml`.

**9. Merge** the branch; from then on `infra.yml` applies both roots (`prod`, then `ai-gateway`).

## Verification

- `terraform plan` shows no changes in both roots right after step 9 (this catches the `ignore_changes` mistakes).
- The gateway is **not** reachable from outside: get the task's public IP (`aws ecs describe-tasks` → ENI → public IP) and `curl -m 5 http://<ip>:4000/health/liveliness` from your machine must time out.
- Inside the VPC it answers: step 7 succeeding is the proof; for a direct check run a one-off task in the API SG with `curl http://litellm.ai.internal:4000/health/liveliness`.
- End to end from the deployed frontend: generate a form from pasted text, from a `.pdf`, and a graded one. Then look at the spend log (via the gateway's admin routes with the master key from Secrets Manager) to see the cost rows with **no prompt text**.
- Failover: temporarily blank `AnthropicApiKey` in the secret, redeploy the service (`aws ecs update-service --force-new-deployment`), generate a form: it should still work through `gpt-5.2`. Restore the key after.
- After a week check Cost Explorer against the ~$18–20 estimate.

## Gotchas

- **`LITELLM_SALT_KEY` must never change** once the database holds data (it encrypts stored credentials). It is a Terraform-generated value in state; do not `terraform taint` it.
- **`DATABASE_URL` and TLS.** RDS PG 15+ forces TLS, hence `sslmode=require`. If Prisma complains about the certificate, try `sslmode=prefer` in the secret. `connection_limit=5` keeps the gateway to a few connections on a small instance (the API uses its own).
- **Env/secret edits on a running task definition do nothing** (`ignore_changes = [container_definitions]`, for the API and the gateway alike); only the image changes through the workflows. To change a gateway environment variable or secret reference later, remove `ignore_changes` for one apply or register a revision by hand.
- **The API service has the same revert risk** the gateway service avoids: `aws_ecs_service.api` does not ignore `task_definition`, so a later `terraform apply` can move it back to Terraform's own revision. Worth fixing in `container-platform` (add `lifecycle { ignore_changes = [task_definition] }`), but out of scope here; check `terraform plan` before the first `infra.yml` apply after a deploy.
- **Rotating the gateway DB password**: re-run the bootstrap task (it re-syncs the role password from the secret), then redeploy the service so it picks up the secret.
- **`provision-app-key.sh` is idempotent**: run it again after changing `app_key_max_budget_usd` (via `terraform apply`) to update the budget.
- **First start is slow and `desired_count = 0` is not an outage**: that is why step 3 uses it. Never apply with `desired_count=1` before step 5, or ECS retries an image pull that cannot succeed.

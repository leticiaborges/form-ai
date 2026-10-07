resource "aws_ecr_repository" "litellm" {
  name                 = "${var.name}-litellm"
  image_tag_mutability = "IMMUTABLE"
  image_scanning_configuration {
    scan_on_push = true
  }
}

resource "aws_ecs_cluster" "this" {
  name = "${var.name}-cluster"
}

resource "aws_cloudwatch_log_group" "litellm" {
  name              = "/ecs/${var.name}"
  retention_in_days = 14
}

# ---- Secrets ----

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
  special = false
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

# ---- IAM ----
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

resource "aws_iam_role" "task" {
  name               = "${var.name}-task-role"
  assume_role_policy = data.aws_iam_policy_document.ecs_tasks_assume.json
}

# --- Service discovery ----
# Gives the gateway a stable name inside the VPC (litellm.ai.internal) although the task's IP
# changes on every deploy. The namespace creates a private Route 53 zone.
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
      ttl  = 10
    }
  }
}

# --- The gateway service ----
resource "aws_ecs_task_definition" "litellm" {
  family                   = var.name
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = "512"
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

  lifecycle {
    ignore_changes = [task_definition]
  }
}

# --- One-off jobs ---
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
    image      = "public.ecr.aws/docker/library/postgres:18-alpine"
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

resource "aws_ecr_repository" "api" {
  name                 = "${var.name}-api"
  image_tag_mutability = "IMMUTABLE" # a given tag (e.g. a git SHA) can never be overwritten — real auditability
  image_scanning_configuration { scan_on_push = true }
}

resource "aws_ecs_cluster" "this" {
  name = "${var.name}-cluster"
}

resource "aws_cloudwatch_log_group" "api" {
  name              = "/ecs/${var.name}-api"
  retention_in_days = 14
}

# Execution role: what ECS itself uses to start the container — pull the
# image, write logs, fetch the secrets below. Not the app's own permissions.
resource "aws_iam_role" "ecs_execution" {
  name = "${var.name}-ecs-execution-role"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "ecs-tasks.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
}

resource "aws_iam_role_policy_attachment" "ecs_execution" {
  role       = aws_iam_role.ecs_execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

resource "aws_iam_role_policy" "ecs_execution_secrets" {
  name = "${var.name}-ecs-execution-secrets"
  role = aws_iam_role.ecs_execution.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = "secretsmanager:GetSecretValue"
      Resource = [var.app_secrets_arn, var.ai_app_key_secret_arn]
    }]
  })
}

# Task role: what the app's own code would use to call other AWS services at
# runtime. Nothing needs it yet — left minimal on purpose (Phase 7).
resource "aws_iam_role" "ecs_task" {
  name = "${var.name}-ecs-task-role"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "ecs-tasks.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
}

resource "aws_ecs_task_definition" "api" {
  family                   = "${var.name}-api"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = "256"                          # 0.25 vCPU
  memory                   = "512"                          # 0.5 GB — cheapest Fargate size, fine for a portfolio's traffic
  execution_role_arn       = aws_iam_role.ecs_execution.arn # pulls image, writes logs, reads secrets
  task_role_arn            = aws_iam_role.ecs_task.arn      # the app's own runtime AWS permissions (none needed yet)

  container_definitions = jsonencode([{
    name         = "api"
    image        = "${aws_ecr_repository.api.repository_url}:latest"
    portMappings = [{ containerPort = 8080 }]
    environment = [
      { name = "Jwt__Issuer", value = "formai" },
      { name = "Jwt__Audience", value = "formai" },
      { name = "Email__FrontendBaseUrl", value = var.frontend_base_url },
      { name = "Email__SmtpHost", value = var.ses_email_smtphost },
      { name = "Email__SmtpPort", value = tostring(var.ses_email_smtpport) },
      { name = "Email__FromAddress", value = var.ses_email_smtpfromaddress },
      { name = "Email__FromName", value = var.ses_email_smtpfromname },
      { name = "Ai__GatewayUrl", value = var.ai_gateway_url },
      # ElastiCache's endpoint is a Terraform-computed value, not a secret — no auth token is
      # configured on the cluster (same as your local docker-compose redis), so this is just a
      # hostname, safe as a plain environment variable rather than routed through Secrets Manager.
      { name = "ConnectionStrings__Redis", value = "${var.redis_address}:6379" }
      # ... other non-secret appsettings keys
    ]
    secrets = [
      { name = "ConnectionStrings__DefaultConnection", valueFrom = "${var.app_secrets_arn}:ConnectionString::" },
      { name = "Jwt__Secret", valueFrom = "${var.app_secrets_arn}:JwtSecret::" },
      { name = "Ai__ApiKey", valueFrom = var.ai_app_key_secret_arn },
      { name = "Email__Username", valueFrom = "${var.app_secrets_arn}:SesSmtpUsername::" },
      { name = "Email__Password", valueFrom = "${var.app_secrets_arn}:SesSmtpPassword::" },
      { name = "Demo__Password", valueFrom = "${var.app_secrets_arn}:DemoPassword::" }
    ]
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        "awslogs-group"         = aws_cloudwatch_log_group.api.name
        "awslogs-region"        = var.region
        "awslogs-stream-prefix" = "api"
      }
    }
  }])

  # deploy.yml registers a new revision with the freshly-built image on every
  # push (Terraform's `:latest` here is only the bootstrap value for the very
  # first apply, before any image has been pushed) — without this, the next
  # `terraform apply` would revert the running service back to that literal
  # `:latest`, which was never pushed and doesn't exist as a tag.
  lifecycle {
    ignore_changes = [container_definitions]
  }
}

resource "aws_ecs_service" "api" {
  name            = "${var.name}-api"
  cluster         = aws_ecs_cluster.this.id
  task_definition = aws_ecs_task_definition.api.arn
  desired_count   = 1
  launch_type     = "FARGATE"

  network_configuration {
    subnets          = var.public_subnet_ids # no NAT Gateway (Phase 5) — tasks need a public IP for outbound
    security_groups  = [var.ecs_security_group_id]
    assign_public_ip = true
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.api.arn
    container_name   = "api"
    container_port   = 8080
  }

  # A target group only becomes "associated with a load balancer" once a
  # listener forwards to it — ECS's CreateService call checks for that and
  # rejects the service otherwise. Terraform can't infer this ordering from
  # the arguments above (neither resource references the other), so it must
  # be forced explicitly, or a fast/parallel apply can schedule this before
  # the listener exists and fail the same way this one just did.
  depends_on = [aws_lb_listener.https]

  # deploy.yml (or a manual revision) decides which task definition revision
  # runs. Without this, every `terraform apply` moves the service back to the
  # revision in Terraform's state, which holds the unpushed `:latest` image.
  lifecycle {
    ignore_changes = [task_definition]
  }
}

resource "aws_lb" "this" {
  name               = "${var.name}-alb"
  load_balancer_type = "application"
  subnets            = var.public_subnet_ids
  security_groups    = [var.alb_security_group_id]
  idle_timeout       = 120
}

resource "aws_lb_target_group" "api" {
  name        = "${var.name}-api-tg"
  port        = 8080
  protocol    = "HTTP"
  vpc_id      = var.vpc_id
  target_type = "ip" # required for Fargate — tasks are IPs, not EC2 instances
  health_check {
    path                = "/health" # Swagger is dev-only (see Program.cs), so this is the only path that works in prod
    healthy_threshold   = 2
    unhealthy_threshold = 3
  }
}

resource "aws_lb_listener" "https" {
  load_balancer_arn = aws_lb.this.arn
  port              = 443
  protocol          = "HTTPS"
  ssl_policy        = "ELBSecurityPolicy-TLS13-1-2-2021-06"
  certificate_arn   = var.acm_certificate_arn # from Phase 8
  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.api.arn
  }
}

resource "aws_lb_listener" "http_redirect" {
  load_balancer_arn = aws_lb.this.arn
  port              = 80
  protocol          = "HTTP"
  default_action {
    type = "redirect"
    redirect {
      port        = "443"
      protocol    = "HTTPS"
      status_code = "HTTP_301"
    }
  }
}

# --- One-off database jobs: role bootstrap and migrations ---------------------------------
# RDS is in private subnets and only the ECS tasks' security group can reach it, so anything
# that needs SQL access (creating roles, applying migrations) runs as a one-off Fargate task
# started with `aws ecs run-task`, never from the GitHub runner and never at API startup.

resource "aws_ecr_repository" "migrator" {
  name                 = "${var.name}-migrator"
  image_tag_mutability = "IMMUTABLE"
  image_scanning_configuration { scan_on_push = true }
}

resource "aws_cloudwatch_log_group" "db_jobs" {
  name              = "/ecs/${var.name}-db-jobs"
  retention_in_days = 14
}

# Separate from ecs_execution on purpose: this one may read the master and migrator
# credentials, and the API's execution role must not be able to.
resource "aws_iam_role" "ecs_db_jobs_execution" {
  name = "${var.name}-ecs-db-jobs-execution-role"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "ecs-tasks.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
}

resource "aws_iam_role_policy_attachment" "ecs_db_jobs_execution" {
  role       = aws_iam_role.ecs_db_jobs_execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

resource "aws_iam_role_policy" "ecs_db_jobs_execution_secrets" {
  name = "${var.name}-ecs-db-jobs-execution-secrets"
  role = aws_iam_role.ecs_db_jobs_execution.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = "secretsmanager:GetSecretValue"
      Resource = [var.master_secret_arn, var.db_job_secrets_arn]
    }]
  })
}

# Run once after the database is created (and again to rotate the role passwords): connects as
# the RDS master user and runs the same script Docker Compose and CI use locally, so the roles
# and grants cannot drift between environments. Started by hand with the admin's own AWS
# credentials; the deploy role is deliberately not allowed to run it.
resource "aws_ecs_task_definition" "db_bootstrap" {
  family                   = "${var.name}-db-bootstrap"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = "256"
  memory                   = "512"
  execution_role_arn       = aws_iam_role.ecs_db_jobs_execution.arn
  task_role_arn            = aws_iam_role.ecs_task.arn

  container_definitions = jsonencode([{
    name       = "bootstrap"
    image      = "public.ecr.aws/docker/library/postgres:18-alpine" # only for psql; the mirror avoids Docker Hub's anonymous pull limit
    essential  = true
    entryPoint = ["sh", "-c"]
    command    = [file("${path.module}/../../../docker/postgres/init/01-create-app-user.sh")]
    environment = [
      { name = "PGHOST", value = var.rds_address },
      { name = "PGSSLMODE", value = "require" },
      { name = "POSTGRES_DB", value = var.db_name },
    ]
    secrets = [
      { name = "POSTGRES_USER", valueFrom = "${var.master_secret_arn}:username::" },
      { name = "PGPASSWORD", valueFrom = "${var.master_secret_arn}:password::" },
      { name = "MIGRATOR_DB_PASSWORD", valueFrom = "${var.db_job_secrets_arn}:MigratorPassword::" },
      { name = "APP_DB_PASSWORD", valueFrom = "${var.db_job_secrets_arn}:AppPassword::" },
    ]
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        "awslogs-group"         = aws_cloudwatch_log_group.db_jobs.name
        "awslogs-region"        = var.region
        "awslogs-stream-prefix" = "bootstrap"
      }
    }
  }])
}

# Run by deploy.yml before each API rollout, as form_ai_migrator. The image is an EF Core
# migrations bundle built by the pipeline; it reads its connection string from
# MIGRATOR_CONNECTION. Same bootstrap-value reasoning as the API task definition above.
resource "aws_ecs_task_definition" "db_migrate" {
  family                   = "${var.name}-db-migrate"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = "256"
  memory                   = "512"
  execution_role_arn       = aws_iam_role.ecs_db_jobs_execution.arn
  task_role_arn            = aws_iam_role.ecs_task.arn

  container_definitions = jsonencode([{
    name      = "migrate"
    image     = "${aws_ecr_repository.migrator.repository_url}:latest"
    essential = true
    secrets = [
      { name = "MIGRATOR_CONNECTION", valueFrom = "${var.db_job_secrets_arn}:MigratorConnectionString::" },
    ]
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        "awslogs-group"         = aws_cloudwatch_log_group.db_jobs.name
        "awslogs-region"        = var.region
        "awslogs-stream-prefix" = "migrate"
      }
    }
  }])

  lifecycle {
    ignore_changes = [container_definitions]
  }
}

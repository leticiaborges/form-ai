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
      Resource = var.app_secrets_arn
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
      # ElastiCache's endpoint is a Terraform-computed value, not a secret — no auth token is
      # configured on the cluster (same as your local docker-compose redis), so this is just a
      # hostname, safe as a plain environment variable rather than routed through Secrets Manager.
      { name = "ConnectionStrings__Redis", value = "${var.redis_address}:6379" }
      # ... other non-secret appsettings keys
    ]
    secrets = [
      { name = "ConnectionStrings__DefaultConnection", valueFrom = "${var.app_secrets_arn}:ConnectionString::" },
      { name = "Jwt__Secret", valueFrom = "${var.app_secrets_arn}:JwtSecret::" },
      { name = "Claude__ApiKey", valueFrom = "${var.app_secrets_arn}:ClaudeApiKey::" },
      { name = "Email__Username", valueFrom = "${var.app_secrets_arn}:SesSmtpUsername::" },
      { name = "Email__Password", valueFrom = "${var.app_secrets_arn}:SesSmtpPassword::" }
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
}

resource "aws_lb" "this" {
  name               = "${var.name}-alb"
  load_balancer_type = "application"
  subnets            = var.public_subnet_ids
  security_groups    = [var.alb_security_group_id]
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

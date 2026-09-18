# Deploying FormAI to AWS — a hands-on guide

You do every step yourself — console clicks, AWS CLI commands, `terraform apply` — nothing here is a script that does it for you. Each phase explains *why*, not just *what*, so you can defend these choices in an interview.

**Scope for this pass:** the application code doesn't change. You're standing up infrastructure for what already exists: the API container, PostgreSQL, Redis (SignalR backplane), and the React frontend as static files. Follow the phases in order — each one depends on the last.

**Conventions used throughout:**
- Region: `us-east-1` (cheapest, most services available first, and required anyway for CloudFront's ACM cert — see Phase 10). Substitute your own if you prefer.
- Resource naming: `formai-prod-<thing>`, e.g. `formai-prod-vpc`, `formai-prod-api`. This is the "one environment, named like there could be more" convention real teams use.
- Placeholder domain: `yourdomain.com`. Swap in whatever you buy in Phase 2.

---

## Phase 0 — AWS account & IAM foundation

**Why this phase exists:** the root account login is a master key with no restrictions. Every real AWS org locks it away and does daily work through a named IAM identity with only the permissions it needs. Doing this first — before you've created a single resource — is itself the "least privilege" story you'll tell in an interview.

1. If you don't have an AWS account, create one at [aws.amazon.com](https://aws.amazon.com). You'll need a card on file even for free-tier usage.
2. **Enable MFA on the root user immediately** (IAM console → root user → Security credentials → Assign MFA device). Then stop using the root login for anything except billing and account-level settings.
3. Create an **IAM Identity Center** (formerly SSO) user for yourself, or — simpler for a solo project — an IAM user with MFA and a group granting `AdministratorAccess`, used only from your terminal/console, never for the CI pipeline (that gets its own scoped role in Phase 11; never give GitHub Actions your personal admin credentials).
4. Install and configure the AWS CLI:
   ```bash
   aws configure
   # AWS Access Key ID / Secret: from the IAM user you just made
   # Default region: us-east-1
   ```
5. Set a **budget alert** now, before anything costs money: Billing console → Budgets → create a budget, e.g. $15/month, with an email alert at 80%. This is a five-minute step that prevents a misconfigured NAT gateway or forgotten resource from becoming a surprise.

---

## Phase 1 — Buy the domain (Route 53)

1. Route 53 console → Registered domains → Register domain.
2. Search `yourdomain.com` (or `.dev`, `.app` — both are cheap and read well for a portfolio). Expect ~$12-14/yr for `.com`.
3. Complete registration. Route 53 **automatically creates a public hosted zone** for the domain — note its **Hosted Zone ID**, you'll need it in Terraform later (Phase 9).
4. Nothing else to do yet — you don't have anything to point it at until Phase 9.

---

## Phase 2 — Containerize the API

ECS Fargate runs container images, and there's no Dockerfile in the repo yet — this is the one piece of "application" work in an otherwise infra-only pass, because without it there's nothing to deploy.

Create `src/FormAI.API/Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore FormAI.sln
RUN dotnet publish src/FormAI.API/FormAI.API.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "FormAI.API.dll"]
```

Notes worth understanding, not just copying:
- Multi-stage build: the SDK image (large, has compilers) only exists to produce `/app`; the final image is the much smaller ASP.NET *runtime* image. This is what "enterprise" Docker images look like — nobody ships the SDK to production.
- Port 8080 is ASP.NET Core's default in-container listen port for .NET 8+ images (it auto-detects the container and binds there instead of 5000/5001). The ALB target group in Phase 8 points at this port.
- Build and run it locally against your existing Docker Compose Postgres/Redis to prove it works before touching AWS:
  ```bash
  docker build -f src/FormAI.API/Dockerfile -t formai-api:local .
  docker run -p 8080:8080 --env-file .env.local formai-api:local
  ```

Commit the Dockerfile. Don't build/push it yet — that happens through the pipeline in Phase 12, not by hand.

---

## Phase 3 — Terraform bootstrap: remote state

**Why:** Terraform tracks what it created in a *state file*. If that file lives on your laptop, nobody else (including CI) can safely run `terraform apply` — two applies from different machines would corrupt it. Every real team puts state in S3, with locking so two applies can't race each other. Set this up *before* writing any other Terraform, because every other module will reference this backend.

This is the one piece of infrastructure you create by hand instead of via Terraform (a well-known chicken-and-egg: Terraform can't manage the bucket it stores its own state in).

```bash
aws s3api create-bucket --bucket formai-terraform-state-<your-unique-suffix> \
  --region us-east-1
aws s3api put-bucket-versioning --bucket formai-terraform-state-<your-unique-suffix> \
  --versioning-configuration Status=Enabled
aws s3api put-bucket-encryption --bucket formai-terraform-state-<your-unique-suffix> \
  --server-side-encryption-configuration '{"Rules":[{"ApplyServerSideEncryptionByDefault":{"SSEAlgorithm":"AES256"}}]}'
```

Versioning means an `apply` that clobbers state is recoverable. Encryption is default-on now for S3 anyway, but declaring it explicitly is the kind of thing a reviewer of your Terraform will notice you did deliberately.

**Locking no longer needs a separate DynamoDB table.** Terraform 1.10+ added native S3 locking (`use_lockfile`, configured in Phase 4), using S3's own conditional-write support to place a lock object next to the state file — the DynamoDB-table approach older guides describe is now deprecated, not removed, but there's no reason to reach for it on a new setup.

---

## Phase 4 — Terraform repo layout ("enterprise" structure)

The pattern real teams use: **modules** describe *how* to build a piece of infrastructure (reusable, no hardcoded environment values); **envs** describe *what* to build for a specific environment (wires modules together with real values). You'll only stand up `prod`, but structuring it this way — instead of one giant `main.tf` — is the actual practice, and it's what makes "I used Terraform" a credible sentence rather than "I ran `terraform apply` once."

```
infra/
├── modules/
│   ├── networking/       # VPC, subnets, security groups
│   ├── data/              # RDS, ElastiCache, Secrets Manager
│   ├── container-platform/ # ECR, ECS cluster/service/task, ALB
│   ├── dns-tls/           # ACM cert, Route 53 records
│   └── frontend/          # S3 + CloudFront
└── envs/
    └── prod/
        ├── main.tf         # composes the modules
        ├── variables.tf
        ├── outputs.tf
        ├── backend.tf      # points at the S3 state bucket from Phase 3
        └── terraform.tfvars
```

`infra/envs/prod/backend.tf`:
```hcl
terraform {
  backend "s3" {
    bucket       = "formai-terraform-state-<your-unique-suffix>"
    key          = "prod/terraform.tfstate"
    region       = "us-east-1"
    encrypt      = true
    use_lockfile = true
  }
  required_providers {
    aws = { source = "hashicorp/aws", version = "~> 5.0" }
  }
}

provider "aws" {
  region = "us-east-1"
}
```

Run `terraform init` inside `infra/envs/prod` once this file exists — Terraform will confirm it's using the remote backend.

---

## Phase 5 — Networking module

**Why this shape:** a public/private subnet split across two Availability Zones is the baseline "this person understands AWS networking" signal. Public subnets hold the load balancer *and* the ECS tasks; RDS sits in private subnets with no direct internet route in at all.

**The one real cost/practice tradeoff here — decide deliberately, don't default silently:** a **NAT Gateway** is what normally lets private-subnet resources (like ECS tasks) reach the internet outbound — e.g., pulling from ECR, calling the Anthropic API. It costs ~$32/month plus data, by far the most expensive single thing in this whole build, and it doesn't scale down with how many hours a day you run things — it's billed hourly for existing, not for being used.

Given the $40/month cap and the 8-11pm-only schedule, **skip the NAT Gateway entirely.** Put the ECS tasks in the *public* subnet instead, with `assign_public_ip = true` so they can reach the internet directly for outbound calls. This is **not** a security downgrade: the subnet only controls whether a resource *can* have a public IP, not what can reach it — that's still entirely the security group's job. The `ecs_tasks` security group below still only allows *inbound* traffic on 8080 from the ALB's security group; nothing about being in a public subnet changes that. Document this tradeoff the same way: "I put ECS tasks in a public subnet with a locked-down security group instead of paying for a NAT Gateway — the inbound path is identical, I just save $32/month on outbound routing for a single-instance portfolio deployment" is a genuinely good interview answer, not a shortcut to hide.

Key resources in `modules/networking/main.tf`:
```hcl
resource "aws_vpc" "this" {
  cidr_block           = var.vpc_cidr
  enable_dns_support   = true
  enable_dns_hostnames = true
  tags = { Name = "${var.name}-vpc" }
}

resource "aws_subnet" "public" {
  for_each                = var.public_subnets   # e.g. { a = "10.0.1.0/24", b = "10.0.2.0/24" }
  vpc_id                  = aws_vpc.this.id
  cidr_block               = each.value
  availability_zone        = "${var.region}${each.key}"
  map_public_ip_on_launch  = true
}

resource "aws_subnet" "private" {
  for_each          = var.private_subnets
  vpc_id            = aws_vpc.this.id
  cidr_block        = each.value
  availability_zone = "${var.region}${each.key}"
}

resource "aws_internet_gateway" "this" { vpc_id = aws_vpc.this.id }

# route table: public subnets (ALB + ECS tasks) -> igw.
# Private subnets (RDS only) get no internet route at all — RDS never
# initiates outbound connections, so it doesn't need one.
# (standard aws_route_table + aws_route_table_association pairs per subnet)

resource "aws_security_group" "alb" {
  name   = "${var.name}-alb-sg"
  vpc_id = aws_vpc.this.id
  ingress { from_port = 443; to_port = 443; protocol = "tcp"; cidr_blocks = ["0.0.0.0/0"] }
  ingress { from_port = 80;  to_port = 80;  protocol = "tcp"; cidr_blocks = ["0.0.0.0/0"] }
  egress  { from_port = 0;   to_port = 0;   protocol = "-1"; cidr_blocks = ["0.0.0.0/0"] }
}

resource "aws_security_group" "ecs_tasks" {
  name   = "${var.name}-ecs-sg"
  vpc_id = aws_vpc.this.id
  ingress { from_port = 8080; to_port = 8080; protocol = "tcp"; security_groups = [aws_security_group.alb.id] }
  egress  { from_port = 0;    to_port = 0;    protocol = "-1"; cidr_blocks = ["0.0.0.0/0"] }
}

resource "aws_security_group" "data" {
  name   = "${var.name}-data-sg"
  vpc_id = aws_vpc.this.id
  ingress { from_port = 5432; to_port = 5432; protocol = "tcp"; security_groups = [aws_security_group.ecs_tasks.id] }
  ingress { from_port = 6379; to_port = 6379; protocol = "tcp"; security_groups = [aws_security_group.ecs_tasks.id] }
  egress  { from_port = 0;    to_port = 0;    protocol = "-1"; cidr_blocks = ["0.0.0.0/0"] }
}
```

The chain of trust here is the whole point: internet → ALB security group (only 80/443) → ECS security group (only 8080, only *from* the ALB's security group, regardless of which subnet it's in) → data security group (only 5432/6379, only *from* the ECS security group). Nothing can reach the database or cache directly from the internet even by guessing the address, because the security group rule references the ALB/ECS *security group itself*, not an IP range. Note that RDS and ElastiCache (Phase 6) sit in the *private* subnets even though the NAT Gateway is gone — they never need outbound internet access in the first place, so removing the NAT Gateway (which only exists to grant that) never affected them.

---

## Phase 6 — Data layer: RDS, ElastiCache, Secrets Manager

**Worth being honest about the tradeoff here, even though it's deliberate:** ADR 0005's entire reason for a Redis backplane is fanning SignalR messages out across *multiple* API instances, and this deployment runs exactly one (`desired_count = 1` in Phase 7) — so ElastiCache isn't earning its keep functionally today; a Redis container running as a sidecar in the ECS task would do the same job for free. The reason to still stand up ElastiCache is deliberately non-technical: hands-on experience with a managed AWS service is worth the ~$12/month on its own, and unlike a sidecar, it's already correctly positioned for the day `desired_count` actually goes above 1. Unlike RDS, ElastiCache has no "stop" state — only create/delete — so this cost runs continuously and isn't part of the 8-11pm schedule (see the cost table at the end).

**Why Secrets Manager, not plain environment variables in the task definition:** a task definition is visible to anyone with ECS read access, including in the console and in `describe-task-definition` output. Storing the DB password there is the equivalent of a hardcoded connection string. Secrets Manager entries are pulled at container-start time and never appear in the task definition JSON itself — that's a real, checkable practice, not just a checkbox.

```hcl
resource "aws_db_instance" "postgres" {
  identifier             = "${var.name}-db"
  engine                 = "postgres"
  engine_version         = "18"
  instance_class         = "db.t4g.micro"
  allocated_storage      = 20
  db_name                = "form_ai"
  username               = "form_ai_app"
  manage_master_user_password = true   # RDS-managed secret, one less thing you generate/store yourself
  db_subnet_group_name   = aws_db_subnet_group.this.name
  vpc_security_group_ids = [var.data_security_group_id]
  publicly_accessible    = false
  skip_final_snapshot    = true         # fine for a portfolio project; a real prod DB would not set this
  backup_retention_period = 3
}

resource "aws_elasticache_cluster" "redis" {
  cluster_id           = "${var.name}-redis"
  engine               = "redis"
  node_type            = "cache.t4g.micro"
  num_cache_nodes      = 1
  subnet_group_name    = aws_elasticache_subnet_group.this.name   # same private subnets as RDS
  security_group_ids   = [var.data_security_group_id]
}

# RDS generated and owns the master password when manage_master_user_password = true —
# it lives in its own AWS-managed secret, not one you create. Read it back out here
# so it can be folded into one connection string your app actually understands.
data "aws_secretsmanager_secret_version" "rds_master" {
  secret_id = aws_db_instance.postgres.master_user_secret[0].secret_arn
}

resource "aws_secretsmanager_secret" "app_secrets" {
  name = "${var.name}-app-secrets"
}

resource "aws_secretsmanager_secret_version" "app_secrets" {
  secret_id = aws_secretsmanager_secret.app_secrets.id
  secret_string = jsonencode({
    ConnectionString = "Host=${aws_db_instance.postgres.address};Database=form_ai;Username=form_ai_app;Password=${jsondecode(data.aws_secretsmanager_secret_version.rds_master.secret_string)["password"]}"
    JwtSecret        = var.jwt_secret       # generate once with `openssl rand -base64 64`, pass as a TF_VAR, never commit it
    ClaudeApiKey     = var.claude_api_key
  })
}
```

`manage_master_user_password = true` is worth calling out specifically: it tells RDS to generate and rotate the master password itself, stored in its own Secrets Manager entry automatically, so you never type or store the DB password anywhere including your own `terraform.tfvars`. The `data` block above is the small extra step that costs you — reading that generated secret back out so it can be composed into the one connection string your app actually reads.

---

## Phase 7 — Container platform: ECR, ECS Fargate, ALB

```hcl
resource "aws_ecr_repository" "api" {
  name                 = "${var.name}-api"
  image_tag_mutability = "IMMUTABLE"   # a given tag (e.g. a git SHA) can never be overwritten — real auditability
  image_scanning_configuration { scan_on_push = true }
}

resource "aws_ecs_cluster" "this" {
  name = "${var.name}-cluster"
}

resource "aws_ecs_task_definition" "api" {
  family                   = "${var.name}-api"
  requires_compatibilities = ["FARGATE"]
  network_mode              = "awsvpc"
  cpu                        = "256"   # 0.25 vCPU
  memory                     = "512"   # 0.5 GB — cheapest Fargate size, fine for a portfolio's traffic
  execution_role_arn         = aws_iam_role.ecs_execution.arn   # pulls image, writes logs, reads secrets
  task_role_arn               = aws_iam_role.ecs_task.arn        # the app's own runtime AWS permissions (none needed yet)

  container_definitions = jsonencode([{
    name  = "api"
    image = "${aws_ecr_repository.api.repository_url}:latest"
    portMappings = [{ containerPort = 8080 }]
    environment = [
      { name = "Jwt__Issuer", value = "formai" },
      { name = "Jwt__Audience", value = "formai" },
      { name = "Email__FrontendBaseUrl", value = "https://app.yourdomain.com" },
      # ElastiCache's endpoint is a Terraform-computed value, not a secret — no auth token is
      # configured on the cluster (same as your local docker-compose redis), so this is just a
      # hostname, safe as a plain environment variable rather than routed through Secrets Manager.
      { name = "ConnectionStrings__Redis", value = "${aws_elasticache_cluster.redis.cache_nodes[0].address}:6379" }
      # ... other non-secret appsettings keys
    ]
    secrets = [
      { name = "ConnectionStrings__DefaultConnection", valueFrom = "${aws_secretsmanager_secret.app_secrets.arn}:ConnectionString::" },
      { name = "Jwt__Secret",                            valueFrom = "${aws_secretsmanager_secret.app_secrets.arn}:JwtSecret::" },
      { name = "Claude__ApiKey",                         valueFrom = "${aws_secretsmanager_secret.app_secrets.arn}:ClaudeApiKey::" }
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
}

resource "aws_ecs_service" "api" {
  name            = "${var.name}-api"
  cluster         = aws_ecs_cluster.this.id
  task_definition = aws_ecs_task_definition.api.arn
  desired_count   = 1
  launch_type     = "FARGATE"

  network_configuration {
    subnets          = var.public_subnet_ids   # no NAT Gateway (Phase 5) — tasks need a public IP for outbound
    security_groups  = [var.ecs_security_group_id]
    assign_public_ip = true
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.api.arn
    container_name   = "api"
    container_port   = 8080
  }
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
  target_type = "ip"   # required for Fargate — tasks are IPs, not EC2 instances
  health_check {
    path                = "/health"   # Swagger is dev-only (see Program.cs), so this is the only path that works in prod
    healthy_threshold   = 2
    unhealthy_threshold = 3
  }
}

resource "aws_lb_listener" "https" {
  load_balancer_arn = aws_lb.this.arn
  port              = 443
  protocol          = "HTTPS"
  ssl_policy         = "ELBSecurityPolicy-TLS13-1-2-2021-06"
  certificate_arn    = var.acm_certificate_arn   # from Phase 8
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
    redirect { port = "443"; protocol = "HTTPS"; status_code = "HTTP_301" }
  }
}
```

Two IAM roles are doing different jobs, and mixing them up is a common beginner mistake worth understanding: the **execution role** is what ECS itself uses to start your container — pull the image, write logs, fetch the secrets above. The **task role** is what your *application code* would use if it called other AWS services at runtime (it doesn't yet — leave it minimal, this is where the S3/SQS permissions from the future PDF pipeline in your roadmap would eventually go).

---

## Phase 8 — TLS certificate and DNS

```hcl
resource "aws_acm_certificate" "api" {
  domain_name       = "api.yourdomain.com"
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

resource "aws_route53_record" "api" {
  zone_id = var.hosted_zone_id
  name    = "api.yourdomain.com"
  type    = "A"
  alias {
    name                   = aws_lb.this.dns_name
    zone_id                = aws_lb.this.zone_id
    evaluate_target_health = true
  }
}
```

Terraform requesting the cert *and* creating the DNS validation record *and* waiting on `aws_acm_certificate_validation` is the fully automated version of what's normally a manual "go click the validation link" step — worth noticing that infra-as-code removed a manual step here, not just documented one.

---

## Phase 9 — Frontend hosting: S3 + CloudFront

The frontend is static output (`npm run build`), so it doesn't need a container or ECS — an S3 bucket behind CloudFront is both cheaper and simpler, and it's the standard pattern for a SPA.

```hcl
resource "aws_s3_bucket" "frontend" {
  bucket = "${var.name}-frontend"
}

resource "aws_s3_bucket_public_access_block" "frontend" {
  bucket                  = aws_s3_bucket.frontend.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls       = true
  restrict_public_buckets = true
}

resource "aws_cloudfront_origin_access_control" "frontend" {
  name                              = "${var.name}-oac"
  origin_access_control_origin_type = "s3"
  signing_behavior                  = "always"
  signing_protocol                  = "sigv4"
}

resource "aws_cloudfront_distribution" "frontend" {
  enabled             = true
  default_root_object = "index.html"

  origin {
    domain_name              = aws_s3_bucket.frontend.bucket_regional_domain_name
    origin_id                = "s3-frontend"
    origin_access_control_id = aws_cloudfront_origin_access_control.frontend.id
  }

  default_cache_behavior {
    target_origin_id       = "s3-frontend"
    viewer_protocol_policy = "redirect-to-https"
    allowed_methods         = ["GET", "HEAD"]
    cached_methods           = ["GET", "HEAD"]
    forwarded_values { query_string = false; cookies { forward = "none" } }
  }

  # SPA routing: unknown paths (e.g. a direct link to /forms/123) should serve index.html, not a CloudFront 404
  custom_error_response {
    error_code         = 404
    response_code      = 200
    response_page_path = "/index.html"
  }

  aliases = ["app.yourdomain.com"]
  viewer_certificate {
    acm_certificate_arn = var.cloudfront_acm_certificate_arn   # must be requested in us-east-1, CloudFront's hard requirement regardless of your main region
    ssl_support_method  = "sni-only"
  }

  restrictions { geo_restriction { restriction_type = "none" } }
}
```

The bucket policy needs to allow *only* this specific CloudFront distribution to read it (via the OAC), not the public — that's what makes this different from the "public S3 website bucket" pattern you'll see in older tutorials, which is no longer the recommended approach.

---

## Phase 10 — GitHub Actions OIDC: no long-lived AWS keys

**Why this matters more than it looks like it does:** the naive approach is generating an IAM user access key and pasting it into a GitHub secret. That key never expires on its own, and if it leaks (a fork, a misconfigured workflow, a compromised action) it's valid until you notice and rotate it. OIDC federation lets GitHub Actions request a *temporary* credential by proving — cryptographically, per-run — that the run really is coming from your repo. No secret ever sits in GitHub at all. This is the current recommended practice, and naming it by name in an interview is a real signal.

1. Create the OIDC identity provider (one-time, per AWS account):
   ```bash
   aws iam create-open-id-connect-provider \
     --url https://token.actions.githubusercontent.com \
     --client-id-list sts.amazonaws.com \
     --thumbprint-list 6938fd4d98bab03faadb97b34396831e3780aea1
   ```
2. Create an IAM role GitHub Actions can assume, trusting only your repo:
   ```hcl
   resource "aws_iam_role" "github_actions_deploy" {
     name = "formai-github-actions-deploy"
     assume_role_policy = jsonencode({
       Version = "2012-10-17"
       Statement = [{
         Effect = "Allow"
         Principal = { Federated = "arn:aws:iam::<account-id>:oidc-provider/token.actions.githubusercontent.com" }
         Action = "sts:AssumeRoleWithWebIdentity"
         Condition = {
           StringEquals = { "token.actions.githubusercontent.com:aud" = "sts.amazonaws.com" }
           StringLike   = { "token.actions.githubusercontent.com:sub" = "repo:<your-github-username>/form-ai:*" }
         }
       }]
     })
   }
   ```
3. Attach **separate, narrowly-scoped** policies to two roles, not `AdministratorAccess` to one. `aws_iam_role.github_actions_deploy` from step 2 is the app-deploy role; it needs a second, genuinely separate role for Terraform, since a compromised app-deploy run should be able to push a bad image, never rewrite your VPC.

   ```hcl
   # --- Role 1: app deploys (deploy.yml) — tightly scoped ---
   data "aws_iam_policy_document" "deploy_permissions" {
     statement {
       sid       = "ECRAuth"
       actions   = ["ecr:GetAuthorizationToken"]
       resources = ["*"]   # this action has no resource-level scoping in AWS's model
     }
     statement {
       sid = "ECRPush"
       actions = [
         "ecr:BatchCheckLayerAvailability", "ecr:PutImage",
         "ecr:InitiateLayerUpload", "ecr:UploadLayerPart", "ecr:CompleteLayerUpload"
       ]
       resources = [aws_ecr_repository.api.arn]
     }
     statement {
       sid       = "ECSDeploy"
       actions   = ["ecs:UpdateService", "ecs:DescribeServices"]
       resources = [aws_ecs_service.api.id]
     }
   }

   resource "aws_iam_role_policy" "deploy" {
     name   = "formai-github-actions-deploy-policy"
     role   = aws_iam_role.github_actions_deploy.id
     policy = data.aws_iam_policy_document.deploy_permissions.json
   }

   # --- Role 2: Terraform (infra.yml) — broad but not admin ---
   resource "aws_iam_role" "github_actions_terraform" {
     name = "formai-github-actions-terraform"
     assume_role_policy = jsonencode({
       Version = "2012-10-17"
       Statement = [{
         Effect    = "Allow"
         Principal = { Federated = "arn:aws:iam::<account-id>:oidc-provider/token.actions.githubusercontent.com" }
         Action    = "sts:AssumeRoleWithWebIdentity"
         Condition = {
           StringEquals = { "token.actions.githubusercontent.com:aud" = "sts.amazonaws.com" }
           StringLike   = { "token.actions.githubusercontent.com:sub" = "repo:<your-github-username>/form-ai:*" }
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
           "iam:CreateRole", "iam:DeleteRole", "iam:GetRole",
           "iam:PutRolePolicy", "iam:DeleteRolePolicy", "iam:GetRolePolicy",
           "iam:AttachRolePolicy", "iam:DetachRolePolicy", "iam:PassRole",
           "iam:TagRole", "iam:ListRolePolicies", "iam:ListAttachedRolePolicies"
         ]
         Resource = "arn:aws:iam::<account-id>:role/formai-*"
       }]
     })
   }
   ```

   `PowerUserAccess` is AWS's own answer to "Terraform needs to touch nearly every service" — broad access, with IAM and Organizations management explicitly excluded, because unrestricted IAM access is equivalent to account takeover. The scoped policy on top adds back just enough IAM (`CreateRole`, `PassRole`, ...) for Terraform to manage the ECS execution/task roles from Phase 7, restricted to role names starting with `formai-*` via the `Resource` condition — that prefix match is the actual guardrail, not the action list.

   **Worth naming honestly, not hiding:** that same `formai-*` prefix also matches this role's own name and the deploy role's, so a fully compromised `infra.yml` run could in principle modify its own trust policy. Closing that gap completely needs a permission boundary or a human-only bootstrap step outside CI — more machinery than a solo portfolio project needs, but a materially smaller blast radius than `AdministratorAccess` either way, and the right thing to say out loud in an interview rather than claim it's airtight.

---

## Phase 11 — Two pipelines

You already agreed infra changes need their own pipeline, separate from app deploys — here's why that split matters in practice, not just in theory: an app deploy (new image, same infrastructure) is safe to run on every merge to `main`. An infra change (a different instance size, a new security group rule) can have side effects — replacing a resource, a few minutes of downtime — that deserve a deliberate, reviewed `apply`, not an automatic one.

### `.github/workflows/infra.yml`

```yaml
name: Infrastructure

on:
  pull_request:
    paths: ['infra/**']
  push:
    branches: [main]
    paths: ['infra/**']

permissions:
  id-token: write   # required for OIDC
  contents: read
  pull-requests: write   # to comment the plan on the PR

jobs:
  plan:
    if: github.event_name == 'pull_request'
    runs-on: ubuntu-latest
    defaults: { run: { working-directory: infra/envs/prod } }
    steps:
      - uses: actions/checkout@v7
      - uses: aws-actions/configure-aws-credentials@v4
        with:
          role-to-assume: arn:aws:iam::<account-id>:role/formai-github-actions-terraform
          aws-region: us-east-1
      - uses: hashicorp/setup-terraform@v3
      - run: terraform init
      - run: terraform plan -no-color -out=tfplan
      - run: terraform show -no-color tfplan > plan.txt
      - uses: actions/github-script@v7
        with:
          script: |
            const fs = require('fs');
            const plan = fs.readFileSync('infra/envs/prod/plan.txt', 'utf8').slice(0, 60000);
            github.rest.issues.createComment({
              issue_number: context.issue.number,
              owner: context.repo.owner,
              repo: context.repo.repo,
              body: `### Terraform Plan\n\`\`\`\n${plan}\n\`\`\``
            });

  apply:
    if: github.event_name == 'push'
    runs-on: ubuntu-latest
    environment: production   # see the manual-approval note below
    defaults: { run: { working-directory: infra/envs/prod } }
    steps:
      - uses: actions/checkout@v7
      - uses: aws-actions/configure-aws-credentials@v4
        with:
          role-to-assume: arn:aws:iam::<account-id>:role/formai-github-actions-terraform
          aws-region: us-east-1
      - uses: hashicorp/setup-terraform@v3
      - run: terraform init
      - run: terraform apply -auto-approve
```

`environment: production` is a real GitHub feature, not decoration: GitHub repo Settings → Environments → `production` → check "Required reviewers" and add yourself. The `apply` job will pause and wait for you to click Approve, even though you pushed the merge — that's a manual gate on the one step that's genuinely worth pausing for, without needing a human to babysit `terraform plan` output on their own machine.

### `.github/workflows/deploy.yml`

```yaml
name: Deploy

on:
  push:
    branches: [main]
    paths-ignore: ['infra/**', 'docs/**']

permissions:
  id-token: write
  contents: read

jobs:
  build-and-deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - uses: aws-actions/configure-aws-credentials@v4
        with:
          role-to-assume: arn:aws:iam::<account-id>:role/formai-github-actions-deploy
          aws-region: us-east-1
      - uses: aws-actions/amazon-ecr-login@v2
        id: ecr
      - run: |
          IMAGE="${{ steps.ecr.outputs.registry }}/formai-prod-api:${{ github.sha }}"
          docker build -f src/FormAI.API/Dockerfile -t "$IMAGE" .
          docker push "$IMAGE"
      - run: |
          aws ecs update-service --cluster formai-prod-cluster \
            --service formai-prod-api --force-new-deployment
```

Tagging the image with `${{ github.sha }}` instead of `latest` is what makes `image_tag_mutability = "IMMUTABLE"` from Phase 7 actually useful — you can always trace a running task back to the exact commit it was built from, and `git revert` + push gives you a real rollback path.

Wire this after your existing `ci.yml` build-and-test job passes — either by making `deploy.yml` a second job in the same workflow file gated on the first job succeeding, or by triggering it via `workflow_run` keyed to `ci.yml`'s completion. Either is fine; the important property is that a broken build or failing test never reaches `docker push`.

---

## Phase 12 — First deploy and smoke test

1. `cd infra/envs/prod && terraform init && terraform plan` — read the plan output line by line before you ever `apply`. This is the habit that matters more than any tool: know what's about to change before it changes.
2. `terraform apply` — this is your only fully-manual apply; every one after this goes through the pipeline. Expect several minutes (RDS is the slow part).
3. Push the Dockerfile to `main` to trigger `deploy.yml`, which pushes the first real image and updates the ECS service.
4. `curl -I https://api.yourdomain.com/health` — confirm you get a 200 over HTTPS with a valid cert.
5. Build and sync the frontend: `cd frontend && npm run build && aws s3 sync dist/ s3://formai-prod-frontend --delete`, then create a CloudFront invalidation (`aws cloudfront create-invalidation --distribution-id <id> --paths "/*"`) so the new build is actually served instead of the cached old one. (Folding this into a third small pipeline job is a natural next step once the manual version works.)
6. Walk through the actual product at `https://app.yourdomain.com` end to end — register, confirm email (you'll need Phase 13's note on this), generate a form, publish, answer, view results.

---

## Phase 13 — One loose end: production email

Locally you use Mailpit; in production there's nothing listening on `Email__SmtpHost`. Registration will fail to send confirmation emails without a real SMTP provider. The path of least resistance in this same AWS account is **Amazon SES** — verify your domain (Route 53 makes the DNS records easy since you already own the zone), request production access (new accounts start in a sandbox that can only email verified addresses — fine for a demo you're walking a recruiter through, worth knowing about before you assume it "just works"), and point `Email__SmtpHost`/`SmtpPort` at SES's SMTP endpoint with credentials from Secrets Manager, same pattern as Phase 6. This is intentionally out of scope for this pass — call it out in `docs/known-gaps.md` if you don't get to it before you start applying.

---

## Cost awareness and teardown

You're running most of this only 8-11pm daily, so the number that matters is the schedule-aware one, not the always-on one — with one exception: ElastiCache has no "stop" state, so it runs continuously regardless of the schedule.

| Item | Always-on | This setup |
|---|---|---|
| NAT Gateway | ~$33/mo | **$0** — removed (Phase 5) |
| ElastiCache `cache.t4g.micro` | ~$12/mo | **~$12/mo** — can't be paused, runs 24/7 by design (Phase 6) |
| Fargate (0.25 vCPU/0.5GB) | ~$9/mo | **~$1.10/mo** — scaled to 0 outside the 8-11pm window |
| RDS `db.t4g.micro` | ~$12/mo | **~$3.75/mo** — stopped outside the window; storage still bills |
| ALB | ~$16-20/mo | **~$16-20/mo** — left running; not worth the daily teardown complexity |
| Route 53 zone + domain + Secrets Manager + S3/CloudFront | ~$3.30/mo | **~$3.30/mo** — all usage-based or flat, negligible either way |
| **Total** | **~$85-90/mo** | **~$36-40/mo** |

That's right at your $40 cap with little to no margin for ALB traffic spikes or data transfer overages — worth knowing going in, not a surprise to discover on a bill later. If it turns out to run over, the one real lever left is scheduling ElastiCache the same way as RDS (delete/recreate daily instead of stop/start, since that's the only lifecycle it supports) — more automation than the current two-command toggle, and worth doing later if the margin turns out to matter, not something to build now.

Bringing it up/down for your 8-11pm window (ECS + RDS) is two manual commands (see the scheduling note from earlier), not automated yet — that's a reasonable next step once the base deployment works, not a blocker to doing it manually for now.

To tear down everything Terraform manages:
```bash
cd infra/envs/prod
terraform destroy
```
This does **not** touch the S3 state bucket or your Route 53 domain registration — those were created by hand in Phases 1 and 3, deliberately outside Terraform's blast radius, so a `destroy` can't accidentally release your domain. Delete the bucket manually if you truly want nothing left.

---

## What this demonstrates in an interview

Not "I deployed to AWS" — the specific, checkable claims: least-privilege IAM with no long-lived CI credentials (OIDC), remote Terraform state with locking, a module/env split instead of one flat file, secrets never touching a task definition or a committed file, a network with a real trust chain instead of everything in one security group, and a deliberate separation between infra changes (reviewed, gated) and app deploys (continuous). Each of those is a sentence you can back up by pointing at a specific file in `infra/`.

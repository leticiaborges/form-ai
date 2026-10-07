# Plan: deploy the LiteLLM AI gateway to AWS (low cost, separable)

## Context
Locally the API calls a LiteLLM gateway (`docker/litellm/`, compose services `litellm` + `litellm-db`). `infra/` (Terraform, `envs/prod` + modules `networking, data, container-platform, dns-tls, frontend`) knows nothing about it: the API task still gets `Claude__ApiKey` from Secrets Manager, and no gateway, gateway DB or secrets exist. The gateway should be deployable and **removable from this repo later** (it will serve other apps), and AWS cost must stay minimal.

Current infra facts that shape the options: no NAT gateway (ECS tasks sit in public subnets with public IPs; RDS/Redis in private subnets with no internet route); one RDS `db.t4g.micro` Postgres 18; one ALB; Redis ElastiCache; secrets in Secrets Manager; one ECS cluster.

## Required changes (all options)
1. **API wiring** (`modules/container-platform/main.tf`, `modules/data/main.tf`, `envs/prod/variables.tf`, `.github/workflows/infra.yml`): drop `Claude__ApiKey` / `claude_api_key` / `CLAUDE_API_KEY`; add env `Ai__GatewayUrl` (internal gateway URL) and secret `Ai__ApiKey` (the LiteLLM *app* key `form-ai-app`, not the master key). Also set `Ai__TextAlias` / `Ai__VisionAlias` / `Ai__TimeoutSeconds` if defaults differ.
2. **ALB idle timeout** `aws_lb.this.idle_timeout` > 90 s (60 default; the README flags this as "not set yet" in the timeout budget).
3. **Separate Terraform root for the gateway**: new `infra/envs/ai-gateway/` with its **own state key** (`backend.tf`) and a self-contained module `infra/modules/ai-gateway/` whose only inputs are plain values (`vpc_id`, `subnet_ids`, `client_security_group_ids`, optional DB endpoint) and whose outputs are `gateway_url` + `app_key_secret_arn`. It must not reference `module.data`/`container_platform` internals. `envs/prod` consumes it via a variable (URL) or `terraform_remote_state`. Moving it to another repo later = moving two folders + `docker/litellm/`, no refactor.
4. **Gateway image**: small `docker/litellm/Dockerfile` (`FROM ghcr.io/berriai/litellm:v1.103.1` + `COPY config.yaml`), pushed to its own ECR repo (Fargate can't bind-mount the config). Image build/push workflow `ai-gateway-deploy.yml` (path-filtered to `docker/litellm/**`), independent from `deploy.yml`.
5. **Secrets** for the gateway: `LITELLM_MASTER_KEY`, `LITELLM_SALT_KEY` (never change after data exists), `ANTHROPIC_API_KEY`, `OPENAI_API_KEY`, `LITELLM_APP_KEY`, DB password. Own secret(s), readable only by the gateway's execution role. (SSM Parameter Store SecureString is free vs $0.40/secret/mo in Secrets Manager — optional saving.)
6. **Networking**: gateway SG allows 4000 **only from the API's ECS SG**; no public ingress. It needs outbound internet to Anthropic/OpenAI, so (no NAT) it stays in a public subnet with a public IP, same as the API today. Never put it behind the public ALB.
7. **Bootstrap, one-off**: create the gateway DB/role (see DB options), then run `provision-app-key.sh` against the deployed gateway (one-off ECS task or `aws ecs execute-command`) to mint `form-ai-app` with its monthly budget; store that key as `Ai__ApiKey`.
8. **Docs**: update `docker/litellm/README.md` (AWS section), `docs/known-gaps.md`, CLAUDE.md "Local infrastructure", and propose an ADR (gateway hosting + DB choice).

## Option set A — where the gateway DB lives (LiteLLM needs Postgres for virtual keys/budgets/spend; no DB = no app key budget)
Prices are approximate us-east-1 on-demand figures from memory; check the AWS calculator before committing.

| | Extra cost / mo | Pros | Cons |
|---|---|---|---|
| **A1. New database + role on the existing RDS instance** | **~$0** | Cheapest; managed backups/patching; private subnet; already wired | Shares CPU/RAM/connections of a db.t4g.micro (1 GB) with the app; blast radius shared; gateway-owned DB must be moved (pg_dump) when split out; LiteLLM runs its own migrations so its role must own its DB (don't reuse `form_ai_app`) |
| **A2. Dedicated RDS db.t4g.micro** | ~$14–15 (instance + 20 GB) | Real isolation, trivially relocatable, managed backups | Most expensive; a second always-on instance for tiny traffic |
| **A3. Postgres container on an EC2 box (gateway's own)** | $0 for DB itself if it shares the gateway's EC2 | Cheaper than A2; full control | You own backups (EBS snapshots/cron), patching, restore; data on one EBS volume; a bad upgrade or instance loss = lose spend log/keys (keys re-creatable, spend history not) |
| **A4. External free-tier Postgres (Neon/Supabase)** | $0 | Free, managed | New vendor, internet round trip + TLS, free-tier limits/auto-suspend cold starts, spend data leaves AWS |
| **A5. Aurora Serverless v2** | ~$10+ (storage + min capacity) | Scales | Not cheaper than A2 at this size; overkill |

Answer to your question: an EC2 with Postgres inside is cheaper than a **dedicated** RDS (A3 vs A2), but the cheapest of all is **A1** (no new DB infra), and EC2-Postgres only wins if you also run the gateway on that same EC2 (then the DB is free). Its price is operational burden.

### A1 clarifications
- **Cost**: no new instance charge, so ~$0 extra. Only side costs: a little of the 20 GB storage (spend log rows are small) and load on a 1 GB burstable instance. If storage ever needs to grow, that is $0.115/GB-month.
- **Fully separate database and user**: yes. Create role `litellm` and `CREATE DATABASE litellm OWNER litellm` (on RDS the master user must first `GRANT litellm TO form_ai_admin`), `REVOKE CONNECT ON DATABASE litellm FROM PUBLIC`, and `REVOKE CONNECT ON DATABASE form_ai FROM litellm`. The DB owner can run LiteLLM's own Prisma migrations at startup (PG15+: the owner can create in `public`). `form_ai_app` and `form_ai_migrator` get no access to `litellm` and vice versa, so the app's least-privilege model ([ADR 0007](../../docs/adr/0007-migrations-run-as-a-separate-role-in-a-deploy-job.md)) is untouched. The gateway needs its own SG rule into the `data` SG on 5432.
- **Found while reading**: `docker/litellm/config.yaml` has `store_prompts_in_spend_logs: true` and `turn_off_message_logging: false`, while the README says prompts are not stored. Fix before deploy, otherwise user source text (and base64 PDFs) land in the gateway DB and inflate the storage on that shared instance.

## Option set B — where the gateway process runs
LiteLLM needs roughly 0.5–1 GB RAM; 0.25 vCPU/0.5 GB is too small.

| | Extra cost / mo | Pros | Cons |
|---|---|---|---|
| **B1. Fargate service (ARM, 0.5 vCPU / 1 GB), 1 task** | ~$14–18 incl. public IPv4 | Same pattern as the API (ECR, ECS, logs, no patching, no SSH); easy rolling deploys | Task IP changes → needs Cloud Map/Service Connect for a stable internal name (~free); costs slightly more than EC2 |
| **B2. Fargate Spot** | ~$5–8 | Up to ~70% cheaper | Can be reclaimed with 2 min notice → gateway down until replaced; API generation returns 503 meanwhile |
| **B3. EC2 t4g.small (2 GB) + Docker** | ~$12 + $0.7 EBS + $3.65 IPv4 ≈ $16 (t4g.micro ≈ $10 but 1 GB is tight, esp. with Postgres alongside) | Cheapest steady cost, fixed private IP (simple URL), can host Postgres too (A3) | OS patching, Docker upkeep, SSM/SSH access, manual deploy script, single point of failure; savings vs B1 are only ~$2 |
| **B4. Sidecar container in the API ECS task** | ~$9 (API task grows to 0.5 vCPU/1 GB) | Cheapest, `localhost:4000`, no networking | **Violates your goal**: gateway can't be shared by other apps, scales/restarts with the API, config coupled to this service. Not recommended |
| **B5. Internal ALB in front** | +$16 | Stable DNS/TLS | Pointless cost; avoid (use Cloud Map instead) |

Reaching it from the API: Cloud Map private DNS namespace (e.g. `gateway.ai.internal`) or ECS Service Connect — about $0–1. Traffic is plain HTTP inside the VPC, restricted by security group + the app key; acceptable here.

## Recommendation
**B1 (Fargate ARM 0.5 vCPU/1 GB) + A1 (separate database and owner role on the existing RDS)** → about **$15–18/month extra**, no new patched servers, consistent with the existing deploy model, and cleanly separable (own Terraform root/state, own ECR, own secrets, own workflow, own DB + role that can be `pg_dump`ed out).

Cost-minimum alternative: **B3 + A3** (one t4g.small EC2 running `docker compose` with gateway + Postgres) ≈ $16/mo — barely cheaper than the recommendation while adding ops work, so only worth it if you value full control.
If isolation matters more than cost: B1 + A2 (≈ $32/mo).

## Implementation steps (for the recommended option, after you approve)
1. Create `docker/litellm/Dockerfile`; add ECR repo + push workflow.
2. Add `infra/modules/ai-gateway` (ECR, log group, IAM execution role, secrets, task def, ECS service, Cloud Map namespace/service, SG rule) and `infra/envs/ai-gateway` (backend, provider, variables).
3. Gateway DB bootstrap: new one-off task/script creating `litellm` role + database on the RDS instance (separate from `docker/postgres/init/01-create-app-user.sh`), with `LITELLM_DB_PASSWORD` in the gateway secret; set `DATABASE_URL` with a small connection limit.
4. Change `container-platform` task def (Ai__* env/secret), set ALB idle timeout, update `infra.yml` secrets.
5. Apply gateway root → bootstrap DB → deploy image → run `provision-app-key.sh` → store app key → apply `prod`.
6. Update docs/ADR/known-gaps.

## Verification
- `terraform validate` + `plan` per root (no changes to unrelated resources in `prod` beyond the API task/ALB).
- From a one-off task in the API SG: `curl http://<gateway>:4000/health/liveliness`; confirm the same call from outside the VPC is unreachable.
- Run `docker/litellm/smoke.sh` pointed at the AWS gateway (costs a few cents); generate a form end-to-end from the deployed frontend, including a PDF and a forced failover.
- Check the AWS bill/Cost Explorer after a week against the estimate.

## Open questions for you
- Is ~$15–18/mo acceptable, or do you want the ~$5–8 Fargate Spot variant (with the downtime risk)?
- Is sharing the existing RDS instance acceptable (A1), or do you want a dedicated DB (A2)?

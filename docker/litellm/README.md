# AI gateway (LiteLLM)

Every model call from FormAI goes through this gateway. The app knows two **model aliases** and never a real model id. Decision: [ADR 0008](../../docs/adr/0008-ai-gateway-instead-of-direct-provider-client.md).

## Reaching it

| From | Base URL |
| ---- | -------- |
| A container on the Compose network | `http://litellm:4000` |
| The host | `http://127.0.0.1:4000` (loopback only, never published on another interface) |

Route: OpenAI-compatible `POST /v1/chat/completions`. The gateway database (`litellm-db`) publishes no port.

## Aliases

| Alias | Primary model | Failover | Accepts |
| ----- | ------------- | -------- | ------- |
| `form-generator` | `claude-haiku-4-5-20251001` | `gpt-5.2` | text |
| `form-generator-vision` | `claude-sonnet-5-5` | `gpt-5.2` | text and PDF |

Any other model name is rejected with a 4xx. The text model is a placeholder until the model comparison (plan T-3).

**Failover rule.** Each alias has two deployments in `config.yaml`, ranked by `order`. When the primary fails with a provider error, the gateway retries once on the next `order` (`router_settings.num_retries: 1`). Retry and timeout policy is set properly in slice 2. `gpt-5.2` must be reachable for failover, which needs `OPENAI_API_KEY`.

**Status of what is verified.** Both aliases answer text through the primary model, and cost lands in the spend log (`smoke.sh`). Failover to `gpt-5.2` and PDF input through either provider are **not yet verified**: they need an OpenAI key, and PDFs are checked in slice 2.

## Calling it

```bash
set -a; . ./.env; set +a   # from the repo root

curl -s http://127.0.0.1:4000/v1/chat/completions \
  -H "Authorization: Bearer $LITELLM_MASTER_KEY" -H 'Content-Type: application/json' \
  -d '{"model":"form-generator","max_tokens":50,"user":"3f2b8c1e-0000-0000-0000-000000000000","messages":[{"role":"user","content":"Say hi"}]}'

curl -s http://127.0.0.1:4000/v1/chat/completions \
  -H "Authorization: Bearer $LITELLM_MASTER_KEY" -H 'Content-Type: application/json' \
  -d '{"model":"form-generator-vision","max_tokens":50,"user":"3f2b8c1e-0000-0000-0000-000000000000","messages":[{"role":"user","content":"Say hi"}]}'
```

Always set `max_tokens`. The reply's `model` field is the alias; the model that actually answered is in the spend log (`LiteLLM_SpendLogs.model`).

## Metadata

`user` carries the user's **guid only**, never an email or a name.

Prompts and responses are not logged and not stored in the spend log (`turn_off_message_logging`, `store_prompts_in_spend_logs: false`). The spend log keeps model, tokens and cost.

## Keys and variables

| Key | Used by | Notes |
| --- | ------- | ----- |
| `LITELLM_MASTER_KEY` | Developers, and `smoke.sh` | The only client key today. Slice 2 adds an app key with `max_budget`, which is what the API will use. |
| `LITELLM_SALT_KEY` | The gateway | Encrypts credentials it stores. Do not change it once data exists. |
| `ANTHROPIC_API_KEY`, `OPENAI_API_KEY` | The gateway | Provider keys. |
| `LITELLM_DB_PASSWORD` | The gateway and `litellm-db` | Its own role and database, unrelated to `form_ai_app` and `form_ai_migrator`. |

All live in the untracked `.env` (names in `.env.example`). `docker compose up` fails if any of the first four, or the password, is unset. `OPENAI_API_KEY` may be empty for now, in which case failover cannot answer.

## Checking it

`bash docker/litellm/smoke.sh` runs the runtime checks against the live stack. It calls real providers and costs a few cents; CI never runs it. Checks it cannot run print `UNVERIFIED`.

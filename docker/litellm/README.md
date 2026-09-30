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

**Failover rule.** Each alias has two deployments in `config.yaml`, ranked by `order`. A request makes at most **two attempts**: the primary once, then the next `order` (`gpt-5.2`). `num_retries` is 0 and provider SDK retries are off (`max_retries: 0`); the failover is the only second attempt. A deployment that fails cools down for 30 s (`allowed_fails: 0`, `cooldown_time: 30`), so the next requests go straight to `gpt-5.2`. `gpt-5.2` needs `OPENAI_API_KEY`.

Two behaviours to design around:

- A request the provider rejects with a 4xx (for example a PDF it cannot read) **also fails over once**. This cannot be disabled in the pinned version (tested with `num_retries` and `retry_policy`). The caller gets a 4xx if `gpt-5.2` rejects it too.
- With `num_retries: 1` a stalled pair of providers took 4 attempts (140 s). Do not raise it.

## Timeout budget

The gateway alone retries and times out. The client never retries (plan AI-7). Each link must be shorter than the next:

| Link | Value | Status |
| ---- | ----- | ------ |
| One gateway attempt (`router_settings.timeout`) | 35 s | Verified: a stalled provider is abandoned at about 35 s (`smoke.sh`) |
| Gateway worst case, 2 attempts | 70 s | Verified: both providers stalled returned an error in 71 s |
| Client timeout (`HttpClient`, slice 3) | 80 s | **Not built yet** |
| Overall generation limit | 90 s | Plan decision, enforced by the client in slice 3 |
| Load balancer idle timeout | above 90 s | **Not set yet**, checked at deploy (slice 8) |

Kestrel's request timeout is the remaining link and is also unchecked until the endpoint exists (slice 10).

Measured latency through `form-generator-vision` with the one-page fixture `fixtures/sample.pdf`: about 1 s from `claude-sonnet-5-5` and 2 s from `gpt-5.2`; a stalled primary costs about 38 s end to end. **The fixture is trivial.** Latency of realistic multi-page and scanned PDFs is measured with the slice 17 evaluation set, and until then it is not known that they fit 35 s.

**Status of what is verified** (`smoke.sh`, 62 assertions, 0 unverified): both aliases answer through the primary model with a cost in the spend log; failover to `gpt-5.2` for each alias; a stalled primary falls over within the budget; a PDF is read through both providers; the app key's scope and budget. Not verified: realistic PDF latency, and the client, Kestrel and load balancer timeouts above.

## PDF input

Send a PDF to `form-generator-vision` as a `file` content part (OpenAI format), base64 in a data URL:

```bash
B64=$(base64 -w0 docker/litellm/fixtures/sample.pdf)
curl -s http://127.0.0.1:4000/v1/chat/completions \
  -H "Authorization: Bearer $LITELLM_APP_KEY" -H 'Content-Type: application/json' \
  -d "{\"model\":\"form-generator-vision\",\"max_tokens\":60,\"messages\":[{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"What is the secret codeword in the document? Answer with the word only.\"},{\"type\":\"file\",\"file\":{\"filename\":\"sample.pdf\",\"file_data\":\"data:application/pdf;base64,$B64\"}}]}]}"
```

The answer is `ZEPHYRQUILL` from either provider.

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
| `LITELLM_MASTER_KEY` | Developers, `provision-app-key.sh` and `smoke.sh` | Administers the gateway. The API never uses it. |
| `LITELLM_APP_KEY` | The API (from slice 3) | Key alias `form-ai-app`. Limited to the two aliases, cannot call management routes, and has a monthly budget. Its value is chosen by you in `.env` (must start with `sk-`). |
| `LITELLM_SALT_KEY` | The gateway | Encrypts credentials it stores. Do not change it once data exists. |
| `ANTHROPIC_API_KEY`, `OPENAI_API_KEY` | The gateway | Provider keys. |
| `LITELLM_DB_PASSWORD` | The gateway and `litellm-db` | Its own role and database, unrelated to `form_ai_app` and `form_ai_migrator`. |

| `LITELLM_APP_MAX_BUDGET_USD` | `provision-app-key.sh` | Monthly budget of the app key in USD, default 10. The global spending ceiling's backstop: set it slightly above the app-side `Ai:MonthlyBudgetUsd` (slice 7). |

### The app key

`bash docker/litellm/provision-app-key.sh` creates the key, or updates its budget and models if it exists. It is idempotent, needs the gateway running, and exits 2 without calling anything when `LITELLM_APP_KEY` is unset. Run it again after `docker compose down -v`: the key is recreated with the same value. A call with a spent key is refused with a 4xx naming the budget; the budget resets every month (`budget_duration: 1mo`, not tested because a month cannot be waited for).

All live in the untracked `.env` (names in `.env.example`). `docker compose up` fails if `LITELLM_MASTER_KEY`, `LITELLM_SALT_KEY`, `LITELLM_DB_PASSWORD` or `ANTHROPIC_API_KEY` is unset. `OPENAI_API_KEY` is needed for failover and for the `gpt-5.2` checks in `smoke.sh`; with it empty those checks print `UNVERIFIED`.

## Checking it

`bash docker/litellm/smoke.sh` runs the runtime checks against the live stack. It calls real providers and costs a few cents; CI never runs it. Checks it cannot run print `UNVERIFIED`.

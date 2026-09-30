#!/usr/bin/env bash
# Smoke test for the local AI gateway (slice ai-gateway-local). Calls real providers:
# needs Docker, keys in .env, and costs a few cents per run. Never run in CI.
# Assertions map to .specs/features/ai-gateway-local/spec.md (P1-n = gateway, S-n = isolation).
set -u
cd "$(dirname "$0")/../.."
set -a; . ./.env; set +a

URL=http://127.0.0.1:4000
DB=form-ai-litellm-db
PASS=0; FAIL=0; UNVERIFIED=0
ok()   { PASS=$((PASS+1)); echo "  ok    $1"; }
bad()  { FAIL=$((FAIL+1)); echo "  FAIL  $1"; }
unv()  { UNVERIFIED=$((UNVERIFIED+1)); echo "  UNVERIFIED  $1"; }
check(){ if [ "$2" = "$3" ]; then ok "$1"; else bad "$1 (expected '$3', got '$2')"; fi; }

chat() { # model key [extra-content]
  curl -s -m 90 -o /tmp/gw_body -w '%{http_code}' "$URL/v1/chat/completions" \
    -H "Authorization: Bearer $2" -H 'Content-Type: application/json' \
    -d "{\"model\":\"$1\",\"max_tokens\":20,\"messages\":[{\"role\":\"user\",\"content\":\"Say hi. ${3:-}\"}]}"
}
sql() { docker exec "$DB" psql -U litellm -d litellm -tA -c "$1"; }

START=$(sql "select now()")

echo "Completions (P1-4, P1-5)"
check "form-generator returns 200" "$(chat form-generator "$LITELLM_MASTER_KEY")" 200
check "form-generator answers from claude-haiku-4-5" "$(sleep 20; sql "select model from \"LiteLLM_SpendLogs\" where model_group='form-generator' order by \"startTime\" desc limit 1")" "anthropic/claude-haiku-4-5-20251001"
check "form-generator-vision returns 200" "$(chat form-generator-vision "$LITELLM_MASTER_KEY")" 200
sleep 20
check "form-generator-vision answers from claude-sonnet-5-5" "$(sql "select model from \"LiteLLM_SpendLogs\" where model_group='form-generator-vision' order by \"startTime\" desc limit 1")" "anthropic/claude-sonnet-5-5"

echo "Spend log cost (P1-8)"
for g in form-generator form-generator-vision; do
  check "$g has a non-null cost > 0" "$(sql "select count(*) from \"LiteLLM_SpendLogs\" where model_group='$g' and \"startTime\" >= '$START' and spend is not null and spend > 0")" "$(sql "select count(*) from \"LiteLLM_SpendLogs\" where model_group='$g' and \"startTime\" >= '$START'")"
done

echo "Rejections (P1-9, P1-10)"
c=$(chat some-other-model "$LITELLM_MASTER_KEY"); if [ "$c" -ge 400 ] && [ "$c" -lt 500 ]; then ok "unknown model is 4xx ($c)"; else bad "unknown model expected 4xx, got $c"; fi
check "no key is 401" "$(curl -s -o /dev/null -w '%{http_code}' -H 'Content-Type: application/json' -d '{"model":"form-generator","messages":[{"role":"user","content":"x"}]}' "$URL/v1/chat/completions")" 401
check "wrong key is 401" "$(chat form-generator sk-wrong)" 401

echo "Config (P1-1, P1-3)"
check "only the two aliases are model names" "$(grep -E '^\s*-?\s*model_name:' docker/litellm/config.yaml | awk '{print $NF}' | sort -u | tr '\n' ' ')" "form-generator form-generator-vision "
img=$(grep -E '^\s+image: ghcr.io/berriai/litellm:' docker-compose.yml | sed 's/.*://')
if echo "$img" | grep -Eq '^v[0-9]+\.[0-9]+\.[0-9]+$'; then ok "image pinned to exact tag $img"; else bad "image tag '$img' is not exact"; fi

echo "Markers (S-5, S-6)"
check "message logging is off in config" "$(grep -cE '^\s*turn_off_message_logging:\s*true\s*$' docker/litellm/config.yaml)" 1
check "prompts are not stored in the spend log in config" "$(grep -cE '^\s*store_prompts_in_spend_logs:\s*false\s*$' docker/litellm/config.yaml)" 1
M="MARKER-$(date +%s)-$RANDOM"
check "marker request returns 200" "$(chat form-generator "$LITELLM_MASTER_KEY" "$M")" 200
sleep 20
check "marker absent from container logs" "$(docker logs form-ai-litellm 2>&1 | grep -c "$M")" 0
check "marker absent from spend log rows" "$(sql "select count(*) from \"LiteLLM_SpendLogs\" s where s::text like '%$M%'")" 0

echo "Isolation (S-3, S-4, S-7, S-8, S-9)"
leaks=0
for v in LITELLM_MASTER_KEY LITELLM_SALT_KEY LITELLM_DB_PASSWORD ANTHROPIC_API_KEY OPENAI_API_KEY; do
  val="${!v}"; [ -n "$val" ] && git grep -qF -- "$val" && leaks=$((leaks+1))
done
check "no secret value in the tracked tree" "$leaks" 0
for v in LITELLM_MASTER_KEY LITELLM_SALT_KEY LITELLM_DB_PASSWORD ANTHROPIC_API_KEY; do
  if env "$v=" docker compose config -q >/dev/null 2>&1; then bad "compose accepts empty $v"; else ok "compose refuses empty $v"; fi
done
check "gateway port bound to 127.0.0.1" "$(docker port form-ai-litellm 4000/tcp | tr -d '\r')" "127.0.0.1:4000"
check "gateway database publishes no port" "$(docker port "$DB" | wc -l | tr -d ' ')" 0
if docker exec -e PGPASSWORD="$APP_DB_PASSWORD" "$DB" psql -h 127.0.0.1 -U form_ai_app -d litellm -c 'select 1' >/dev/null 2>&1; then bad "form_ai_app can connect to the gateway database"; else ok "form_ai_app is refused by the gateway database"; fi

echo "Fallback to gpt-5.2 (P1-6, P1-7)"
if [ -z "${OPENAI_API_KEY:-}" ]; then
  unv "fallback for form-generator: OPENAI_API_KEY is empty"
  unv "fallback for form-generator-vision: OPENAI_API_KEY is empty"
else
  ANTHROPIC_API_KEY=sk-ant-invalid docker compose up -d litellm >/dev/null 2>&1
  for i in $(seq 1 30); do [ "$(docker inspect -f '{{.State.Health.Status}}' form-ai-litellm)" = healthy ] && break; sleep 5; done
  for g in form-generator form-generator-vision; do
    check "$g answers 200 with a failing primary" "$(chat $g "$LITELLM_MASTER_KEY")" 200
    sleep 20
    check "$g was answered by gpt-5.2" "$(sql "select model from \"LiteLLM_SpendLogs\" where model_group='$g' order by \"startTime\" desc limit 1")" "openai/gpt-5.2"
  done
  docker compose up -d litellm >/dev/null 2>&1
fi

echo; echo "passed=$PASS failed=$FAIL unverified=$UNVERIFIED"
[ "$FAIL" -eq 0 ]

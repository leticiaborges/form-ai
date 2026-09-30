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

for i in $(seq 1 30); do [ "$(docker inspect -f '{{.State.Health.Status}}' form-ai-litellm)" = healthy ] && break; sleep 5; done
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

echo "App key (app-key spec P1 AC 1-11)"
APP_ALIAS=form-ai-app
check "provision script refuses an unset LITELLM_APP_KEY without calling the gateway (exit 2)" "$(env -u LITELLM_APP_KEY ENV_FILE=/dev/null LITELLM_URL=http://127.0.0.1:1 bash docker/litellm/provision-app-key.sh >/dev/null 2>&1; echo $?)" 2
bash docker/litellm/provision-app-key.sh >/dev/null && bash docker/litellm/provision-app-key.sh >/dev/null
check "two provisioning runs leave exactly one $APP_ALIAS key" "$(sql "select count(*) from \"LiteLLM_VerificationToken\" where key_alias='$APP_ALIAS'")" 1
INFO=$(curl -s "$URL/key/info" -G --data-urlencode "key=$LITELLM_APP_KEY" -H "Authorization: Bearer $LITELLM_MASTER_KEY")
keyinfo() { echo "$INFO" | python3 -c "import json,sys; print(json.load(sys.stdin)['info']$1)"; }
check "app key models are the two aliases only" "$(keyinfo "['models']")" "['form-generator', 'form-generator-vision']"
check "app key max_budget is LITELLM_APP_MAX_BUDGET_USD" "$(keyinfo "['max_budget']")" "$(python3 -c "print(float('${LITELLM_APP_MAX_BUDGET_USD:-10}'))")"
check "app key budget_duration is 1mo" "$(keyinfo "['budget_duration']")" 1mo
check "app key form-generator returns 200" "$(chat form-generator "$LITELLM_APP_KEY")" 200
check "app key form-generator-vision returns 200" "$(chat form-generator-vision "$LITELLM_APP_KEY")" 200
c=$(chat some-other-model "$LITELLM_APP_KEY"); if [ "$c" -ge 400 ] && [ "$c" -lt 500 ]; then ok "app key with another model is 4xx ($c)"; else bad "app key with another model expected 4xx, got $c"; fi
c=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$URL/key/generate" -H "Authorization: Bearer $LITELLM_APP_KEY" -H 'Content-Type: application/json' -d '{}')
if [ "$c" = 401 ] || [ "$c" = 403 ]; then ok "app key on /key/generate is $c"; else bad "app key on /key/generate expected 401 or 403, got $c"; fi
sleep 20
check "spend-log rows of app-key calls are attributed to $APP_ALIAS" "$(sql "select count(*) from \"LiteLLM_SpendLogs\" s join \"LiteLLM_VerificationToken\" v on s.api_key = v.token where v.key_alias='$APP_ALIAS' and s.model_group in ('form-generator','form-generator-vision') and s.\"startTime\" >= '$START'")" 2

TKEY="sk-smoke-throwaway-$RANDOM$RANDOM"
admin() { curl -s -m 30 -X POST "$URL$1" -H "Authorization: Bearer $LITELLM_MASTER_KEY" -H 'Content-Type: application/json' -d "$2"; }
admin /key/delete '{"key_aliases":["smoke-throwaway"]}' >/dev/null
admin /key/generate "{\"key\":\"$TKEY\",\"key_alias\":\"smoke-throwaway\",\"models\":[\"form-generator\"],\"max_budget\":0.000001}" >/dev/null
check "throwaway key with a tiny budget answers its first call" "$(chat form-generator "$TKEY")" 200
sleep 20
code=$(chat form-generator "$TKEY")
if [ "$code" -ge 400 ] && [ "$code" -lt 500 ] && grep -qi budget /tmp/gw_body; then ok "spent key is refused with a 4xx naming the budget ($code)"; else bad "spent key expected 4xx naming the budget, got $code"; fi
admin /key/delete '{"key_aliases":["smoke-throwaway"]}' >/dev/null

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

echo "Retries and timeouts (retries spec P1 AC 1-6)"
check "config sets a 35 s attempt timeout" "$(grep -cE '^\s*timeout:\s*35\s*$' docker/litellm/config.yaml)" 1
check "provider SDK retries are off on all four deployments" "$(grep -cE '^\s*max_retries:\s*0\s*$' docker/litellm/config.yaml)" 4
check "config sets num_retries 0 (failover is the only second attempt)" "$(grep -cE '^\s*num_retries:\s*0\s*$' docker/litellm/config.yaml)" 1
check "no other file configures gateway retries" "$(git grep -lE 'num_retries|retry_policy' -- . ':!docker/litellm/config.yaml' ':!docker/litellm/README.md' ':!docker/litellm/smoke.sh' ':!.specs' ':!docs' | wc -l | tr -d ' ')" 0
OVR=docker/litellm/.smoke-override.yml
BH=form-ai-blackhole
cleanup_gw() { docker rm -f $BH >/dev/null 2>&1; if [ -f $OVR ]; then rm -f $OVR; docker compose up -d --force-recreate litellm >/dev/null 2>&1; fi; }
trap cleanup_gw EXIT
wait_gw() { for i in $(seq 1 30); do [ "$(docker inspect -f '{{.State.Health.Status}}' form-ai-litellm)" = healthy ] && break; sleep 5; done; }
hang_provider() { # starts a server that accepts connections and never answers; gateway env lines follow as args
  docker rm -f $BH >/dev/null 2>&1
  docker run -d --rm --name $BH --network form-ai_default --entrypoint python "$(docker inspect -f '{{.Config.Image}}' form-ai-litellm)" -u -c "
import socket
s=socket.socket(); s.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1); s.bind(('0.0.0.0',9)); s.listen(50)
c=[]
while True:
    c.append(s.accept()[0]); print('conn', flush=True)
" >/dev/null
  { echo "services:"; echo "  litellm:"; echo "    environment:"; for e in "$@"; do echo "      $e: http://$BH:9"; done; } > $OVR
  docker compose -f docker-compose.yml -f $OVR up -d --force-recreate litellm >/dev/null 2>&1; wait_gw
}
timed_chat() { t0=$(date +%s); CODE=$(chat "$1" "$LITELLM_MASTER_KEY"); ELAPSED=$(( $(date +%s) - t0 )); }

hang_provider ANTHROPIC_API_BASE
timed_chat form-generator
check "stalled primary: fallback answers 200" "$CODE" 200
if [ "$ELAPSED" -lt 75 ]; then ok "stalled primary: answered in ${ELAPSED}s, under 75 s"; else bad "stalled primary took ${ELAPSED}s, expected under 75 s"; fi
sleep 20
check "stalled primary: answered by gpt-5.2" "$(sql "select model from \"LiteLLM_SpendLogs\" where model_group='form-generator' order by \"startTime\" desc limit 1")" "openai/gpt-5.2"
check "stalled primary: attempted once, not retried on itself" "$(docker logs $BH 2>&1 | grep -c conn)" 1

hang_provider ANTHROPIC_API_BASE OPENAI_API_BASE
timed_chat form-generator
if [ "$CODE" != 200 ] && [ "$CODE" != 000 ]; then ok "both providers stalled: error $CODE returned"; else bad "both providers stalled: expected an error status, got $CODE"; fi
if [ "$ELAPSED" -lt 75 ]; then ok "both providers stalled: error after ${ELAPSED}s, under 75 s"; else bad "both providers stalled: took ${ELAPSED}s, expected under 75 s"; fi
cleanup_gw; wait_gw

timed_chat_tokens() { t0=$(date +%s); CODE=$(curl -s -m 90 -o /tmp/gw_body -w '%{http_code}' "$URL/v1/chat/completions" -H "Authorization: Bearer $LITELLM_MASTER_KEY" -H 'Content-Type: application/json' -d '{"model":"form-generator","max_tokens":-5,"messages":[{"role":"user","content":"Say hi"}]}'); ELAPSED=$(( $(date +%s) - t0 )); }
timed_chat_tokens
if [ "$CODE" -ge 400 ] && [ "$CODE" -lt 500 ]; then ok "provider-rejected request returns 4xx ($CODE)"; else bad "provider-rejected request expected 4xx, got $CODE"; fi
if [ "$ELAPSED" -lt 75 ]; then ok "provider-rejected request answered in ${ELAPSED}s, under 75 s"; else bad "provider-rejected request took ${ELAPSED}s"; fi

echo; echo "passed=$PASS failed=$FAIL unverified=$UNVERIFIED"
[ "$FAIL" -eq 0 ]

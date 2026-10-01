#!/usr/bin/env bash
# Creates or updates the gateway key the API uses (alias form-ai-app): only the two model aliases,
# a monthly budget. Idempotent. Needs the gateway running and LITELLM_MASTER_KEY, LITELLM_APP_KEY
# in .env (LITELLM_APP_MAX_BUDGET_USD defaults to 10). Slice ai-gateway-app-key, GWK-01..03.
set -eu
cd "$(dirname "$0")/../.."
ENV_FILE="${ENV_FILE:-./.env}"
[ -f "$ENV_FILE" ] && { set -a; . "$ENV_FILE"; set +a; }

URL="${LITELLM_URL:-http://127.0.0.1:4000}"
ALIAS=form-ai-app

if [ -z "${LITELLM_APP_KEY:-}" ]; then
  echo "LITELLM_APP_KEY is not set in $ENV_FILE" >&2
  exit 2
fi
: "${LITELLM_MASTER_KEY:?set LITELLM_MASTER_KEY in .env}"

BUDGET="${LITELLM_APP_MAX_BUDGET_USD:-10}"
BODY=$(python3 - "$LITELLM_APP_KEY" "$ALIAS" "$BUDGET" <<'PY'
import json, sys
key, alias, budget = sys.argv[1:4]
print(json.dumps({"key": key, "key_alias": alias,
                  "models": ["form-generator", "form-generator-vision"],
                  "max_budget": float(budget), "budget_duration": "1mo"}))
PY
)

call() { curl -s -m 30 -o /dev/null -w '%{http_code}' -X POST "$URL$1" \
  -H "Authorization: Bearer $LITELLM_MASTER_KEY" -H 'Content-Type: application/json' -d "$BODY"; }

exists=$(curl -s -m 30 -o /dev/null -w '%{http_code}' "$URL/key/info" -G --data-urlencode "key=$LITELLM_APP_KEY" \
  -H "Authorization: Bearer $LITELLM_MASTER_KEY")
if [ "$exists" = 200 ]; then path=/key/update; verb=updated; else path=/key/generate; verb=created; fi

code=$(call "$path")
if [ "$code" != 200 ]; then echo "gateway answered $code on $path" >&2; exit 1; fi
echo "app key $verb (alias $ALIAS, budget ${BUDGET} USD per month)"

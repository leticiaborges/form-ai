#!/bin/sh
# Creates the form_ai_e2e database the Playwright suite runs against, with the same roles and
# grants as form_ai (form_ai_migrator owns it, form_ai_app reads and writes).
#
# Run from the repo root, with Docker Compose's postgres container up:
#   sh docker/postgres/create-e2e-db.sh
#
# Safe to re-run. It reuses the container's own environment (POSTGRES_USER, the two role
# passwords) and the same script Docker runs on first start, so nothing is read from .env here.
# It lives outside init/ on purpose: everything in init/ runs automatically on an empty volume.
set -e

# Git Bash on Windows rewrites arguments that look like Unix paths before docker sees them.
export MSYS_NO_PATHCONV=1

CONTAINER="${DB_CONTAINER:-form-ai-db}"
E2E_DB=form_ai_e2e

docker exec -i "$CONTAINER" sh -c 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d postgres' <<EOSQL
SELECT 'CREATE DATABASE $E2E_DB' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = '$E2E_DB') \gexec
EOSQL

docker exec -e POSTGRES_DB="$E2E_DB" "$CONTAINER" sh /docker-entrypoint-initdb.d/01-create-app-user.sh

echo "$E2E_DB is ready. Apply migrations to it as form_ai_migrator (see README)."

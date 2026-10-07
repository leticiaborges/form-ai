#!/bin/sh
# Creates the gateway's own role and database on the shared RDS instance:
#   litellm  owns the `litellm` database and runs LiteLLM's own schema migrations at startup.
# Idempotent: a re-run only re-syncs the password. Needs POSTGRES_USER (RDS master) and
# LITELLM_DB_PASSWORD; connects through PGHOST / PGPASSWORD / PGSSLMODE.
set -e

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
  -v litellm_password="$LITELLM_DB_PASSWORD" \
  -v admin_role="$POSTGRES_USER" <<-'EOSQL'
SELECT 'CREATE ROLE litellm NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION'
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'litellm') \gexec

ALTER ROLE litellm WITH LOGIN PASSWORD :'litellm_password';

-- RDS's master is not a superuser: it can only create a database owned by a role it can SET ROLE to.
GRANT litellm TO :"admin_role" WITH SET TRUE;

-- CREATE DATABASE cannot run inside a transaction block; \gexec runs it as its own statement.
SELECT 'CREATE DATABASE litellm OWNER litellm'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'litellm') \gexec

REVOKE ALL ON DATABASE litellm FROM PUBLIC;
EOSQL

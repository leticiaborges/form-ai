#!/bin/sh
# Creates the two application roles and the grants between them:
#   form_ai_migrator  owns the database and schema, runs DDL (migrations only)
#   form_ai_app       runtime role, DML only (the API instances)
#
# The same script runs locally (Docker init, CI) and in production (one-off ECS task against
# RDS), so it must work for a non-superuser $POSTGRES_USER too, and it is idempotent: a re-run
# only re-syncs the passwords and grants.
set -e

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v migrator_password="$MIGRATOR_DB_PASSWORD" \
  -v app_password="$APP_DB_PASSWORD" \
  -v db_name="$POSTGRES_DB" \
  -v admin_role="$POSTGRES_USER" <<-'EOSQL'
SELECT 'CREATE ROLE form_ai_migrator NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION'
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'form_ai_migrator') \gexec

SELECT 'CREATE ROLE form_ai_app NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION'
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'form_ai_app') \gexec

-- Attributes are set at creation only: RDS's master can't ALTER the SUPERUSER attribute, even to NOSUPERUSER.
ALTER ROLE form_ai_migrator WITH LOGIN PASSWORD :'migrator_password';
ALTER ROLE form_ai_app WITH LOGIN PASSWORD :'app_password';

-- RDS's master user is not a superuser: it can only hand ownership to a role it can SET ROLE to.
-- Harmless where $POSTGRES_USER is already a superuser.
GRANT form_ai_migrator TO :"admin_role" WITH SET TRUE;

ALTER DATABASE :"db_name" OWNER TO form_ai_migrator;
REVOKE ALL ON DATABASE :"db_name" FROM PUBLIC;
GRANT CONNECT ON DATABASE :"db_name" TO form_ai_app;

\c :db_name

ALTER SCHEMA public OWNER TO form_ai_migrator;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO form_ai_app;

-- Tables/sequences the migrator creates in future migrations are usable by the app automatically.
ALTER DEFAULT PRIVILEGES FOR ROLE form_ai_migrator IN SCHEMA public
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO form_ai_app;

ALTER DEFAULT PRIVILEGES FOR ROLE form_ai_migrator IN SCHEMA public
GRANT USAGE, SELECT ON SEQUENCES TO form_ai_app;
EOSQL

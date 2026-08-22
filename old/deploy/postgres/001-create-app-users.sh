#!/bin/sh
set -eu

: "${NOVELAGENT_APP_PASSWORD:?NOVELAGENT_APP_PASSWORD is required}"
: "${NOVELAGENT_WORKER_PASSWORD:?NOVELAGENT_WORKER_PASSWORD is required}"

psql --set=ON_ERROR_STOP=1 \
    --set=app_password="$NOVELAGENT_APP_PASSWORD" \
    --set=worker_password="$NOVELAGENT_WORKER_PASSWORD" <<'SQL'
SELECT format(
    'CREATE ROLE novelagent_app LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION',
    :'app_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'novelagent_app')
\gexec

SELECT format('ALTER ROLE novelagent_app PASSWORD %L', :'app_password')
\gexec

SELECT format(
    'CREATE ROLE novelagent_worker LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION',
    :'worker_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'novelagent_worker')
\gexec

SELECT format('ALTER ROLE novelagent_worker PASSWORD %L', :'worker_password')
\gexec

GRANT CONNECT ON DATABASE novelagent TO novelagent_app;
GRANT CONNECT ON DATABASE novelagent TO novelagent_worker;
GRANT USAGE ON SCHEMA public TO novelagent_app;
GRANT USAGE ON SCHEMA public TO novelagent_worker;

ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO novelagent_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO novelagent_app;
SQL

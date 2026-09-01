#!/bin/sh
# Export the backend's OpenAPI document to a version-controlled file.
#
# Why HTTP rather than a CLI: Swashbuckle's CLI requires a Startup class, and
# this host uses minimal hosting (top-level statements in Program.cs), so the
# CLI cannot load it. The document is therefore fetched from a running host.
#
# The host deliberately fails fast on missing configuration — connection
# strings, JWT secret, worker role identity, and a worker role login precheck
# against a real PostgreSQL. Those gates are security invariants, so exporting
# requires the database stack to be up:
#
#   docker compose -p tianming-agentic-novel-studio \
#     -f old/docker-compose.yml --env-file old/.env up -d postgres postgres-role-bootstrap qdrant redis
#
# Swagger is only mapped when ASPNETCORE_ENVIRONMENT=Development (Program.cs),
# which is why this script forces it.
#
# Usage (from repository root):
#   ./tianming-web/backend/Scripts/export-openapi.sh            # write openapi.json
#   ./tianming-web/backend/Scripts/export-openapi.sh --check    # verify, do not write
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
backend_root=$(CDPATH= cd -- "$script_dir/.." && pwd)
repo_root=$(CDPATH= cd -- "$backend_root/../.." && pwd)

CHECK=0
[ "${1:-}" = "--check" ] && CHECK=1

OUTPUT="$backend_root/openapi.json"
PORT="${OPENAPI_EXPORT_PORT:-5111}"
DB_HOST="${OPENAPI_EXPORT_DB_HOST:-novelagent-postgres.orb.local}"
ENV_FILE="${OPENAPI_EXPORT_ENV_FILE:-$repo_root/old/.env}"

if [ ! -f "$ENV_FILE" ]; then
  printf '%s\n' "Missing env file: $ENV_FILE" >&2
  printf '%s\n' "It must define NOVELAGENT_ADMIN_PASSWORD, NOVELAGENT_APP_PASSWORD," >&2
  printf '%s\n' "NOVELAGENT_WORKER_PASSWORD and NOVELAGENT_JWT_SECRET." >&2
  exit 1
fi

# shellcheck disable=SC1090
set -a; . "$ENV_FILE"; set +a

DOTNET_ROOT="$backend_root/.dotnet"
export DOTNET_ROOT
PATH="$DOTNET_ROOT:$PATH"
export PATH
if [ ! -x "$DOTNET_ROOT/dotnet" ]; then
  printf '%s\n' "Project-local .NET SDK is missing: $DOTNET_ROOT/dotnet" >&2
  exit 1
fi

dotnet build "$backend_root/Tianming.Web/NovelAgentWeb.csproj" --nologo -v q >/dev/null

APP_DIR="$backend_root/Tianming.Web/bin/Debug/net10.0"
# One private directory per run. Deriving sibling names by string concatenation
# (e.g. "$TMP_SPEC.norm") is not safe: mktemp only guarantees the name it
# returns is unique, so a leftover sibling from an earlier run can be read back
# and silently compared against, which reports drift that does not exist.
WORK=$(mktemp -d)
LOG="$WORK/host.log"
RAW="$WORK/raw.json"
CANDIDATE="$WORK/candidate.json"
cleanup() {
  if [ -n "${APP_PID:-}" ]; then
    kill "$APP_PID" 2>/dev/null || true
    # Reap quietly so the shell does not print "Terminated" for the host.
    wait "$APP_PID" 2>/dev/null || true
  fi
  rm -rf "$WORK"
}
trap cleanup EXIT

# Refuse to run if the port is taken. Otherwise curl would reach a process this
# script did not start and the exported document would describe someone else's
# build, which is indistinguishable from a correct export.
if command -v lsof >/dev/null 2>&1 && lsof -nP -iTCP:"$PORT" -sTCP:LISTEN >/dev/null 2>&1; then
  printf '%s\n' "Port $PORT is already in use; refusing to export." >&2
  printf '%s\n' "Stop that process, or set OPENAPI_EXPORT_PORT to a free port." >&2
  exit 1
fi

conn() { printf 'Host=%s;Port=5432;Database=novelagent;Username=%s;Password=%s' "$DB_HOST" "$1" "$2"; }

(
  cd "$APP_DIR"
  ASPNETCORE_URLS="http://127.0.0.1:$PORT" \
  ASPNETCORE_ENVIRONMENT=Development \
  ConnectionStrings__NovelAgentDb="$(conn novelagent_app "$NOVELAGENT_APP_PASSWORD")" \
  ConnectionStrings__NovelAgentMigrationDb="$(conn novelagent_admin "$NOVELAGENT_ADMIN_PASSWORD")" \
  ConnectionStrings__NovelAgentWorkerDb="$(conn novelagent_worker "$NOVELAGENT_WORKER_PASSWORD")" \
  JwtSettings__SecretKey="$NOVELAGENT_JWT_SECRET" \
  Qdrant__BaseUrl="http://novelagent-qdrant.orb.local:6333" \
  Qdrant__Host=novelagent-qdrant.orb.local \
  Qdrant__Port=6334 \
  Redis__Enabled=true \
  Redis__ConnectionString=novelagent-redis.orb.local:6379 \
  exec dotnet NovelAgentWeb.dll >"$LOG" 2>&1
) &
# exec above replaces the subshell with dotnet itself, so $! is the host's own
# PID. Without it, cleanup would kill the subshell and leave dotnet holding the
# port — a stale host then answers the next run's curl and the exported document
# silently reflects whatever code that old process was built from.
APP_PID=$!

i=0
while [ "$i" -lt 60 ]; do
  sleep 2
  if curl -sf "http://127.0.0.1:$PORT/swagger/v1/swagger.json" -o "$RAW" 2>/dev/null; then
    break
  fi
  if ! kill -0 "$APP_PID" 2>/dev/null; then
    printf '%s\n' "Host exited before serving the document:" >&2
    tail -20 "$LOG" >&2
    exit 1
  fi
  i=$((i + 1))
done

if [ ! -s "$RAW" ]; then
  printf '%s\n' "Timed out fetching the OpenAPI document." >&2
  tail -20 "$LOG" >&2
  exit 1
fi

# Normalise so the committed artifact is stable across runs and readable in diffs.
python3 -c "
import json,sys
with open(sys.argv[1]) as f: doc=json.load(f)
with open(sys.argv[2],'w') as f:
    json.dump(doc,f,indent=2,sort_keys=True,ensure_ascii=False)
    f.write('\n')
" "$RAW" "$CANDIDATE"

if [ "$CHECK" -eq 1 ]; then
  if [ ! -f "$OUTPUT" ]; then
    printf '%s\n' "$OUTPUT does not exist. Run without --check first." >&2
    exit 1
  fi
  if cmp -s "$CANDIDATE" "$OUTPUT"; then
    printf '%s\n' "openapi.json is up to date."
  else
    printf '%s\n' "openapi.json is out of date with the backend contract." >&2
    printf '%s\n' "Regenerate with: ./tianming-web/backend/Scripts/export-openapi.sh" >&2
    diff -u "$OUTPUT" "$CANDIDATE" | head -40 >&2 || true
    exit 1
  fi
else
  cp "$CANDIDATE" "$OUTPUT"
  printf '%s\n' "Wrote $OUTPUT"
  python3 -c "
import json,sys
with open(sys.argv[1]) as f: d=json.load(f)
print('  paths:',len(d.get('paths',{})),'schemas:',len(d.get('components',{}).get('schemas',{})))
" "$OUTPUT"
fi

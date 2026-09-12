#!/usr/bin/env bash
# Backup all runtime data of the Tianming deployment into the private
# GitHub repository lyston11/tianming-backups and push it.
#
# Collected artifacts:
#   - dumps/postgresql-*.sql.gz      full pg_dump of the novelagent database
#   - qdrant/<collection>-*.snap     snapshot per Qdrant collection
#   - app-data/App_Data-*.tar.gz     backend App_Data storage root
#   - secrets/old.env                deploy secrets (chmod 600, private repo)
#
# Retention: artifacts older than 30 days are removed before committing.
# Designed to run unattended via the tianming-db-backup systemd timer.
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BACKUP_DIR="${TIANMING_BACKUP_DIR:-$HOME/tianming-backups}"
RETENTION_DAYS=30
STAMP="$(date '+%Y%m%d-%H%M%S')"
LOG_TAG="[tianming-backup]"

mkdir -p "$BACKUP_DIR"
cd "$BACKUP_DIR"

if [ ! -d .git ]; then
    git clone git@github.com:lyston11/tianming-backups.git "$BACKUP_DIR"
fi
if git rev-parse --verify -q HEAD >/dev/null; then
    git pull --rebase --autostash origin main
fi

mkdir -p dumps qdrant app-data secrets

# 1. PostgreSQL full dump
docker exec novelagent-postgres pg_dump -U novelagent_admin -d novelagent \
    | gzip > "dumps/postgresql-$STAMP.sql.gz"
echo "$LOG_TAG postgresql dump: dumps/postgresql-$STAMP.sql.gz"

# 2. Qdrant collection snapshots (empty list is fine)
collections="$(curl -fsS -m 10 http://127.0.0.1:6333/collections \
    | sed -n 's/.*"collections":\[[^]]*"name":"\([^"]*\)".*/\1/p' || true)"
if [ -n "${collections:-}" ]; then
    for collection in $collections; do
        curl -fsS -X POST "http://127.0.0.1:6333/collections/$collection/snapshots" >/dev/null
        snap_name="$(curl -fsS "http://127.0.0.1:6333/collections/$collection/snapshots" \
            | sed -n 's/.*"name":"\([^"]*\)".*/\1/p' | head -1)"
        curl -fsS -o "qdrant/${collection}-$STAMP.snap" \
            "http://127.0.0.1:6333/collections/$collection/snapshots/$snap_name"
        curl -fsS -X DELETE "http://127.0.0.1:6333/collections/$collection/snapshots/$snap_name" >/dev/null
        echo "$LOG_TAG qdrant snapshot: qdrant/${collection}-$STAMP.snap"
    done
else
    echo "$LOG_TAG qdrant: no collections, skipped"
fi

# 3. Backend App_Data storage root
APP_DATA="$REPO_ROOT/tianming-web/backend/Tianming.Web/App_Data"
if [ -d "$APP_DATA" ]; then
    tar -czf "app-data/App_Data-$STAMP.tar.gz" -C "$(dirname "$APP_DATA")" "$(basename "$APP_DATA")"
    echo "$LOG_TAG App_Data archive: app-data/App_Data-$STAMP.tar.gz"
fi

# 4. Deploy secrets (private repo; remove these two lines if undesired)
if [ -f "$REPO_ROOT/old/.env" ]; then
    install -m 600 "$REPO_ROOT/old/.env" "secrets/old.env"
    echo "$LOG_TAG secrets: secrets/old.env"
fi

# 5. Retention
find dumps qdrant app-data -type f -mtime +$RETENTION_DAYS -delete 2>/dev/null || true

# 6. Commit and push (skip when nothing changed)
if [ -z "$(git status --porcelain)" ]; then
    echo "$LOG_TAG nothing new to back up"
    exit 0
fi
git add -A
git commit -m "backup: $(date '+%F %T')"
git push origin main
echo "$LOG_TAG pushed backup to GitHub"

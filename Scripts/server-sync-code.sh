#!/usr/bin/env bash
# Auto-sync the server checkout to GitHub: commit pending changes, rebase, push.
# Designed to run unattended via the tianming-code-sync systemd timer.
set -euo pipefail
cd "$(dirname "$0")/.."

LOG_TAG="[tianming-code-sync]"

if [ -z "$(git status --porcelain)" ]; then
    echo "$LOG_TAG no local changes"
else
    git add -A
    git commit -m "chore(auto): server code sync $(date '+%F %T')"
    echo "$LOG_TAG committed pending changes"
fi

git pull --rebase --autostash origin main
git push origin main
echo "$LOG_TAG pushed to GitHub"

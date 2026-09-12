#!/usr/bin/env bash
# One-shot installer for unattended automation on the server:
#   - tianming-code-sync.service/.timer : commit+push code every 30 minutes
#   - tianming-db-backup.service/.timer : backup db/vectors/app-data every hour
# Units are systemd user units; enable login linger if not already done.
set -euo pipefail
UNIT_DIR="$HOME/.config/systemd/user"
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"

chmod +x "$REPO_ROOT/Scripts/server-sync-code.sh" "$REPO_ROOT/Scripts/server-backup.sh"

# git identity for unattended commits
git -C "$REPO_ROOT" config user.name  >/dev/null 2>&1 || \
    git -C "$REPO_ROOT" config user.name "lyston11"
git -C "$REPO_ROOT" config user.email >/dev/null 2>&1 || \
    git -C "$REPO_ROOT" config user.email "lyston11@users.noreply.github.com"

mkdir -p "$UNIT_DIR"

write_unit() { # name, on-calendar, exec
    local name="$1" calendar="$2" exec="$3"
    cat > "$UNIT_DIR/$name.service" <<EOF
[Unit]
Description=Tianming $name (oneshot)

[Service]
Type=oneshot
ExecStart=$exec
EOF
    cat > "$UNIT_DIR/$name.timer" <<EOF
[Unit]
Description=Tianming $name schedule

[Timer]
OnCalendar=$calendar
Persistent=true
RandomizedDelaySec=120

[Install]
WantedBy=timers.target
EOF
}

write_unit "tianming-code-sync" "*:0/30" "$REPO_ROOT/Scripts/server-sync-code.sh"
write_unit "tianming-db-backup" "hourly" "$REPO_ROOT/Scripts/server-backup.sh"

systemctl --user daemon-reload
systemctl --user enable --now tianming-code-sync.timer tianming-db-backup.timer

if ! loginctl show-user "$USER" -p Linger | grep -q yes; then
    sudo -n loginctl enable-linger "$USER" || \
        echo "WARNING: could not enable linger; timers stop at logout"
fi

echo "installed. status:"
systemctl --user list-timers 'tianming-*' --no-pager

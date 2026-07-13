#!/usr/bin/env bash
#
# check-backups.sh — monitor that DB backups (and the monthly test-restore) are FRESH.
#
# Silent backup failure is the real disaster: you only notice when you need a restore and the
# newest good one is weeks old. This watches the heartbeats the backup + test-restore write and
# alerts if either goes stale:
#   - nightly backup  : /var/backups/ojunai-db/.last-success          stale if > 26h
#   - monthly restore : /var/backups/ojunai-db/.last-restore-success  stale if > 40d (once it exists)
#
# Alert delivery (any/all that apply):
#   - node_exporter textfile metric (native Grafana path) — written if the collector dir exists
#   - ALERT_WEBHOOK POST (Slack/Discord/generic) — if set in /etc/ojunai/backup.env
#   - non-zero exit + loud stdout — so a cron MAILTO also catches it
#
# Run as root via cron.
set -uo pipefail

ENV_FILE="${OJUNAI_BACKUP_ENV:-/etc/ojunai/backup.env}"
[ -f "$ENV_FILE" ] && { set -a; . "$ENV_FILE"; set +a; }

DIR="${LOCAL_DIR:-/var/backups/ojunai-db}"
BACKUP_HB="$DIR/.last-success"
RESTORE_HB="$DIR/.last-restore-success"
BACKUP_MAX_H="${BACKUP_MAX_HOURS:-26}"
RESTORE_MAX_D="${RESTORE_MAX_DAYS:-40}"
TEXTFILE_DIR="${TEXTFILE_DIR:-/var/lib/node_exporter/textfile_collector}"
NOW=$(date +%s)

# File mtime in epoch seconds — GNU stat (Linux/prod) with a BSD fallback for portability.
mtime() { stat -c %Y "$1" 2>/dev/null || stat -f %m "$1" 2>/dev/null; }
age_secs() { if [ -f "$1" ]; then local m; m=$(mtime "$1"); echo $(( NOW - m )); else echo -1; fi; }

b_age=$(age_secs "$BACKUP_HB")
r_age=$(age_secs "$RESTORE_HB")

problems=()
if [ "$b_age" -lt 0 ]; then
  problems+=("no backup heartbeat (${BACKUP_HB} missing)")
elif [ "$b_age" -gt $(( BACKUP_MAX_H * 3600 )) ]; then
  problems+=("backup STALE: $(( b_age / 3600 ))h old (max ${BACKUP_MAX_H}h)")
fi
# The restore heartbeat only exists after the first monthly test-restore — warn only if stale.
if [ "$r_age" -ge 0 ] && [ "$r_age" -gt $(( RESTORE_MAX_D * 86400 )) ]; then
  problems+=("test-restore STALE: $(( r_age / 86400 ))d old (max ${RESTORE_MAX_D}d)")
fi

# node_exporter textfile metric (best-effort; native Grafana/Prometheus alerting path).
if [ -d "$TEXTFILE_DIR" ]; then
  {
    echo "# HELP ojunai_backup_last_success_age_seconds Age of the newest successful DB backup (-1 = missing)."
    echo "# TYPE ojunai_backup_last_success_age_seconds gauge"
    echo "ojunai_backup_last_success_age_seconds ${b_age}"
    echo "# HELP ojunai_restore_test_last_success_age_seconds Age of the last passing test-restore (-1 = never)."
    echo "# TYPE ojunai_restore_test_last_success_age_seconds gauge"
    echo "ojunai_restore_test_last_success_age_seconds ${r_age}"
  } > "${TEXTFILE_DIR}/ojunai_backup.prom.tmp" 2>/dev/null \
    && mv "${TEXTFILE_DIR}/ojunai_backup.prom.tmp" "${TEXTFILE_DIR}/ojunai_backup.prom" 2>/dev/null || true
fi

if [ ${#problems[@]} -eq 0 ]; then
  restore_note=""
  [ "$r_age" -ge 0 ] && restore_note=", test-restore $(( r_age / 86400 ))d old"
  echo "[$(date -u +%FT%TZ)] OK: backup $(( b_age / 3600 ))h old${restore_note}"
  exit 0
fi

MSG="Ojunai backup check FAILED on $(hostname): $(IFS=';'; echo "${problems[*]}")"
echo "[$(date -u +%FT%TZ)] $MSG"
if [ -n "${ALERT_WEBHOOK:-}" ]; then
  # "text" (Slack) + "content" (Discord) so one payload works with either.
  curl -fsS -m 10 -H 'Content-Type: application/json' \
    -d "{\"text\":\"🔴 ${MSG}\",\"content\":\"🔴 ${MSG}\"}" "$ALERT_WEBHOOK" >/dev/null 2>&1 \
    || echo "(alert webhook POST failed)"
fi
exit 1

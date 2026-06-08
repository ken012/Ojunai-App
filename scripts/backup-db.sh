#!/usr/bin/env bash
#
# backup-db.sh — nightly logical backup of the Ojunai production database.
#
# Creates a compressed pg_dump of ojunai_prod (+ role globals) and uploads it OFF-BOX to
# S3-compatible object storage. A dump that only lives on the same server is NOT a backup —
# it dies with the box — so the off-box upload is REQUIRED and the script fails if it can't
# upload. See docs/disaster-recovery.md for install + restore instructions.
#
# Run as ROOT (root cron) so `sudo -u postgres` and reads of /etc/ojunai/backup.env work
# without a password prompt.
#
set -euo pipefail

# cron runs with a minimal PATH that omits /usr/local/bin — make the AWS CLI (installed there by
# the v2 bundle) and the postgres client tools resolvable regardless of how this is invoked.
export PATH="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"

# ── Config (override in /etc/ojunai/backup.env, chmod 600) ────────────────────
ENV_FILE="${OJUNAI_BACKUP_ENV:-/etc/ojunai/backup.env}"
if [ -f "$ENV_FILE" ]; then set -a; . "$ENV_FILE"; set +a; fi

DB_NAME="${DB_NAME:-ojunai_prod}"
LOCAL_DIR="${LOCAL_DIR:-/var/backups/ojunai-db}"
LOCAL_RETENTION_DAYS="${LOCAL_RETENTION_DAYS:-7}"        # off-box retention via bucket lifecycle
S3_PREFIX="${S3_PREFIX:-ojunai-db}"
S3_ENDPOINT="${S3_ENDPOINT:-}"                            # set for Backblaze B2 / Cloudflare R2 / DO Spaces; empty = AWS S3
HEARTBEAT="${HEARTBEAT:-${LOCAL_DIR}/.last-success}"
: "${S3_BUCKET:?S3_BUCKET not set — off-box storage is REQUIRED. Set it in ${ENV_FILE}}"

log() { echo "[$(date -u +%FT%TZ)] $*"; }

TS="$(date -u +%Y%m%dT%H%M%SZ)"
DUMP="${LOCAL_DIR}/ojunai_prod-${TS}.dump"
GLOBALS="${LOCAL_DIR}/ojunai_globals-${TS}.sql"

mkdir -p "$LOCAL_DIR"
chmod 700 "$LOCAL_DIR"

log "backup start: db=${DB_NAME}"

# ── 1. Dump. Custom format (-Fc) = compressed + supports selective/parallel restore. ──
#    --no-owner/--no-privileges so it restores cleanly onto a box with a different role setup.
#    pg_dump runs as the `postgres` user but STREAMS to stdout; the redirect is performed by this
#    (root) script, so the file is created/owned by root in the root-only backup dir — postgres
#    never needs write access there.
sudo -u postgres pg_dump -Fc --no-owner --no-privileges "$DB_NAME" > "$DUMP"
chmod 600 "$DUMP"

# Role definitions (so a bare-metal restore can recreate the app's DB role).
sudo -u postgres pg_dumpall --globals-only > "$GLOBALS"
chmod 600 "$GLOBALS"

# Integrity check: a valid custom-format archive must list its table of contents.
# Run as root (reads the root-owned dump file); pg_restore --list needs no DB access.
pg_restore --list "$DUMP" > /dev/null
log "dump OK: $(basename "$DUMP") ($(du -h "$DUMP" | cut -f1)) + globals"

# ── 2. Upload off-box (REQUIRED — failure aborts the script non-zero). ──
AWS_ARGS=()
[ -n "$S3_ENDPOINT" ] && AWS_ARGS+=(--endpoint-url "$S3_ENDPOINT")
aws "${AWS_ARGS[@]}" s3 cp "$DUMP"    "s3://${S3_BUCKET}/${S3_PREFIX}/${TS}/$(basename "$DUMP")"    --only-show-errors
aws "${AWS_ARGS[@]}" s3 cp "$GLOBALS" "s3://${S3_BUCKET}/${S3_PREFIX}/${TS}/$(basename "$GLOBALS")" --only-show-errors
log "uploaded -> s3://${S3_BUCKET}/${S3_PREFIX}/${TS}/"

# ── 3. Prune old LOCAL copies (the off-box bucket keeps the durable history). ──
find "$LOCAL_DIR" -name 'ojunai_prod-*.dump'   -mtime "+${LOCAL_RETENTION_DAYS}" -delete
find "$LOCAL_DIR" -name 'ojunai_globals-*.sql' -mtime "+${LOCAL_RETENTION_DAYS}" -delete

# ── 4. Heartbeat — monitor this file's age to detect a missed/failed backup. ──
date -u +%FT%TZ > "$HEARTBEAT"
log "backup complete"

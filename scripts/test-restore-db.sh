#!/usr/bin/env bash
#
# test-restore-db.sh — prove the newest nightly backup actually restores.
#
# Restores the most recent local pg_dump into a THROWAWAY database, checks that real
# rows came back, then drops the scratch DB. Production (`ojunai_prod`) is never touched.
# An untested backup is not a backup — run this at least monthly (or via cron).
#
# Run as root (needs `sudo -u postgres` + read of the root-only backup dir):
#     sudo bash /tmp/test-restore-db.sh
#   or, once installed:
#     sudo /usr/local/bin/ojunai-test-restore.sh
#
set -uo pipefail

BACKUP_DIR="${BACKUP_DIR:-/var/backups/ojunai-db}"
SCRATCH="ojunai_restore_test"
TMP="/tmp/ojunai-restore-test.dump"

# Always clean up the scratch DB + temp dump, even on failure or early exit.
cleanup() {
  sudo -u postgres dropdb --if-exists "$SCRATCH" >/dev/null 2>&1 || true
  rm -f "$TMP" /tmp/rt.dump 2>/dev/null || true   # also clears a stray temp from earlier manual runs
}
trap cleanup EXIT

fail() { echo ">> FAIL: $*"; exit 1; }

[ "$(id -u)" -eq 0 ] || fail "must run as root (use: sudo bash $0)"

DUMP=$(ls -1t "$BACKUP_DIR"/ojunai_prod-*.dump 2>/dev/null | head -1)
[ -n "$DUMP" ] || fail "no dump found in $BACKUP_DIR — check /var/log/ojunai-backup.log"
echo ">> Using dump: $DUMP ($(du -h "$DUMP" | cut -f1))"

# The nightly dumps are root:root 600; copy to a postgres-owned temp file so pg_restore can read it.
install -o postgres -g postgres -m 600 "$DUMP" "$TMP" || fail "could not stage dump to $TMP"

echo ">> Verifying archive integrity (table of contents)..."
sudo -u postgres pg_restore --list "$TMP" >/dev/null || fail "archive is corrupt / unreadable"
echo "   TOC OK"

echo ">> Restoring into scratch DB '$SCRATCH' (prod untouched)..."
sudo -u postgres dropdb --if-exists "$SCRATCH"
sudo -u postgres createdb "$SCRATCH"
sudo -u postgres pg_restore --no-owner --no-privileges -d "$SCRATCH" "$TMP" || fail "pg_restore errored"
echo "   restore OK"

echo ">> Row counts in the restored copy:"
sudo -u postgres psql "$SCRATCH" -c 'SELECT
  (SELECT count(*) FROM "Businesses") AS businesses,
  (SELECT count(*) FROM "Users")      AS users,
  (SELECT count(*) FROM "Sales")      AS sales,
  (SELECT count(*) FROM "Products")   AS products;' || fail "could not query restored DB"

echo ">> PASS — backup restores cleanly. Scratch DB + temp file cleaned up on exit."

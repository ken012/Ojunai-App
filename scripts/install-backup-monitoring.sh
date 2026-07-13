#!/usr/bin/env bash
#
# install-backup-monitoring.sh — install the backup-freshness monitor + monthly test-restore cron.
#
# Run as root, with check-backups.sh and test-restore-db.sh sitting next to it (e.g. all three
# scp'd to /tmp):
#     sudo bash /tmp/install-backup-monitoring.sh
#
set -euo pipefail
[ "$(id -u)" -eq 0 ] || { echo "run as root: sudo bash $0"; exit 1; }
SRC="$(cd "$(dirname "$0")" && pwd)"

for f in check-backups.sh test-restore-db.sh; do
  [ -f "$SRC/$f" ] || { echo "missing $SRC/$f — scp it next to this installer first"; exit 1; }
done

install -m 750 -o root -g root "$SRC/check-backups.sh"  /usr/local/bin/ojunai-check-backups.sh
install -m 750 -o root -g root "$SRC/test-restore-db.sh" /usr/local/bin/ojunai-test-restore.sh
echo ">> installed /usr/local/bin/ojunai-check-backups.sh + ojunai-test-restore.sh"

cat > /etc/cron.d/ojunai-backup-monitor <<'CRON'
# Ojunai backup monitoring. System crontab format: needs the user field + explicit PATH
# (cron's minimal PATH omits /usr/local/bin where the aws/pg tools live).
PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
# Freshness check every 6 hours (alerts if the nightly backup goes stale).
17 */6 * * * root /usr/local/bin/ojunai-check-backups.sh >> /var/log/ojunai-backup-monitor.log 2>&1
# Monthly test-restore — 1st at 02:15 UTC, an hour after the nightly backup.
15 2 1 * * root /usr/local/bin/ojunai-test-restore.sh    >> /var/log/ojunai-backup-monitor.log 2>&1
CRON
chmod 644 /etc/cron.d/ojunai-backup-monitor
echo ">> installed /etc/cron.d/ojunai-backup-monitor (check every 6h; test-restore monthly)"

echo ">> running one freshness check now:"
/usr/local/bin/ojunai-check-backups.sh || true
echo ">> done. Log: /var/log/ojunai-backup-monitor.log"

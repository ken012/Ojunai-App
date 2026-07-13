# Disaster recovery — database backups

The Ojunai prod database (`ojunai_prod`) holds every merchant's books, balances, and payment
records. A logical backup runs nightly via `scripts/backup-db.sh` and is stored **off the
production box** — because a dump on the same server dies with the server.

## How the off-box copy works

The box has no special "remote disk." The backup leaves the server the same way any file does —
over **HTTPS to an object-storage bucket** hosted by a different provider:

1. `pg_dump -Fc ojunai_prod` writes a compressed dump to `/var/backups/ojunai-db/` (local staging).
2. The script runs **`aws s3 cp <dump> s3://<bucket>/…`**, which uploads that file to an
   **S3-compatible** bucket living on entirely separate infrastructure (AWS, Backblaze, Cloudflare…).
3. If the VPS is lost, the bucket is untouched — you restore from it onto a new box.

"S3-compatible" means the same `aws s3` command works with any of these — you just point it at the
provider's endpoint via `S3_ENDPOINT`:

| Provider | Why | `S3_ENDPOINT` |
|---|---|---|
| **Cloudflare R2** | cheap, **no egress fees**, S3 API | `https://<account>.r2.cloudflarestorage.com` |
| **Backblaze B2** | cheapest storage, S3 API | `https://s3.<region>.backblazeb2.com` |
| **AWS S3** | ubiquitous | *(leave empty)* |
| **DigitalOcean Spaces** | if you're already on DO | `https://<region>.digitaloceanspaces.com` |

Credentials are API keys (access key + secret) the provider issues for the bucket; they live in
`/etc/ojunai/backup.env` on the box, never in git.

> Not into object storage? The same script shape works with `rsync`/`scp` to a **second server**,
> or `rclone` (Google Drive, Dropbox, etc.). Object storage is recommended: cheapest, durable,
> nothing else to run. The one rule: the copy must live somewhere the prod box's failure can't take down.

## One-time host setup

```bash
# 1. Install the AWS CLI (works with all S3-compatible providers)
sudo apt-get update && sudo apt-get install -y awscli      # or: pipx install awscli

# 2. Put the script on the box
sudo install -m 750 -o root -g root scripts/backup-db.sh /usr/local/bin/ojunai-backup-db.sh

# 3. Create the (private) config + credentials
sudo mkdir -p /etc/ojunai
sudo tee /etc/ojunai/backup.env >/dev/null <<'ENV'
S3_BUCKET=ojunai-prod-backups
S3_PREFIX=ojunai-db
# For B2/R2/Spaces set the endpoint; leave unset/empty for AWS S3:
S3_ENDPOINT=https://<account>.r2.cloudflarestorage.com
AWS_ACCESS_KEY_ID=xxxxxxxx
AWS_SECRET_ACCESS_KEY=xxxxxxxx
AWS_DEFAULT_REGION=auto
LOCAL_RETENTION_DAYS=7
ENV
sudo chmod 600 /etc/ojunai/backup.env

# 4. Schedule it nightly (root cron) — 01:15 UTC.
#    NOTE: /etc/cron.d/ files use the SYSTEM crontab format — they require a username field
#    ("root") and run with a minimal PATH (so we set PATH so `aws` in /usr/local/bin is found).
printf '%s\n' \
'PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin' \
'15 1 * * * root /usr/local/bin/ojunai-backup-db.sh >> /var/log/ojunai-backup.log 2>&1' \
| sudo tee /etc/cron.d/ojunai-backup >/dev/null
sudo chmod 644 /etc/cron.d/ojunai-backup

# 5. Run it once now to verify end-to-end
sudo /usr/local/bin/ojunai-backup-db.sh
```

### Bucket hardening (do this in the provider console)
- **Private** bucket (no public access) — it contains PII + financial data.
- **Server-side encryption** at rest (R2/B2 encrypt by default; enable SSE on S3).
- **Object lifecycle**: keep e.g. 30 daily + 12 monthly, expire the rest (this is your off-box retention).
- **Object lock / versioning** if available — protects against an attacker (or bad script) deleting backups.
- A **separate** credential scoped to only this bucket — not your main cloud account keys.

## Monitoring + automated test-restore (installed)

Two extra cron jobs guard the backups (installed via `scripts/install-backup-monitoring.sh` →
`/etc/cron.d/ojunai-backup-monitor`, log `/var/log/ojunai-backup-monitor.log`):

1. **Freshness check — every 6h** (`/usr/local/bin/ojunai-check-backups.sh`). Alerts if the
   nightly backup heartbeat `.last-success` is older than **26h**, or the test-restore heartbeat
   `.last-restore-success` is older than **40d**.
2. **Test-restore — monthly** (1st, 02:15 UTC, `/usr/local/bin/ojunai-test-restore.sh`). Restores
   the newest dump into a throwaway DB, checks row counts, drops it, and writes
   `.last-restore-success` on pass. Never touches prod.

**Wire the alert delivery (the check detects; you choose how it pages):**
- **Grafana/Prometheus:** the check writes `ojunai_backup_last_success_age_seconds` (and
  `..._restore_test_...`) to `/var/lib/node_exporter/textfile_collector/ojunai_backup.prom` when
  that collector dir exists. Alert when it exceeds `93600` (26h) or goes absent.
- **Slack/Discord:** set `ALERT_WEBHOOK=<incoming-webhook-url>` in `/etc/ojunai/backup.env` — the
  check POSTs the failure there.
- Either way the check exits non-zero, so a cron `MAILTO` also catches it if mail is configured.

To reinstall/update after editing the scripts: `scp` the three `scripts/*.sh` to `/tmp` and re-run
`sudo bash /tmp/install-backup-monitoring.sh`.

## Restore procedure

> **An untested backup is not a backup.** Do the test-restore (below) at least monthly.

Fetch a dump from the bucket:
```bash
aws [--endpoint-url <S3_ENDPOINT>] s3 ls s3://<bucket>/ojunai-db/        # list timestamps
aws [--endpoint-url <S3_ENDPOINT>] s3 cp s3://<bucket>/ojunai-db/<TS>/ojunai_prod-<TS>.dump .
```

**Test-restore (safe — restores into a scratch DB, never touches prod):**
```bash
sudo -u postgres createdb ojunai_restore_test
sudo -u postgres pg_restore --no-owner --no-privileges -d ojunai_restore_test ojunai_prod-<TS>.dump
# sanity-check a few tables:
sudo -u postgres psql ojunai_restore_test -c 'SELECT count(*) FROM "Businesses", "Sales", "BillingEvents";'
sudo -u postgres dropdb ojunai_restore_test     # clean up
```

**Real recovery (prod is gone / corrupted)** — on a fresh box:
```bash
sudo -u postgres psql -f ojunai_globals-<TS>.sql                       # recreate roles
sudo -u postgres createdb ojunai_prod
sudo -u postgres pg_restore --no-owner --no-privileges -d ojunai_prod ojunai_prod-<TS>.dump
# then point the API's connection string at it and restart ojunai-api
```

## Limitation: this is daily snapshots, not point-in-time

`pg_dump` gives you last-night's state — you could lose up to ~24h on a hard failure. That's an
acceptable starting point pre-launch. If/when transaction volume makes a 24h loss unacceptable,
enable **WAL archiving / PITR** (`archive_mode = on` + a tool like pgBackRest or wal-g) for
recovery to any point in time. Until then, nightly logical dumps off-box are the priority.

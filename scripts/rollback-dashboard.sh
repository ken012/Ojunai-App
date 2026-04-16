#!/usr/bin/env bash
# Roll the BizPilot dashboard back to a previous .next backup.
# Usage:  ./rollback-dashboard.sh
set -e

SERVER="bizpilot@46.225.108.35"
REMOTE_DIR="/var/www/bizpilot-dashboard"
BACKUPS_DIR="/var/www/bizpilot-dashboard-backups"

echo "Available dashboard backups (newest first):"
ssh -t "$SERVER" "sudo ls -1t $BACKUPS_DIR 2>/dev/null | grep '^dashboard-' | head -5 | nl"

echo ""
read -p "Enter the number to rollback to (or Ctrl+C to cancel): " CHOICE

BACKUP_NAME=$(ssh -t "$SERVER" "sudo ls -1t $BACKUPS_DIR | grep '^dashboard-' | sed -n ${CHOICE}p")

if [ -z "$BACKUP_NAME" ]; then
  echo "❌ Invalid choice"
  exit 1
fi

echo "⏪ Rolling back .next to $BACKUP_NAME..."
ssh -t "$SERVER" "
  sudo rm -rf $REMOTE_DIR/.next
  sudo cp -r $BACKUPS_DIR/$BACKUP_NAME $REMOTE_DIR/.next
  pm2 restart bizpilot-dashboard
  sleep 3
  curl -fs http://localhost:3000 > /dev/null
"

echo ""
echo "✅ Dashboard rolled back to $BACKUP_NAME"

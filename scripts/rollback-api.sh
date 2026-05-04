#!/usr/bin/env bash
# Roll the Ojunai API back to a previous backup.
# Usage:  ./rollback-api.sh
set -e

SERVER="ojunai@46.225.108.35"
REMOTE_DIR="/var/www/ojunai-api"
BACKUPS_DIR="/var/www/ojunai-api-backups"

echo "Available API backups (newest first):"
ssh -t "$SERVER" "sudo ls -1t $BACKUPS_DIR 2>/dev/null | grep '^api-' | head -5 | nl"

echo ""
read -p "Enter the number to rollback to (or Ctrl+C to cancel): " CHOICE

BACKUP_NAME=$(ssh -t "$SERVER" "sudo ls -1t $BACKUPS_DIR | grep '^api-' | sed -n ${CHOICE}p")

if [ -z "$BACKUP_NAME" ]; then
  echo "❌ Invalid choice"
  exit 1
fi

echo "⏪ Rolling back to $BACKUP_NAME..."
ssh -t "$SERVER" "
  sudo rm -rf $REMOTE_DIR
  sudo cp -r $BACKUPS_DIR/$BACKUP_NAME $REMOTE_DIR
  sudo systemctl restart ojunai-api
  sleep 3
  curl -fs http://localhost:5000/health
"

echo ""
echo "✅ Rolled back to $BACKUP_NAME"

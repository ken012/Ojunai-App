#!/usr/bin/env bash
# Deploy the Ojunai .NET API to production.
# Usage:  ./deploy-api.sh
set -e

SERVER="bizpilot@46.225.108.35"
REMOTE_DIR="/var/www/ojunai-api"
BACKUPS_DIR="/var/www/ojunai-api-backups"
LOCAL_DIR="$HOME/Desktop/Ojunai-AI/Ojunai.API"

echo "🔨 Building API locally..."
cd "$LOCAL_DIR"
rm -rf ./publish
dotnet publish -c Release -o ./publish

# Strip any dev-only config files so they never land on the server
rm -f ./publish/appsettings.Development.json

echo "📦 Backing up current build on server..."
ssh -t "$SERVER" "
  sudo mkdir -p $BACKUPS_DIR
  TIMESTAMP=\$(date +%Y%m%d-%H%M%S)
  sudo cp -r $REMOTE_DIR $BACKUPS_DIR/api-\$TIMESTAMP
  # keep only last 5 backups
  sudo ls -1t $BACKUPS_DIR | grep '^api-' | tail -n +6 | xargs -I {} sudo rm -rf $BACKUPS_DIR/{}
  echo \"Saved backup: api-\$TIMESTAMP\"
"

echo "⬆️  Uploading new build..."
scp -r ./publish/* "$SERVER:$REMOTE_DIR/"

echo "🔄 Restarting service..."
# Restart, then poll /health for up to 30 seconds so we wait through migrations and Hangfire startup
# before declaring the deploy done. Without this, a slow cold start would fail a single 3-second health
# check and the script would abort before printing the success message even when the app is actually fine.
ssh -t "$SERVER" "sudo systemctl restart ojunai-api && \
  for i in \$(seq 1 30); do \
    if curl -fs http://localhost:5000/health > /dev/null; then \
      echo \"✓ Health check passed after \${i}s\"; \
      exit 0; \
    fi; \
    sleep 1; \
  done; \
  echo \"✗ Health check never passed after 30s\"; \
  sudo systemctl status ojunai-api --no-pager | head -20; \
  exit 1"

echo ""
echo "✅ API deployed."

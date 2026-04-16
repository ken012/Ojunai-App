#!/usr/bin/env bash
# Deploy the BizPilot .NET API to production.
# Usage:  ./deploy-api.sh
set -e

SERVER="bizpilot@46.225.108.35"
REMOTE_DIR="/var/www/bizpilot-api"
BACKUPS_DIR="/var/www/bizpilot-api-backups"
LOCAL_DIR="$HOME/Desktop/BizPilot-AI/BizPilot.API"

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
ssh -t "$SERVER" "sudo systemctl restart bizpilot-api && sleep 3 && curl -fs http://localhost:5000/health"

echo ""
echo "✅ API deployed."

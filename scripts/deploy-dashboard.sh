#!/usr/bin/env bash
# Deploy the Ojunai Next.js dashboard to production.
# Builds locally to catch errors, uploads source, builds on server, restarts PM2.
# Usage:  ./deploy-dashboard.sh
set -e

SERVER="bizpilot@46.225.108.35"
REMOTE_DIR="/var/www/ojunai-dashboard"
BACKUPS_DIR="/var/www/ojunai-dashboard-backups"
LOCAL_DIR="$HOME/Desktop/Ojunai-AI/dashboard"

echo "🧪 Building dashboard locally to catch errors first..."
cd "$LOCAL_DIR"
npm run build

echo "📦 Backing up current dashboard build on server..."
ssh -t "$SERVER" "
  sudo mkdir -p $BACKUPS_DIR
  TIMESTAMP=\$(date +%Y%m%d-%H%M%S)
  sudo cp -r $REMOTE_DIR/.next $BACKUPS_DIR/dashboard-\$TIMESTAMP
  sudo ls -1t $BACKUPS_DIR | grep '^dashboard-' | tail -n +6 | xargs -I {} sudo rm -rf $BACKUPS_DIR/{}
  echo \"Saved backup: dashboard-\$TIMESTAMP\"
"

echo "⬆️  Uploading source..."
scp -r src public tsconfig.json tailwind.config.ts postcss.config.mjs components.json package.json package-lock.json next.config.mjs .env.production "$SERVER:$REMOTE_DIR/"

echo "🔨 Building on server..."
ssh -t "$SERVER" "
  cd $REMOTE_DIR
  npm ci --legacy-peer-deps
  npm run build
  pm2 restart ojunai-dashboard
  sleep 3
  curl -fs http://localhost:3000 > /dev/null
"

echo ""
echo "✅ Dashboard deployed."

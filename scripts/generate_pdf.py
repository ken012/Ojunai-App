#!/usr/bin/env python3
"""Generate the BizPilot deployment scripts reference PDF."""
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import inch
from reportlab.lib.colors import HexColor, black
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, PageBreak, Preformatted, Table, TableStyle
)
from reportlab.lib.enums import TA_LEFT
import os

OUT = os.path.join(os.path.dirname(__file__), "BizPilot-Deploy-Scripts-Reference.pdf")

styles = getSampleStyleSheet()
H1 = ParagraphStyle('H1', parent=styles['Heading1'], fontSize=20, spaceAfter=14, textColor=HexColor("#0ea5e9"))
H2 = ParagraphStyle('H2', parent=styles['Heading2'], fontSize=14, spaceBefore=14, spaceAfter=8, textColor=HexColor("#0f172a"))
H3 = ParagraphStyle('H3', parent=styles['Heading3'], fontSize=11, spaceBefore=10, spaceAfter=4, textColor=HexColor("#475569"))
BODY = ParagraphStyle('Body', parent=styles['BodyText'], fontSize=10, leading=14, spaceAfter=6, textColor=HexColor("#1e293b"))
CODE = ParagraphStyle('Code', parent=styles['Code'], fontSize=8.5, leading=11, leftIndent=10, backColor=HexColor("#f1f5f9"), textColor=HexColor("#0f172a"), borderPadding=6)

def code(text):
    return Preformatted(text, CODE)

doc = SimpleDocTemplate(OUT, pagesize=letter, leftMargin=0.7*inch, rightMargin=0.7*inch, topMargin=0.8*inch, bottomMargin=0.8*inch)
story = []

story.append(Paragraph("BizPilot AI — Deploy & Rollback Scripts", H1))
story.append(Paragraph("A practical reference for deploying and rolling back the BizPilot AI API and dashboard. Save this somewhere you can find it during a 2 AM panic.", BODY))
story.append(Spacer(1, 0.15*inch))

# Overview table
story.append(Paragraph("Overview", H2))
story.append(Paragraph("Four bash scripts live in <b>~/Desktop/BizPilot-AI/scripts/</b>:", BODY))

data = [
    ["Script", "What it does"],
    ["deploy-api.sh", "Build, back up, upload, restart the .NET API"],
    ["rollback-api.sh", "Restore the API from a previous backup"],
    ["deploy-dashboard.sh", "Build, back up, upload source, rebuild on server, restart PM2"],
    ["rollback-dashboard.sh", "Restore the dashboard .next folder from a previous backup"],
]
t = Table(data, colWidths=[1.9*inch, 4.6*inch])
t.setStyle(TableStyle([
    ('BACKGROUND', (0,0), (-1,0), HexColor("#0ea5e9")),
    ('TEXTCOLOR', (0,0), (-1,0), HexColor("#ffffff")),
    ('FONTNAME', (0,0), (-1,0), 'Helvetica-Bold'),
    ('FONTSIZE', (0,0), (-1,-1), 9),
    ('GRID', (0,0), (-1,-1), 0.5, HexColor("#cbd5e1")),
    ('VALIGN', (0,0), (-1,-1), 'TOP'),
    ('LEFTPADDING', (0,0), (-1,-1), 8),
    ('RIGHTPADDING', (0,0), (-1,-1), 8),
    ('TOPPADDING', (0,0), (-1,-1), 6),
    ('BOTTOMPADDING', (0,0), (-1,-1), 6),
    ('FONTNAME', (0,1), (0,-1), 'Courier-Bold'),
]))
story.append(t)
story.append(Spacer(1, 0.15*inch))

story.append(Paragraph("First-time setup", H2))
story.append(Paragraph("If you ever recreate the scripts or download fresh, make them executable:", BODY))
story.append(code("chmod +x ~/Desktop/BizPilot-AI/scripts/*.sh"))
story.append(Paragraph("That's it. They're now runnable.", BODY))

# Deploy API
story.append(PageBreak())
story.append(Paragraph("deploy-api.sh — Deploy the .NET API", H2))
story.append(Paragraph("<b>Run it:</b>", BODY))
story.append(code("~/Desktop/BizPilot-AI/scripts/deploy-api.sh"))

story.append(Paragraph("<b>What happens, in order:</b>", BODY))
story.append(Paragraph("1. <b>Builds the API locally</b> with <font face='Courier'>dotnet publish -c Release</font>. If the build fails on your Mac, the script stops here — you never push broken code.", BODY))
story.append(Paragraph("2. <b>SSHes into the Hetzner server</b> and creates a timestamped backup of the current /var/www/bizpilot-api at /var/www/bizpilot-api-backups/api-YYYYMMDD-HHMMSS/", BODY))
story.append(Paragraph("3. <b>Prunes old backups</b> — keeps the 5 most recent, deletes anything older. Stops the disk filling up.", BODY))
story.append(Paragraph("4. <b>SCPs the new build</b> to the server.", BODY))
story.append(Paragraph("5. <b>Restarts systemd</b> service (bizpilot-api).", BODY))
story.append(Paragraph("6. <b>Hits /health endpoint</b> to confirm the API came back up. If /health fails, the script exits with an error and you know to investigate or roll back.", BODY))

story.append(Paragraph("<b>Time:</b> ~2 minutes per run. ~30 seconds of API downtime during the restart.", BODY))

story.append(Paragraph("<b>Edit before running if:</b> the server IP, SSH user, or paths change. Edit the variables at the top of the script.", BODY))

# Rollback API
story.append(Paragraph("rollback-api.sh — Restore a previous API build", H2))
story.append(Paragraph("<b>Run it:</b>", BODY))
story.append(code("~/Desktop/BizPilot-AI/scripts/rollback-api.sh"))

story.append(Paragraph("<b>What happens:</b>", BODY))
story.append(Paragraph("1. Lists the 5 most recent backups, newest first, with numbers (1, 2, 3, ...).", BODY))
story.append(Paragraph("2. Prompts you to pick a number.", BODY))
story.append(Paragraph("3. Replaces the live /var/www/bizpilot-api with the chosen backup.", BODY))
story.append(Paragraph("4. Restarts the API and verifies /health.", BODY))

story.append(Paragraph("<b>Sample run:</b>", BODY))
story.append(code("""Available API backups (newest first):
  1  api-20260409-073012
  2  api-20260408-184501
  3  api-20260408-091823
Enter the number to rollback to (or Ctrl+C to cancel): 2
Rolling back to api-20260408-184501...
Rolled back to api-20260408-184501"""))

story.append(Paragraph("<b>Important caveats:</b>", BODY))
story.append(Paragraph("• <b>Database migrations are NOT rolled back.</b> If your last deploy added a new column or table, the rolled-back code will still work (it just won't use the new column). But if it removed or renamed something, the rolled-back code will break. Test migrations carefully.", BODY))
story.append(Paragraph("• <b>The 5-backup retention is a soft limit.</b> If you need to roll back further, you have to manually preserve older backups before they get pruned.", BODY))

# Deploy dashboard
story.append(PageBreak())
story.append(Paragraph("deploy-dashboard.sh — Deploy the Next.js dashboard", H2))
story.append(Paragraph("<b>Run it:</b>", BODY))
story.append(code("~/Desktop/BizPilot-AI/scripts/deploy-dashboard.sh"))

story.append(Paragraph("<b>What happens, in order:</b>", BODY))
story.append(Paragraph("1. <b>Builds the dashboard locally first</b> with <font face='Courier'>npm run build</font>. Catches TypeScript and ESLint errors before wasting time uploading.", BODY))
story.append(Paragraph("2. <b>Backs up the current .next folder on the server</b> to /var/www/bizpilot-dashboard-backups/dashboard-YYYYMMDD-HHMMSS/. Keeps the last 5.", BODY))
story.append(Paragraph("3. <b>Uploads source files</b> (src/, package.json, configs, .env.production) via SCP.", BODY))
story.append(Paragraph("4. <b>Rebuilds on the server</b> — Mac (ARM) and Linux (x86) build artifacts aren't compatible, so we always build on the box where it'll run.", BODY))
story.append(Paragraph("5. <b>Restarts PM2</b> process (bizpilot-dashboard).", BODY))
story.append(Paragraph("6. <b>Hits the dashboard URL</b> internally to verify it came up.", BODY))

story.append(Paragraph("<b>Time:</b> ~3-4 minutes per run. Dashboard reload is instant for users — Next.js serves the new build immediately.", BODY))

story.append(Paragraph("<b>Why source files instead of .next?</b> Because Mac builds reference Mac binaries (@next/swc-darwin) that don't exist on Linux. Building on the server avoids this entirely.", BODY))

# Rollback dashboard
story.append(Paragraph("rollback-dashboard.sh — Restore a previous dashboard build", H2))
story.append(Paragraph("<b>Run it:</b>", BODY))
story.append(code("~/Desktop/BizPilot-AI/scripts/rollback-dashboard.sh"))

story.append(Paragraph("Same prompt-and-pick flow as the API rollback. Restores only the .next folder (the compiled output), then restarts PM2.", BODY))

story.append(Paragraph("<b>Note:</b> the dashboard rollback only restores the compiled .next/ folder, not the source files. The source on the server still reflects your most recent upload. That's intentional — you don't want a rollback to also revert your source so the next build picks up your latest fix.", BODY))

# Daily workflow
story.append(PageBreak())
story.append(Paragraph("Daily Workflow", H2))

story.append(Paragraph("Backend change", H3))
story.append(code("""# 1. Edit code, test locally
cd ~/Desktop/BizPilot-AI/BizPilot.API
# (make changes)
dotnet run    # verify, then Ctrl+C

# 2. Deploy
~/Desktop/BizPilot-AI/scripts/deploy-api.sh"""))

story.append(Paragraph("Frontend change", H3))
story.append(code("""# 1. Edit code, test locally
cd ~/Desktop/BizPilot-AI/dashboard
# (make changes)
npm run dev    # verify at localhost:3000, then Ctrl+C

# 2. Deploy
~/Desktop/BizPilot-AI/scripts/deploy-dashboard.sh"""))

story.append(Paragraph("Bot is broken — emergency rollback", H3))
story.append(code("""~/Desktop/BizPilot-AI/scripts/rollback-api.sh
# Pick "2" (the previous build), wait 30 seconds, you're back."""))

story.append(Paragraph("Database schema change (new EF migration)", H3))
story.append(code("""# 1. Add migration locally
cd ~/Desktop/BizPilot-AI/BizPilot.API
dotnet ef migrations add YourMigrationName

# 2. Test it locally
dotnet ef database update

# 3. Verify the app still works
dotnet run

# 4. Deploy as usual
~/Desktop/BizPilot-AI/scripts/deploy-api.sh
# The migration auto-applies on API startup."""))

story.append(Paragraph("Env var / secret change", H3))
story.append(code("""ssh bizpilot@46.225.108.35
sudo nano /etc/bizpilot/api.env
# (edit values)
sudo systemctl restart bizpilot-api"""))

# Troubleshooting
story.append(PageBreak())
story.append(Paragraph("Troubleshooting", H2))

story.append(Paragraph("Deploy fails with 'Permission denied (publickey)'", H3))
story.append(Paragraph("Your SSH key isn't loaded. Run <font face='Courier'>ssh-add ~/.ssh/id_ed25519</font> and try again.", BODY))

story.append(Paragraph("Deploy fails with 'sudo: a password is required'", H3))
story.append(Paragraph("The script needs sudo on the server but your sudoers requires a password. Two options: (a) type the password when prompted, (b) configure passwordless sudo for the bizpilot user (less secure but more convenient).", BODY))

story.append(Paragraph("Build fails locally", H3))
story.append(Paragraph("Fix the build error first. The script will not deploy a broken build.", BODY))

story.append(Paragraph("Deploy succeeded but the API or dashboard 502s", H3))
story.append(Paragraph("Check the service logs:", BODY))
story.append(code("""ssh bizpilot@46.225.108.35
sudo journalctl -u bizpilot-api -n 50 --no-pager
pm2 logs bizpilot-dashboard --lines 50"""))

story.append(Paragraph("Look for stack traces. Common causes: missing env var, broken DB migration, port already in use. If you can't fix in 5 min, roll back.", BODY))

story.append(Paragraph("rollback-api.sh shows no backups", H3))
story.append(Paragraph("Either you've never deployed yet, or the backups directory was wiped. The first deploy creates the backups folder. Subsequent deploys add to it.", BODY))

story.append(Paragraph("API restart causes 30 sec downtime — is that normal?", H3))
story.append(Paragraph("Yes. systemd restart is not zero-downtime. For an MVP this is fine. If you ever need zero-downtime, you'd set up blue/green deployment with two API instances behind nginx and a reload-not-restart pattern. Overkill until you have paying customers.", BODY))

# Things to know
story.append(Paragraph("Things to know", H2))
story.append(Paragraph("• <b>The scripts deploy from your local files.</b> If you mess up locally, the server backup is your only undo. Set up git and commit before each deploy.", BODY))
story.append(Paragraph("• <b>Backups eat ~50 MB each for the API.</b> The script auto-prunes to 5. Bump or lower in the script if needed.", BODY))
story.append(Paragraph("• <b>The .env.production file</b> in the dashboard folder gets uploaded with each dashboard deploy. If you change env vars, edit that file before deploying.", BODY))
story.append(Paragraph("• <b>The /etc/bizpilot/api.env file on the server</b> is NOT touched by deploy scripts. Edit it directly via SSH and restart the API manually after.", BODY))

# Appendix
story.append(PageBreak())
story.append(Paragraph("Appendix: Quick Reference", H2))

story.append(Paragraph("Server connection", H3))
story.append(code("ssh bizpilot@46.225.108.35"))

story.append(Paragraph("Read API logs (live)", H3))
story.append(code("sudo journalctl -u bizpilot-api -f"))

story.append(Paragraph("Read dashboard logs", H3))
story.append(code("pm2 logs bizpilot-dashboard"))

story.append(Paragraph("Restart API manually", H3))
story.append(code("sudo systemctl restart bizpilot-api"))

story.append(Paragraph("Restart dashboard manually", H3))
story.append(code("pm2 restart bizpilot-dashboard"))

story.append(Paragraph("Health check", H3))
story.append(code("curl https://api.bizpilot-ai.com/health"))

story.append(Paragraph("Edit production secrets", H3))
story.append(code("""sudo nano /etc/bizpilot/api.env
sudo systemctl restart bizpilot-api"""))

story.append(Paragraph("Production URLs", H3))
story.append(code("""Dashboard:  https://app.bizpilot-ai.com
API:        https://api.bizpilot-ai.com
Health:     https://api.bizpilot-ai.com/health
Webhook:    https://api.bizpilot-ai.com/api/webhooks/whatsapp"""))

doc.build(story)
print(f"PDF written to {OUT}")

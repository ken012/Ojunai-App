#!/usr/bin/env python3
"""Generate the BizPilot Making Changes step-by-step guide PDF."""
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import inch
from reportlab.lib.colors import HexColor
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, PageBreak, Preformatted, Table, TableStyle
)
import os

OUT = os.path.expanduser("~/Desktop/BizPilot-Making-Changes-Guide.pdf")

styles = getSampleStyleSheet()
H1 = ParagraphStyle('H1', parent=styles['Heading1'], fontSize=22, spaceAfter=14, textColor=HexColor("#0ea5e9"))
H2 = ParagraphStyle('H2', parent=styles['Heading2'], fontSize=15, spaceBefore=16, spaceAfter=8, textColor=HexColor("#0f172a"))
H3 = ParagraphStyle('H3', parent=styles['Heading3'], fontSize=12, spaceBefore=10, spaceAfter=4, textColor=HexColor("#475569"))
BODY = ParagraphStyle('Body', parent=styles['BodyText'], fontSize=10, leading=14, spaceAfter=6, textColor=HexColor("#1e293b"))
NOTE = ParagraphStyle('Note', parent=styles['BodyText'], fontSize=9, leading=13, spaceAfter=6, textColor=HexColor("#64748b"), leftIndent=10, borderColor=HexColor("#cbd5e1"), borderWidth=0, borderPadding=4)
CODE = ParagraphStyle('Code', parent=styles['Code'], fontSize=8.5, leading=11, leftIndent=10, backColor=HexColor("#f1f5f9"), textColor=HexColor("#0f172a"), borderPadding=6)

def code(text):
    return Preformatted(text, CODE)

doc = SimpleDocTemplate(OUT, pagesize=letter, leftMargin=0.7*inch, rightMargin=0.7*inch, topMargin=0.8*inch, bottomMargin=0.8*inch)
story = []

story.append(Paragraph("BizPilot AI", H1))
story.append(Paragraph("How to Make Changes Once You're Live", ParagraphStyle('sub', parent=BODY, fontSize=14, textColor=HexColor("#475569"), spaceAfter=12)))
story.append(Paragraph("A step-by-step playbook for editing the BizPilot AI app and shipping changes safely to production. Read this once start to finish, then keep it as a reference.", BODY))

story.append(Spacer(1, 0.2*inch))
story.append(Paragraph("Big picture", H2))
story.append(Paragraph("BizPilot has two pieces:", BODY))
story.append(Paragraph("• <b>Backend (.NET 8 API)</b> — handles WhatsApp messages, business logic, database. Runs as a systemd service on the Hetzner server.", BODY))
story.append(Paragraph("• <b>Frontend (Next.js dashboard)</b> — the web app users see at app.bizpilot-ai.com. Runs as a PM2-managed Node process.", BODY))
story.append(Paragraph("Each piece has its own deploy and rollback script. You always test changes locally on your Mac first, then deploy.", BODY))

story.append(Paragraph("The golden rule", H3))
story.append(Paragraph("<b>Never deploy a change you haven't tested locally.</b> Local first, prod second. The deploy scripts will refuse to deploy a build that fails locally — that's intentional.", BODY))

# === Section 1: Setup ===
story.append(PageBreak())
story.append(Paragraph("Section 1 — One-time setup (already done, for reference)", H2))
story.append(Paragraph("These steps were completed during deployment. Listed here so you know what's running where.", BODY))

story.append(Paragraph("Local machine (your Mac)", H3))
story.append(Paragraph("• <b>~/Desktop/BizPilot-AI/BizPilot.API/</b> — .NET API source code", BODY))
story.append(Paragraph("• <b>~/Desktop/BizPilot-AI/dashboard/</b> — Next.js dashboard source code", BODY))
story.append(Paragraph("• <b>~/Desktop/BizPilot-AI/scripts/</b> — deploy and rollback bash scripts", BODY))
story.append(Paragraph("• <b>SSH key</b> at ~/.ssh/id_ed25519 (gives access to the Hetzner server)", BODY))

story.append(Paragraph("Production server (Hetzner Cloud)", H3))
story.append(Paragraph("• <b>IP:</b> 46.225.108.35", BODY))
story.append(Paragraph("• <b>SSH user:</b> bizpilot", BODY))
story.append(Paragraph("• <b>API:</b> /var/www/bizpilot-api/ — runs as systemd service `bizpilot-api`", BODY))
story.append(Paragraph("• <b>Dashboard:</b> /var/www/bizpilot-dashboard/ — runs via PM2 process `bizpilot-dashboard`", BODY))
story.append(Paragraph("• <b>Database:</b> PostgreSQL `bizpilot_prod` on localhost:5432", BODY))
story.append(Paragraph("• <b>Secrets:</b> /etc/bizpilot/api.env (chmod 600, root-owned)", BODY))
story.append(Paragraph("• <b>Nginx:</b> reverse-proxies app.bizpilot-ai.com → :3000 and api.bizpilot-ai.com → :5000", BODY))
story.append(Paragraph("• <b>SSL:</b> Let's Encrypt via Certbot, auto-renews", BODY))

story.append(Paragraph("Production URLs", H3))
story.append(code("""Dashboard:  https://app.bizpilot-ai.com
API:        https://api.bizpilot-ai.com
Health:     https://api.bizpilot-ai.com/health
Webhook:    https://api.bizpilot-ai.com/api/webhooks/whatsapp"""))

# === Section 2: Backend changes ===
story.append(PageBreak())
story.append(Paragraph("Section 2 — Making a backend (API) change", H2))
story.append(Paragraph("Use this when you change C# code, prompts, services, controllers, the database schema, or anything in <b>~/Desktop/BizPilot-AI/BizPilot.API/</b>.", BODY))

story.append(Paragraph("Step 1 — Edit the code", H3))
story.append(Paragraph("Open the file in your editor. Make the change. Save.", BODY))

story.append(Paragraph("Step 2 — Test locally", H3))
story.append(code("""cd ~/Desktop/BizPilot-AI/BizPilot.API
dotnet run"""))
story.append(Paragraph("This starts the API on http://localhost:5000. Watch the terminal for errors. To trigger your change:", BODY))
story.append(Paragraph("• If you changed the WhatsApp parsing logic — start ngrok in another tab: <font face='Courier'>ngrok http 5000</font>, point Twilio sandbox webhook at the ngrok URL, message your bot from your phone.", BODY))
story.append(Paragraph("• If you changed an API endpoint — also run <font face='Courier'>npm run dev</font> in the dashboard folder and exercise it through the UI.", BODY))
story.append(Paragraph("• If you just changed business logic — write a quick test or use the local dashboard to trigger it.", BODY))
story.append(Paragraph("When satisfied, press <b>Ctrl+C</b> to stop the local API.", BODY))

story.append(Paragraph("Step 3 — Deploy to production", H3))
story.append(code("~/Desktop/BizPilot-AI/scripts/deploy-api.sh"))
story.append(Paragraph("This script:", BODY))
story.append(Paragraph("1. Builds the API for production with <font face='Courier'>dotnet publish -c Release</font>", BODY))
story.append(Paragraph("2. Backs up the current version on the server (timestamped, last 5 kept)", BODY))
story.append(Paragraph("3. Uploads the new build via scp", BODY))
story.append(Paragraph("4. Restarts the systemd service", BODY))
story.append(Paragraph("5. Hits /health to verify the new version is alive", BODY))
story.append(Paragraph("Total time: ~2 minutes. Brief downtime (~30 seconds) during the restart.", BODY))

story.append(Paragraph("Step 4 — Verify in production", H3))
story.append(Paragraph("Trigger the change against production:", BODY))
story.append(Paragraph("• Bot change → message your bot, watch the WhatsApp reply", BODY))
story.append(Paragraph("• API change → log into <font face='Courier'>https://app.bizpilot-ai.com</font> and exercise the affected feature", BODY))
story.append(Paragraph("Watch live logs while testing:", BODY))
story.append(code("""ssh bizpilot@46.225.108.35
sudo journalctl -u bizpilot-api -f
# Press Ctrl+C to exit"""))

story.append(Paragraph("Step 5 — If it's broken, roll back", H3))
story.append(code("~/Desktop/BizPilot-AI/scripts/rollback-api.sh"))
story.append(Paragraph("Pick number <b>2</b> (the previous build). The script restores it and restarts. ~30 seconds and you're back on the working version. Then debug locally before trying again.", BODY))

# === Section 3: Frontend changes ===
story.append(PageBreak())
story.append(Paragraph("Section 3 — Making a frontend (dashboard) change", H2))
story.append(Paragraph("Use this when you change anything in <b>~/Desktop/BizPilot-AI/dashboard/</b> — pages, components, types, styles.", BODY))

story.append(Paragraph("Step 1 — Edit the code", H3))
story.append(Paragraph("Most dashboard code lives in <font face='Courier'>dashboard/src/app/</font>. The pages are:", BODY))
story.append(Paragraph("• <font face='Courier'>(dashboard)/page.tsx</font> — overview/home", BODY))
story.append(Paragraph("• <font face='Courier'>(dashboard)/sales/page.tsx</font> — sales table", BODY))
story.append(Paragraph("• <font face='Courier'>(dashboard)/expenses/page.tsx</font> — expenses table", BODY))
story.append(Paragraph("• <font face='Courier'>(dashboard)/inventory/page.tsx</font> — products grid", BODY))
story.append(Paragraph("• <font face='Courier'>(dashboard)/contacts/page.tsx</font> — contacts table", BODY))
story.append(Paragraph("• <font face='Courier'>(dashboard)/get-started/page.tsx</font> — onboarding", BODY))

story.append(Paragraph("Step 2 — Test locally", H3))
story.append(code("""cd ~/Desktop/BizPilot-AI/dashboard
npm run dev"""))
story.append(Paragraph("Open <font face='Courier'>http://localhost:3000</font> (or 3001 if 3000 is taken). The dashboard hot-reloads on every save — no restart needed when you edit a file.", BODY))
story.append(Paragraph("Make sure the local API is also running in another tab so the dashboard has data to fetch.", BODY))
story.append(Paragraph("When satisfied, press <b>Ctrl+C</b> in the dashboard terminal.", BODY))

story.append(Paragraph("Step 3 — Deploy to production", H3))
story.append(code("~/Desktop/BizPilot-AI/scripts/deploy-dashboard.sh"))
story.append(Paragraph("This script:", BODY))
story.append(Paragraph("1. Builds the dashboard locally (catches errors before uploading)", BODY))
story.append(Paragraph("2. Backs up the current .next folder on the server (last 5 kept)", BODY))
story.append(Paragraph("3. Uploads source files via scp", BODY))
story.append(Paragraph("4. Rebuilds on the server (Mac and Linux builds aren't compatible)", BODY))
story.append(Paragraph("5. Restarts PM2 process", BODY))
story.append(Paragraph("6. Hits the local dashboard URL to verify it's alive", BODY))
story.append(Paragraph("Total time: ~3-4 minutes. Users see the new build immediately.", BODY))

story.append(Paragraph("Step 4 — Verify in production", H3))
story.append(Paragraph("Open <font face='Courier'>https://app.bizpilot-ai.com</font> in your browser. Hard refresh (Cmd+Shift+R) to bypass any cached HTML. Click through the affected pages.", BODY))

story.append(Paragraph("Step 5 — Roll back if broken", H3))
story.append(code("~/Desktop/BizPilot-AI/scripts/rollback-dashboard.sh"))
story.append(Paragraph("Pick the previous backup. PM2 reloads with the old build.", BODY))

# === Section 4: Database changes ===
story.append(PageBreak())
story.append(Paragraph("Section 4 — Making a database schema change", H2))
story.append(Paragraph("This is the riskiest type of change. Take it slow.", BODY))

story.append(Paragraph("Step 1 — Edit the entity model", H3))
story.append(Paragraph("Open the relevant file in <font face='Courier'>BizPilot.API/Models/</font>. Add or modify the property. Example: adding a new column to Sale.", BODY))
story.append(code("""// In Models/Sale.cs
public string? CouponCode { get; set; }"""))

story.append(Paragraph("Step 2 — Add a migration", H3))
story.append(code("""cd ~/Desktop/BizPilot-AI/BizPilot.API
dotnet ef migrations add AddCouponCodeToSale"""))
story.append(Paragraph("This creates a new file under <font face='Courier'>Migrations/</font>. Open it. Read it. Make sure it does what you expect (adds the column, no surprise drops).", BODY))

story.append(Paragraph("Step 3 — Apply locally and test", H3))
story.append(code("""dotnet ef database update
dotnet run"""))
story.append(Paragraph("Verify the local DB has the new column. Test the affected feature.", BODY))

story.append(Paragraph("Step 4 — Deploy", H3))
story.append(code("~/Desktop/BizPilot-AI/scripts/deploy-api.sh"))
story.append(Paragraph("The migration auto-applies on API startup (this is wired into Program.cs). Watch the logs to confirm it ran:", BODY))
story.append(code("""ssh bizpilot@46.225.108.35
sudo journalctl -u bizpilot-api -n 30"""))
story.append(Paragraph("You should see lines like 'Applying migration AddCouponCodeToSale' and the SQL it executed.", BODY))

story.append(Paragraph("Step 5 — If the migration is wrong", H3))
story.append(Paragraph("<b>Migrations are forward-only.</b> A rollback of the API code will NOT roll back the database change. You have to:", BODY))
story.append(Paragraph("1. Either fix forward — add a new migration that corrects the schema, deploy again", BODY))
story.append(Paragraph("2. Or restore the database from a backup (you should set up daily pg_dump backups soon)", BODY))
story.append(Paragraph("This is why you test migrations locally first and read the generated SQL.", BODY))

# === Section 5: Secrets / env vars ===
story.append(PageBreak())
story.append(Paragraph("Section 5 — Changing a secret or environment variable", H2))
story.append(Paragraph("Production secrets live in <font face='Courier'>/etc/bizpilot/api.env</font> on the server. They are NOT touched by deploy scripts (intentional — secrets stay on the server).", BODY))

story.append(Paragraph("Step 1 — Edit the file", H3))
story.append(code("""ssh bizpilot@46.225.108.35
sudo nano /etc/bizpilot/api.env"""))
story.append(Paragraph("Edit the line you want to change. Format is <font face='Courier'>KEY=value</font> with no quotes, no spaces.", BODY))
story.append(Paragraph("Save: <b>Ctrl+O</b>, Enter, <b>Ctrl+X</b>", BODY))

story.append(Paragraph("Step 2 — Restart the API", H3))
story.append(code("sudo systemctl restart bizpilot-api"))

story.append(Paragraph("Step 3 — Verify", H3))
story.append(code("""sudo systemctl status bizpilot-api
curl https://api.bizpilot-ai.com/health"""))

story.append(Paragraph("Common env vars you might change:", H3))
story.append(Paragraph("• <font face='Courier'>Twilio__AuthToken</font> — after rotating in the Twilio console", BODY))
story.append(Paragraph("• <font face='Courier'>Claude__ApiKey</font> — after rotating in Anthropic console", BODY))
story.append(Paragraph("• <font face='Courier'>Cors__AllowedOrigins</font> — when adding a new dashboard domain", BODY))
story.append(Paragraph("• <font face='Courier'>Claude__Model</font> — switch between sonnet/haiku/opus", BODY))

# === Section 6: Tweaking the AI ===
story.append(PageBreak())
story.append(Paragraph("Section 6 — Tweaking the AI prompt", H2))
story.append(Paragraph("The Claude system prompt lives in <font face='Courier'>BizPilot.API/Services/ClaudeParsingService.cs</font> in the <font face='Courier'>BuildSystemPrompt()</font> method.", BODY))

story.append(Paragraph("Step 1 — Edit the prompt", H3))
story.append(Paragraph("Open the file. Find the section you want to tune (intent rules, examples, edge cases). Edit it.", BODY))

story.append(Paragraph("Step 2 — Test locally with real conversations", H3))
story.append(code("""# Terminal 1
cd ~/Desktop/BizPilot-AI/BizPilot.API
dotnet run

# Terminal 2
ngrok http 5000

# Twilio sandbox: set webhook to ngrok URL
# Phone: send test messages"""))

story.append(Paragraph("Watch the terminal logs for parsed intents and Claude's confidence scores. Iterate until the conversations work.", BODY))

story.append(Paragraph("Step 3 — Deploy", H3))
story.append(code("~/Desktop/BizPilot-AI/scripts/deploy-api.sh"))
story.append(Paragraph("Then test with real WhatsApp conversations against production.", BODY))

story.append(Paragraph("Tip: Save failure transcripts", H3))
story.append(Paragraph("When the bot misparses something, copy-paste the exact WhatsApp transcript into a notes file. You'll use these failures to iterate the prompt accurately instead of guessing.", BODY))

# === Section 7: Common scenarios ===
story.append(PageBreak())
story.append(Paragraph("Section 7 — Common scenarios", H2))

story.append(Paragraph("Scenario: A user reports a bug", H3))
story.append(Paragraph("1. Reproduce locally first if possible.", BODY))
story.append(Paragraph("2. Check logs to understand what happened:", BODY))
story.append(code("""ssh bizpilot@46.225.108.35
sudo journalctl -u bizpilot-api -n 100 --no-pager | grep -i error
pm2 logs bizpilot-dashboard --lines 100"""))
story.append(Paragraph("3. Fix locally, test, deploy.", BODY))
story.append(Paragraph("4. Tell the user it's fixed.", BODY))

story.append(Paragraph("Scenario: Production is broken right now", H3))
story.append(Paragraph("1. Don't panic. Roll back first, debug after.", BODY))
story.append(code("""~/Desktop/BizPilot-AI/scripts/rollback-api.sh
# OR
~/Desktop/BizPilot-AI/scripts/rollback-dashboard.sh"""))
story.append(Paragraph("2. Once back on the working version, look at the logs and diff your last change to find the bug.", BODY))
story.append(Paragraph("3. Fix on your Mac. Test. Deploy.", BODY))

story.append(Paragraph("Scenario: I want to add a new dashboard page", H3))
story.append(Paragraph("1. Create a new file at <font face='Courier'>dashboard/src/app/(dashboard)/yourpage/page.tsx</font>", BODY))
story.append(Paragraph("2. Use an existing page (like sales/page.tsx) as a template.", BODY))
story.append(Paragraph("3. Add a link to it in the dashboard layout/sidebar (likely in <font face='Courier'>(dashboard)/layout.tsx</font>).", BODY))
story.append(Paragraph("4. Test locally with <font face='Courier'>npm run dev</font>.", BODY))
story.append(Paragraph("5. Deploy with <font face='Courier'>./deploy-dashboard.sh</font>.", BODY))

story.append(Paragraph("Scenario: I want to add a new API endpoint", H3))
story.append(Paragraph("1. Add the method to the relevant Controller in <font face='Courier'>BizPilot.API/Controllers/</font>.", BODY))
story.append(Paragraph("2. Add the matching method to the Service interface and implementation.", BODY))
story.append(Paragraph("3. Test locally with <font face='Courier'>dotnet run</font> + curl or the dashboard.", BODY))
story.append(Paragraph("4. Deploy with <font face='Courier'>./deploy-api.sh</font>.", BODY))
story.append(Paragraph("5. Update the dashboard to call the new endpoint, then deploy the dashboard.", BODY))

story.append(Paragraph("Scenario: I want to add a new column to a table", H3))
story.append(Paragraph("Follow Section 4 (database schema change). Then update any DTOs and dashboard pages that need to display the new field. Then deploy both API and dashboard.", BODY))

# === Section 8: Safety net ===
story.append(PageBreak())
story.append(Paragraph("Section 8 — The safety net", H2))

story.append(Paragraph("Things you should set up to make changes safer:", BODY))

story.append(Paragraph("Daily database backups", H3))
story.append(Paragraph("On the server, set up a cron job to run pg_dump every night and save to ~/backups/. Keep the last 14 days. This is your insurance against bad migrations.", BODY))
story.append(code("""ssh bizpilot@46.225.108.35
mkdir -p ~/backups
crontab -e
# Add this line:
0 2 * * * pg_dump -U bizpilot bizpilot_prod | gzip > ~/backups/bizpilot_$(date +\\%Y\\%m\\%d).sql.gz && find ~/backups -name 'bizpilot_*.sql.gz' -mtime +14 -delete"""))

story.append(Paragraph("Git version control", H3))
story.append(Paragraph("Initialize git in <font face='Courier'>~/Desktop/BizPilot-AI/</font> if you haven't. Commit before every deploy. Push to a private GitHub repo. This is your insurance against bad local changes.", BODY))
story.append(code("""cd ~/Desktop/BizPilot-AI
git init
git add .
git commit -m \"Initial commit - production live\"
# Then create a private repo on GitHub and push"""))

story.append(Paragraph("Uptime monitoring (already set up)", H3))
story.append(Paragraph("UptimeRobot pings <font face='Courier'>https://api.bizpilot-ai.com/health</font> every 5 min. You'll get an email the moment the API goes down.", BODY))

story.append(Paragraph("Pre-launch security TODOs (still pending)", H3))
story.append(Paragraph("Before going public with real users:", BODY))
story.append(Paragraph("• Rotate the Claude API key one more time (the current one was visible in development chat)", BODY))
story.append(Paragraph("• Rotate the Twilio Auth Token", BODY))
story.append(Paragraph("• Rotate the JWT secret", BODY))
story.append(Paragraph("• Update <font face='Courier'>/etc/bizpilot/api.env</font> with all three new values", BODY))
story.append(Paragraph("• Restart the API", BODY))

# === Quick reference ===
story.append(PageBreak())
story.append(Paragraph("Quick Reference Card", H2))

story.append(Paragraph("Deploy commands", H3))
story.append(code("""# API
~/Desktop/BizPilot-AI/scripts/deploy-api.sh

# Dashboard
~/Desktop/BizPilot-AI/scripts/deploy-dashboard.sh

# Rollbacks
~/Desktop/BizPilot-AI/scripts/rollback-api.sh
~/Desktop/BizPilot-AI/scripts/rollback-dashboard.sh"""))

story.append(Paragraph("Testing locally", H3))
story.append(code("""# API
cd ~/Desktop/BizPilot-AI/BizPilot.API && dotnet run

# Dashboard
cd ~/Desktop/BizPilot-AI/dashboard && npm run dev"""))

story.append(Paragraph("Server access", H3))
story.append(code("ssh bizpilot@46.225.108.35"))

story.append(Paragraph("Reading logs", H3))
story.append(code("""# API live
sudo journalctl -u bizpilot-api -f

# API last 50 lines
sudo journalctl -u bizpilot-api -n 50 --no-pager

# Dashboard
pm2 logs bizpilot-dashboard"""))

story.append(Paragraph("Manual restart", H3))
story.append(code("""sudo systemctl restart bizpilot-api
pm2 restart bizpilot-dashboard"""))

story.append(Paragraph("Edit secrets", H3))
story.append(code("""sudo nano /etc/bizpilot/api.env
sudo systemctl restart bizpilot-api"""))

story.append(Paragraph("Database access", H3))
story.append(code("""sudo -u postgres psql bizpilot_prod
# Inside psql:
# \\dt           list tables
# \\d \"Sales\"    describe Sales table
# SELECT * FROM \"Users\";
# \\q             quit"""))

story.append(Paragraph("Production URLs", H3))
story.append(code("""Dashboard:  https://app.bizpilot-ai.com
API:        https://api.bizpilot-ai.com
Health:     https://api.bizpilot-ai.com/health
Webhook:    https://api.bizpilot-ai.com/api/webhooks/whatsapp"""))

doc.build(story)
print(f"PDF written to {OUT}")

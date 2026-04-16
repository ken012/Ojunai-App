#!/usr/bin/env python3
"""Generate the BizPilot Analytics & Admin Views reference PDF."""
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import inch
from reportlab.lib.colors import HexColor
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, PageBreak, Preformatted, Table, TableStyle
)
import os

OUT = os.path.expanduser("~/Desktop/BizPilot-Analytics-Guide.pdf")

styles = getSampleStyleSheet()
H1 = ParagraphStyle('H1', parent=styles['Heading1'], fontSize=22, spaceAfter=14, textColor=HexColor("#0ea5e9"))
H2 = ParagraphStyle('H2', parent=styles['Heading2'], fontSize=15, spaceBefore=16, spaceAfter=8, textColor=HexColor("#0f172a"))
H3 = ParagraphStyle('H3', parent=styles['Heading3'], fontSize=12, spaceBefore=10, spaceAfter=4, textColor=HexColor("#475569"))
BODY = ParagraphStyle('Body', parent=styles['BodyText'], fontSize=10, leading=14, spaceAfter=6, textColor=HexColor("#1e293b"))
CODE = ParagraphStyle('Code', parent=styles['Code'], fontSize=8.5, leading=11, leftIndent=10, backColor=HexColor("#f1f5f9"), textColor=HexColor("#0f172a"), borderPadding=6)
SMALL = ParagraphStyle('Small', parent=styles['BodyText'], fontSize=9, leading=12, spaceAfter=4, textColor=HexColor("#64748b"))

doc = SimpleDocTemplate(OUT, pagesize=letter, leftMargin=0.7*inch, rightMargin=0.7*inch,
                        topMargin=0.8*inch, bottomMargin=0.8*inch)
story = []

def code(text):
    return Preformatted(text, CODE)

def p(text):
    story.append(Paragraph(text, BODY))

def h2(text):
    story.append(Paragraph(text, H2))

def h3(text):
    story.append(Paragraph(text, H3))

def small(text):
    story.append(Paragraph(text, SMALL))

def spacer(height=0.1):
    story.append(Spacer(1, height*inch))

def endpoint_table(rows):
    """rows = list of (URL, Who, What, Access). Header added automatically."""
    data = [["Endpoint / Page", "Who can view", "What you see", "How to access"]]
    for r in rows:
        data.append([
            Paragraph(f"<font face='Courier'>{r[0]}</font>", SMALL),
            Paragraph(r[1], SMALL),
            Paragraph(r[2], SMALL),
            Paragraph(r[3], SMALL),
        ])
    t = Table(data, colWidths=[2.0*inch, 1.2*inch, 2.3*inch, 1.3*inch], repeatRows=1)
    t.setStyle(TableStyle([
        ('BACKGROUND', (0, 0), (-1, 0), HexColor("#0ea5e9")),
        ('TEXTCOLOR', (0, 0), (-1, 0), HexColor("#ffffff")),
        ('FONTNAME', (0, 0), (-1, 0), 'Helvetica-Bold'),
        ('FONTSIZE', (0, 0), (-1, 0), 9),
        ('ALIGN', (0, 0), (-1, 0), 'LEFT'),
        ('FONTSIZE', (0, 1), (-1, -1), 8.5),
        ('GRID', (0, 0), (-1, -1), 0.3, HexColor("#cbd5e1")),
        ('VALIGN', (0, 0), (-1, -1), 'TOP'),
        ('ROWBACKGROUNDS', (0, 1), (-1, -1), [HexColor("#ffffff"), HexColor("#f8fafc")]),
        ('LEFTPADDING', (0, 0), (-1, -1), 5),
        ('RIGHTPADDING', (0, 0), (-1, -1), 5),
        ('TOPPADDING', (0, 0), (-1, -1), 4),
        ('BOTTOMPADDING', (0, 0), (-1, -1), 4),
    ]))
    story.append(t)
    spacer(0.15)


# ═══════════════════════════════ Cover ═══════════════════════════════
story.append(Paragraph("BizPilot AI", H1))
story.append(Paragraph(
    "Analytics, Admin Views, and Telemetry",
    ParagraphStyle('sub', parent=BODY, fontSize=14, textColor=HexColor("#475569"), spaceAfter=12)
))
p("A complete reference for every dashboard page, admin endpoint, and telemetry signal in BizPilot. "
  "Organized by audience: who uses it, what they see, and exactly how to get there.")

spacer(0.2)

h2("Three audiences, three layers of visibility")
p("BizPilot's observability is split into three audiences based on who needs to see what:")
p("<b>Business owners and staff</b> see their own business's data via the dashboard and WhatsApp. Reports on sales, inventory, customers, ledger, and so on. Scoped to their business only.")
p("<b>You (Ken, as admin)</b> see cross-business metrics via admin-keyed endpoints: onboarding funnel, misparse rate, confidence drift, top failing phrasings. Used for product health monitoring.")
p("<b>Operators / on-call</b> see background job internals via the Hangfire dashboard (loopback-only). Used for debugging jobs and inspecting failures.")

p("The rest of this document is organized by audience. Skip to the section you need.")

# ═══════════════════════════════ Part 1: Admin ═══════════════════════════════
story.append(PageBreak())
h2("Part 1 — Admin views (cross-business metrics)")

p("Admin endpoints are gated by a shared secret key (<b>Admin:AnalyticsKey</b> in appsettings). Every request to an admin endpoint must include the key as a <code>?key=...</code> query parameter. The key is 32+ characters minimum, and requests are rejected with a constant-time comparison so timing attacks can't leak it.")

h3("How to configure the admin key")
code("""# On the Hetzner box, the key lives in the production appsettings.
# Set it via environment variable on the systemd service so it never lands in git:
sudo systemctl edit bizpilot-api
# Add:
# [Service]
# Environment=Admin__AnalyticsKey=<32+-character-random-string>
sudo systemctl daemon-reload && sudo systemctl restart bizpilot-api

# Generate a new key locally:
openssl rand -base64 48""")

spacer(0.1)

h3("Onboarding funnel")
p("Shows how many phone numbers reach each step of the WhatsApp signup flow. Answers questions like 'how many people start signup but drop off?' and 'which step has the biggest drop-off?'")
p("<b>URL:</b> <code>https://app.bizpilot-ai.com/admin/analytics?key=&lt;admin-key&gt;</code>")
p("<b>Backing endpoint:</b> <code>GET /api/admin/onboarding-analytics?key=&lt;key&gt;</code>")

p("<b>You see three sections:</b>")
p("• <b>Funnel</b> — count per onboarding step (menu → started → business name → type → city → owner name → confirm → complete). Drop-off between steps tells you which prompt is losing users.")
p("• <b>Active flows</b> — phone numbers currently mid-signup (started but not yet complete). Each row shows the partial business info captured so far.")
p("• <b>Recent signups</b> — businesses created in the last 30 days with owner name, city, plan, and trial end date.")

p("Phone numbers are redacted to the last 4 digits in the output.")

spacer(0.1)

h3("Telemetry dashboard (4 metrics)")
p("<b>URL:</b> <code>https://app.bizpilot-ai.com/admin/telemetry?key=&lt;admin-key&gt;</code>")
p("Single page rendering all four telemetry signals side-by-side. Pick a window (1, 7, 14, or 30 days) and click Load.")

p("The page hits four admin-gated endpoints. You can also call them directly if you want raw JSON or scripting access:")

endpoint_table([
    ("/api/admin/telemetry/misparse-rate",
     "You (admin key)",
     "% of inbound messages ending in NeedsClarification or Failed, per intent. Flags where Claude struggles.",
     "GET with ?key=&days=7"),
    ("/api/admin/telemetry/retry-patterns",
     "You (admin key)",
     "Users who sent multiple rapid messages after a clarification response. Each chain is a frustrated user.",
     "GET with ?key=&days=7"),
    ("/api/admin/telemetry/confidence-distribution",
     "You (admin key)",
     "Histogram of Claude confidence scores across 6 buckets plus the mean. Drift-detection signal.",
     "GET with ?key=&days=7"),
    ("/api/admin/telemetry/top-failures",
     "You (admin key)",
     "Clustered raw messages that failed to parse, ranked by frequency. Each cluster is a corpus candidate.",
     "GET with ?key=&days=7&limit=20"),
])

h3("How to read each telemetry signal")

p("<b>Misparse rate.</b> Overall rate should sit under 3%. Per-intent rates above 5% flag categories Claude struggles with. When a specific intent spikes, open the top-failures endpoint and see which phrasings are driving the misses. Add those phrasings to the corpus (<code>BizPilot.Tests/Corpus/conversation-corpus.yml</code>) and iterate on the prompt.")

p("<b>Retry chains.</b> Each chain is a user who got a clarification response, then sent 1-5 more messages within 2 minutes trying to be understood. The first message in each chain is your priority for fixes — it's what they actually wanted the bot to do. Healthy steady state: less than 5 chains per 100 unique users per week.")

p("<b>Confidence distribution.</b> Healthy shape is: 80%+ in the 0.90-1.00 bucket, most of the rest in 0.75-0.90, low single digits in 0.60-0.75, negligible below. When the high-confidence percentage drops week over week, something has shifted — prompt change, Claude model update, new user phrasings. Look at top-failures to find what changed.")

p("<b>Top failures.</b> Each row is a cluster of normalized messages that failed. The sample message shows a literal example. The count is how many times that cluster appeared. The 'Claude parsed as' field shows what intent Claude returned (often null or 'unknown'). Each cluster is a corpus entry waiting to be written.")

# ═══════════════════════════════ Part 2: Business-owner reports ═══════════════════════════════
story.append(PageBreak())
h2("Part 2 — Business-owner reports (the /reports dashboard tab)")

p("Every business owner and staff member with <code>view_own_reports</code> permission can access these. Advanced reports are gated behind the <b>advanced_reports</b> plan feature (Shop tier and above).")
p("<b>URL:</b> <code>https://app.bizpilot-ai.com/reports</code>")
p("Five tabs: Overview, Financial, Customers, Inventory, Debts. Each tab calls one or more of the endpoints listed below.")

h3("Overview tab (all plans)")
endpoint_table([
    ("/api/reports/daily", "All roles", "Today's sales, expenses, net cash, receivables, payables, low-stock count", "Dashboard tab"),
    ("/api/reports/weekly", "Shop+", "This week: sales, expenses, est. profit, top products, top debtors", "Dashboard tab"),
    ("/api/reports/cash-position", "Shop+", "Month-to-date sales/expenses/cash in + receivables/payables/net position", "Dashboard tab"),
    ("/api/reports/stockout-predictions", "All roles", "Products predicted to run out based on sales velocity", "Dashboard tab"),
    ("/api/reports/dead-stock", "All roles", "Products with stock but no sales in 14+ days", "Dashboard tab"),
    ("/api/reports/profit-by-product", "All roles", "Per-product revenue/cost/profit/margin over the last 30 days", "Dashboard tab"),
    ("/api/reports/staff-sales", "All roles", "Today's per-staff sales breakdown (own-sales for Sales role, all-staff for others)", "Dashboard tab"),
])

h3("Financial tab (Shop+)")
endpoint_table([
    ("/api/reports/monthly-pnl", "Shop+", "Revenue, COGS, gross profit, opex, net profit with previous-month comparison", "Dashboard tab"),
    ("/api/reports/expense-breakdown", "Shop+", "Expense category totals with percentages for a given month", "Dashboard tab"),
    ("/api/reports/monthly-trend", "Shop+", "12-month revenue/expenses/profit line chart data", "Dashboard tab"),
    ("/api/reports/avg-transaction-value", "Shop+", "12-month monthly average basket size trend", "Dashboard tab"),
    ("/api/reports/payment-method-split", "Shop+", "Cash/Transfer/POS/Credit/Other split by month over 6 months", "Dashboard tab"),
    ("/api/reports/sales-heatmap", "Shop+", "7x24 day/hour heatmap of sales volume in Africa/Lagos time", "Dashboard tab"),
])

h3("Customers tab (Shop+)")
endpoint_table([
    ("/api/reports/aging-receivables", "Shop+", "Per-customer debt broken into 0-30/31-60/61-90/90+ day buckets", "Dashboard tab"),
    ("/api/reports/top-customers", "Shop+", "Revenue-ranked customers with concentration-risk flag when >40%", "Dashboard tab"),
    ("/api/reports/customer-reliability", "Shop+", "Per-customer avg days-to-pay with Prompt/Regular/Slow/Late classification", "Dashboard tab"),
    ("/api/reports/customer-retention", "Shop+", "New vs. returning customer counts and revenue share over 6 months", "Dashboard tab"),
    ("/api/reports/product-affinity", "Shop+", "Top product pairs frequently bought together over the last 90 days", "Dashboard tab"),
])

h3("Inventory tab (Shop+)")
endpoint_table([
    ("/api/reports/inventory-turnover", "Shop+", "Per-product velocity, days-of-stock, turnover ratio, Fast/Healthy/Slow/Dead class", "Dashboard tab"),
    ("/api/reports/reorder-suggestions", "Shop+", "Products needing restock with suggested qty and Critical/High/Normal urgency", "Dashboard tab"),
    ("/api/reports/wastage", "Shop+", "Total value and top products damaged/written off in the last 30 days", "Dashboard tab"),
])

h3("Debts tab (Shop+)")
endpoint_table([
    ("/api/reports/aging-payables", "Shop+", "Per-supplier debt broken into 0-30/31-60/61-90/90+ day buckets", "Dashboard tab"),
])

# ═══════════════════════════════ Part 3: Dashboard home & activity ═══════════════════════════════
story.append(PageBreak())
h2("Part 3 — Dashboard home and activity feed")

h3("Dashboard home")
p("<b>URL:</b> <code>https://app.bizpilot-ai.com/</code>")
p("At-a-glance tiles: today's sales/expenses/net, outstanding receivables and payables, low-stock count, 7-day sales trend sparkline, 7-day expense trend sparkline, recent activity feed.")
p("<b>Backing endpoints:</b>")
endpoint_table([
    ("/api/dashboard/overview", "All roles (view_own_reports)", "Headline numbers for today plus 7-day sparkline data", "Loaded on page open"),
    ("/api/dashboard/recent-activity", "All roles", "Last 10 transactions across sales, expenses, inventory", "Loaded on page open"),
    ("/api/dashboard/insights", "Shop+", "Top products, expense categories, payment status breakdown, aging summary, top customers", "Loaded on page open"),
])

h3("Activity feed")
p("<b>URL:</b> <code>https://app.bizpilot-ai.com/activity</code>")
p("Paginated feed of every recorded action (sales, expenses, inventory, payments). Filterable by type.")
p("<b>Backing endpoint:</b> <code>GET /api/dashboard/activity?type=&limit=50&offset=0</code>")

# ═══════════════════════════════ Part 4: Operator dashboards ═══════════════════════════════
story.append(PageBreak())
h2("Part 4 — Operator dashboards (on-call / debugging)")

h3("Hangfire dashboard")
p("Lists every background job: summary jobs, trial reminders, trial reverts, CSV imports. Shows running, succeeded, failed, and scheduled. Clicking into a job shows the full exception trace if it failed.")

p("<b>Access is loopback-only by design.</b> The Hangfire dashboard exposes sensitive info (exception traces, job arguments that can contain IDs). The <code>HangfireLocalAuthFilter</code> rejects any request whose RemoteIpAddress isn't loopback, so you can only reach it via SSH tunnel from the Hetzner box itself.")

p("<b>How to access from your Mac:</b>")
code("""# Open a tunnel mapping local port 9000 to the API's port 5000 on the server
ssh -N -L 9000:localhost:5000 bizpilot@46.225.108.35

# In another terminal, open the dashboard in your browser
open http://localhost:9000/hangfire""")

p("Leave the ssh command running in a terminal while you use the dashboard; kill it (Ctrl-C) when done.")

spacer(0.1)

h3("Import job history (per-business)")
p("Owners and staff with import permissions can see their own past imports. Not admin-only.")
p("<b>URL:</b> <code>https://app.bizpilot-ai.com/import</code> (history appears inline)")
endpoint_table([
    ("/api/import/jobs", "Business members with csv_import", "Last 20 import jobs with status, progress, error counts", "Dashboard inline"),
    ("/api/import/jobs/{id}", "Same", "Single job detail — status, errors, timestamps", "Polled by frontend"),
])

# ═══════════════════════════════ Part 5: Database-level introspection ═══════════════════════════════
story.append(PageBreak())
h2("Part 5 — Database-level introspection (fallback)")

p("When the dashboards don't answer your question, drop into psql on the server. This is the lowest-level but highest-fidelity view of the system.")

h3("Getting a psql session")
code("""ssh bizpilot@46.225.108.35
# The DB creds are in the environment of the running service:
sudo cat /proc/$(pgrep -f BizPilot.API)/environ | tr '\\0' '\\n' | grep -i connection
# Or via systemd config:
systemctl show bizpilot-api --property=Environment | tr ' ' '\\n' | grep -i connection

# Connect:
PGPASSWORD='<password>' psql -h localhost -U <user> -d <db>""")

spacer(0.1)

h3("Common diagnostic queries")

p("<b>Recent bot messages for a specific user:</b>")
code("""SELECT "ParsedIntent", "ProcessingStatus", "ConfidenceScore", "RawMessage", "CreatedAtUtc"
FROM "MessageLogs"
WHERE "BusinessId" = (SELECT "BusinessId" FROM "Users" WHERE "PhoneNumber" LIKE '%<last10>')
  AND "CreatedAtUtc" > now() - interval '1 hour'
ORDER BY "CreatedAtUtc" DESC
LIMIT 30;""")

p("<b>Import jobs in last 24 hours:</b>")
code("""SELECT "Id", "Type", "Status", "TotalRows", "ProcessedRows", "SuccessCount", "ErrorCount", "CreatedAtUtc"
FROM "ImportJobs"
WHERE "CreatedAtUtc" > now() - interval '24 hours'
ORDER BY "CreatedAtUtc" DESC;""")

p("<b>Pending-action state by user (should almost always be empty):</b>")
code("""SELECT "UserId", "Intent", "AwaitingField", "QuestionText", "CreatedAtUtc", "ExpiresAtUtc"
FROM "PendingActions"
ORDER BY "CreatedAtUtc" DESC;""")

p("<b>Migration history (confirm what's been applied in prod):</b>")
code("""SELECT "MigrationId" FROM "__EFMigrationsHistory"
ORDER BY "MigrationId" DESC LIMIT 10;""")

p("<b>Plan distribution across customers:</b>")
code("""SELECT "Plan", COUNT(*) as businesses
FROM "Businesses"
WHERE "IsActive" = true
GROUP BY "Plan"
ORDER BY businesses DESC;""")

p("<b>Paystack webhook history (confirm events are arriving):</b>")
code("""SELECT "EventId", "EventType", "ReceivedAtUtc"
FROM "PaystackEventLogs"
ORDER BY "ReceivedAtUtc" DESC
LIMIT 20;""")

# ═══════════════════════════════ Part 6: Business-level health signals ═══════════════════════════════
story.append(PageBreak())
h2("Part 6 — Business-level health signals (what to watch weekly)")

p("A short checklist to review every week. Each signal points you at action if it's off.")

h3("Weekly admin checklist (5 minutes)")

p("1. <b>Visit <code>/admin/telemetry?key=...&days=7</code></b>")
small("• Overall misparse rate < 5%? If higher, look at byIntent for outliers.")
small("• Confidence distribution 0.90-1.00 bucket > 70%? If lower, something has shifted.")
small("• Any top-failure cluster with count > 10? Add to corpus.")
small("• More than 10 retry chains? Users are frustrated — triage them.")

p("2. <b>Visit <code>/admin/analytics?key=...</code></b>")
small("• Funnel drop-off between 'started' and 'business_name' > 20%? First prompt is confusing.")
small("• 'onboarding:expired' count high? Users abandon mid-signup, try shorter flow or better retention message.")
small("• New signups last 7 days — does the growth rate match expectations?")

p("3. <b>SSH + Hangfire check</b>")
small("• Any jobs in Failed state for recurring jobs (daily-summary, weekly-summary, trial-reminders)?")
small("• Any import jobs stuck in Running for > 10 minutes?")

p("4. <b>Psql spot-checks</b>")
small("• Recent Paystack events present? If there's a gap > 6 hours during business hours, webhook may be broken.")
small("• PendingActions table has few or zero rows (they should expire within 10 min)? Large numbers mean users are abandoning mid-flow.")

spacer(0.1)

h3("What to do when a signal goes red")
p("• <b>Misparse rate spike in one intent:</b> use top-failures to see which phrasings are failing. Add corpus entries. Iterate on system prompt. Deploy. Re-check next week.")
p("• <b>Confidence distribution shift:</b> usually upstream model change or a prompt regression from your recent deploy. Check if a recent deploy changed the prompt. If not, test with older corpus entries to see if Claude behavior changed.")
p("• <b>Hangfire job stuck or failing:</b> click into the job in the Hangfire dashboard, copy the exception, check the runbook (<code>docs/runbook.md</code>) for similar patterns.")
p("• <b>User complains bot didn't understand them:</b> use the runbook 5-step flow. SQL lookup, parse diagnosis, corpus entry, prompt fix.")

# ═══════════════════════════════ Quick reference ═══════════════════════════════
story.append(PageBreak())
h2("Quick reference — all URLs")

h3("Admin (you)")
p("• <code>https://app.bizpilot-ai.com/admin/analytics?key=&lt;admin-key&gt;</code> — onboarding funnel")
p("• <code>https://app.bizpilot-ai.com/admin/telemetry?key=&lt;admin-key&gt;</code> — telemetry dashboard")

h3("Business owner / staff")
p("• <code>https://app.bizpilot-ai.com/</code> — home dashboard")
p("• <code>https://app.bizpilot-ai.com/reports</code> — all reports (5 tabs)")
p("• <code>https://app.bizpilot-ai.com/activity</code> — activity feed")
p("• <code>https://app.bizpilot-ai.com/sales</code>, <code>/expenses</code>, <code>/inventory</code>, <code>/contacts</code>, <code>/import</code>, <code>/settings</code>")

h3("Operator (loopback only — SSH tunnel)")
p("• <code>http://localhost:9000/hangfire</code> — Hangfire background-job dashboard")
p("• <code>psql -h localhost -U &lt;user&gt; -d &lt;db&gt;</code> — direct database access")

h3("Related documents in the repo")
p("• <code>docs/design-principles.md</code> — the six invariants for code review")
p("• <code>docs/runbook.md</code> — 5-step flow for diagnosing user complaints")
p("• <code>BizPilot.Tests/Corpus/conversation-corpus.yml</code> — regression test corpus")
p("• <code>scripts/BizPilot-Deploy-Scripts-Reference.pdf</code> — deploy and rollback")
p("• <code>~/Desktop/BizPilot-Testing-Plan.pdf</code> — post-audit comprehensive test plan")


# ═══════════════════════════════ Build ═══════════════════════════════
doc.build(story)
print(f"✅ Wrote {OUT}")

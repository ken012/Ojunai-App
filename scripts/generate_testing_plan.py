#!/usr/bin/env python3
"""Generate the BizPilot post-audit comprehensive testing plan PDF."""
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import inch
from reportlab.lib.colors import HexColor
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, PageBreak, Preformatted, Table, TableStyle
)
import os

OUT = os.path.expanduser("~/Desktop/BizPilot-Testing-Plan.pdf")

styles = getSampleStyleSheet()
H1 = ParagraphStyle('H1', parent=styles['Heading1'], fontSize=22, spaceAfter=14, textColor=HexColor("#0ea5e9"))
H2 = ParagraphStyle('H2', parent=styles['Heading2'], fontSize=15, spaceBefore=16, spaceAfter=8, textColor=HexColor("#0f172a"))
H3 = ParagraphStyle('H3', parent=styles['Heading3'], fontSize=12, spaceBefore=10, spaceAfter=4, textColor=HexColor("#475569"))
BODY = ParagraphStyle('Body', parent=styles['BodyText'], fontSize=10, leading=14, spaceAfter=6, textColor=HexColor("#1e293b"))
CODE = ParagraphStyle('Code', parent=styles['Code'], fontSize=8.5, leading=11, leftIndent=10, backColor=HexColor("#f1f5f9"), textColor=HexColor("#0f172a"), borderPadding=6)
SMALL = ParagraphStyle('Small', parent=styles['BodyText'], fontSize=9, leading=12, spaceAfter=4, textColor=HexColor("#64748b"))
NOTE = ParagraphStyle('Note', parent=styles['BodyText'], fontSize=9, leading=12, spaceAfter=6, textColor=HexColor("#64748b"), leftIndent=12)

def code(text):
    return Preformatted(text, CODE)

def section(title, body_paragraphs):
    story.append(Paragraph(title, H2))
    for p in body_paragraphs:
        story.append(p)

def subsection(title):
    story.append(Paragraph(title, H3))

def p(text):
    story.append(Paragraph(text, BODY))

def note(text):
    story.append(Paragraph(text, NOTE))

def case_table(rows):
    """rows = list of (ID, Scenario, Expected) tuples. Header is added automatically."""
    data = [["ID", "Scenario", "Expected result"]]
    for r in rows:
        data.append([Paragraph(r[0], SMALL), Paragraph(r[1], SMALL), Paragraph(r[2], SMALL)])
    t = Table(data, colWidths=[0.5*inch, 2.8*inch, 3.5*inch], repeatRows=1)
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
    story.append(Spacer(1, 0.1*inch))


doc = SimpleDocTemplate(OUT, pagesize=letter, leftMargin=0.7*inch, rightMargin=0.7*inch,
                        topMargin=0.8*inch, bottomMargin=0.8*inch)
story = []

# ══════════════════════════ Cover ══════════════════════════
story.append(Paragraph("BizPilot AI", H1))
story.append(Paragraph("Post-Audit Comprehensive Testing Plan",
             ParagraphStyle('sub', parent=BODY, fontSize=14,
                            textColor=HexColor("#475569"), spaceAfter=12)))
p("Covers every behavior added or changed during the security hardening rounds, CSV async refactor, "
  "and the fifteen new advanced reports. Work through each section before releasing to paying customers. "
  "The goal is to catch regressions in existing flows and confirm new flows behave correctly on the happy "
  "path and at their sharpest edges.")
story.append(Spacer(1, 0.1*inch))

p("<b>How to use this document.</b> Each section has two kinds of content: a short prose explanation of "
  "what to look for, and a table of concrete test cases with IDs you can reference in a tracking sheet. "
  "Do not skip the prose — it explains why a case matters. Mark each row pass/fail as you go; anything "
  "flagged fail blocks release.")

p("<b>Environment.</b> Run most tests in a staging copy of production if you have one. If you only have "
  "live production, use a throwaway test business that nobody depends on and clean up after yourself.")

p("<b>Required accounts.</b> You need access to at least one Owner account, one Admin, one Sales, one "
  "Bookkeeper, and one Viewer — ideally all on the same test business so permission tests are fast. "
  "For plan-gating tests, you also need one Starter-tier business and one Shop+ business.")

p("<b>Tools.</b> A browser with devtools, a WhatsApp-connected phone, access to the production Postgres "
  "(read-only is fine for verification), and the <code>psql</code>, <code>curl</code>, and "
  "<code>journalctl</code> commands on the server.")

# ══════════════════════════ Section 1: Deploy verification ══════════════════════════
story.append(PageBreak())
section("1. Deploy and Migration Verification", [])
p("Before testing features, confirm the deploy landed and migrations ran. Run these once, right after deploy.")

subsection("Commands to run on the Hetzner box")
story.append(code(
    "# Migrations present in DB\n"
    'psql -h localhost -U <user> -d <db> -c \'SELECT "MigrationId" FROM "__EFMigrationsHistory" \n'
    "  ORDER BY \"MigrationId\" DESC LIMIT 5;'\n"
    "# Expected top rows: AddImportJob, AddCaseInsensitiveIndexes\n\n"
    "# New table present\n"
    "psql -h localhost -U <user> -d <db> -c '\\d \"ImportJobs\"'\n"
    "# Expected: 16 columns including RawCsvText, ProcessedRows, Status\n\n"
    "# Functional indexes present\n"
    "psql -h localhost -U <user> -d <db> -c \"SELECT indexname FROM pg_indexes \n"
    "  WHERE indexname LIKE '%NameLower';\"\n"
    "# Expected: IX_Products_BusinessId_NameLower, IX_Contacts_BusinessId_NameLower\n\n"
    "# Service health\n"
    "curl -fs http://localhost:5000/health && echo OK\n\n"
    "# No migration errors in startup logs\n"
    "sudo journalctl -u bizpilot-api -n 200 --no-pager | grep -iE 'migration|error'"
))

case_table([
    ("D1", "Last migration id in DB is AddImportJob", "Present. If an earlier id is last, the app didn't migrate — check logs."),
    ("D2", "ImportJobs table visible via \\d", "Exists with FK to Businesses and two indexes (BusinessId+CreatedAtUtc, Status)."),
    ("D3", "Two NameLower indexes exist", "Both indexes present. Absent means the additive SQL migration silently skipped."),
    ("D4", "/health returns 200 OK", "Returns {\"status\":\"ok\"}. Failure means the service didn't come back up."),
    ("D5", "No 'Migration failed on startup' in journal", "No such log line. If present, the app booted without migrations — rollback and investigate."),
    ("D6", "Dashboard loads at app.bizpilot-ai.com", "Login page renders, no 500s in browser devtools Network tab."),
])

# ══════════════════════════ Section 2: Authentication & Session ══════════════════════════
story.append(PageBreak())
section("2. Authentication, Sessions, and Account Lockout", [])
p("The hardening work introduced TokenVersion-based session invalidation, account lockout on repeated "
  "failed logins, and per-request active-user validation. These must all behave correctly before any "
  "feature testing — broken auth invalidates everything downstream.")

subsection("Login, lockout, password reset")
case_table([
    ("A1", "Login with correct credentials", "Returns a JWT. mustChangePassword is false for established users."),
    ("A2", "Login with wrong password 4 times", "Each returns 401 with 'invalid credentials'. No lockout yet."),
    ("A3", "Login with wrong password a 5th time", "401, user row has LockoutEndsAtUtc set ~15 minutes in future."),
    ("A4", "Login with correct password during lockout", "401 with 'Account temporarily locked'. Even valid creds are blocked."),
    ("A5", "Login with correct password after lockout expires", "Succeeds. FailedLoginAttempts resets to 0."),
    ("A6", "Forgot password flow: request code for existing active user", "WhatsApp/email code delivered. Reset works with that code."),
    ("A7", "Forgot password flow: request code for deactivated user", "Silent no-op. No enumeration leak — response looks the same as success."),
    ("A8", "Reset password completes", "User must set new password. TokenVersion incremented. All old JWTs rejected on next request."),
    ("A9", "Change password from dashboard", "Succeeds. TokenVersion incremented. Current session either keeps working with refresh or prompts re-login."),
])

subsection("TokenVersion invalidation and cross-tenant protection")
p("The ActiveUserMiddleware runs on every authenticated request. Test that tampering with JWTs or "
  "holding stale tokens is rejected.")
case_table([
    ("A10", "Take a valid JWT, increment tokenVersion claim in it, hit any authed endpoint", "401 'Session expired'. The signature breaks, but even if signed correctly the middleware would reject on version mismatch."),
    ("A11", "Copy a JWT from Business A and swap the businessId claim to Business B (requires forging)", "401 'Invalid session'. businessId in claim must match DB."),
    ("A12", "Log in as user X, then from another tab have an admin reset user X's password. Refresh the first tab.", "First tab gets 401 and redirects to login."),
    ("A13", "Log in as user X, then deactivate user X via StaffController. Refresh any page.", "401 'Account is inactive'. No access even with unexpired JWT."),
    ("A14", "Deactivate an Owner's business from AdminController. Any user in that business hits any endpoint.", "401 'Account is inactive' (business.IsActive gate)."),
])

# ══════════════════════════ Section 3: Deleted staff handling ══════════════════════════
story.append(PageBreak())
section("3. Deleted Staff Handling", [])
p("This was a dedicated audit round. The attack model: a staff member deactivated at 10:00 should be "
  "unable to access anything by 10:00:01, via any channel, and any reactivation path should start from "
  "a clean state.")

subsection("Removal flow")
case_table([
    ("S1", "Owner removes staff member via DELETE /api/staff/{id}", "200 OK. staff.IsActive=false, staff.TokenVersion+1, staff.PasswordResetCode=null."),
    ("S2", "Deactivated staff hits any authed endpoint with their old JWT", "401 'Account is inactive'. Middleware blocks at request entry."),
    ("S3", "Deactivated staff sends a WhatsApp message", "Either ignored (no active user match) or treated as new onboarding menu. They do NOT reach any handler."),
    ("S4", "Deactivated staff appears in GET /api/staff", "Does not appear. GetAll filters IsActive."),
    ("S5", "Owner attempts to remove themselves", "400 'You cannot deactivate yourself'."),
    ("S6", "Admin attempts to remove the Owner", "400 'Cannot remove the business owner'."),
    ("S7", "Deactivated staff row still exists with Sales, Expenses they recorded", "Historical records retain RecordedByName. No FK errors in any reports."),
])

subsection("Reactivation flow (dashboard AddStaff)")
case_table([
    ("S8", "Owner adds a staff member whose phone matches a deactivated user in same business",
        "User row reactivated: IsActive=true, FailedLoginAttempts=0, LockoutEndsAtUtc=null, PasswordResetCode=null, TokenVersion incremented. New password accepted."),
    ("S9", "Owner adds staff whose phone belongs to a deactivated user in ANOTHER business",
        "400 'Phone number is linked to another business'. Blocked."),
    ("S10", "Owner adds staff whose phone is ACTIVE in another business",
        "400 'Phone number already registered'. Blocked."),
    ("S11", "After reactivation, the old WhatsApp setup code from the prior lifetime is rejected",
        "Reset code was cleared, so any attempt to use it fails with 'Invalid or expired code'."),
])

subsection("Reactivation flow (WhatsApp add staff)")
case_table([
    ("S12", "Owner messages 'add staff Mary 08012345678' where Mary is a previously-removed staff at same business",
        "Reactivation path applies: clears lockout state, bumps TokenVersion, generates fresh setup code. Mary gets welcome message."),
    ("S13", "Same as S12 but Mary was deactivated at another business",
        "Response says phone is linked to another business. No reactivation."),
])

subsection("Password reset and role change")
case_table([
    ("S14", "Admin calls POST /api/staff/{id}/reset-password on a deactivated user",
        "404 'Staff not found' — endpoint filters IsActive. Deactivated users cannot be 'reset' back into access."),
    ("S15", "Admin calls POST /api/staff/{id}/reset-password on an active user",
        "200. Password hash updated, MustChangePassword=true, TokenVersion+1. User's existing sessions terminate."),
    ("S16", "Admin calls PUT /api/staff/{id}/role on a deactivated user",
        "404 'Staff not found'. Deactivated users cannot have role changed back in."),
    ("S17", "Admin changes active staff role from Sales to Admin",
        "Role updated. TokenVersion+1. User's JWT contains old role until they re-login."),
    ("S18", "Attempt to change Owner's role via PUT /api/staff/{id}/role",
        "400 'Cannot change the owner's role'."),
])

# ══════════════════════════ Section 4: Permissions and plan gating ══════════════════════════
story.append(PageBreak())
section("4. Permissions and Plan Gating", [])
p("Every endpoint that restricts access must enforce the restriction at the controller layer. "
  "Test systematically across the five roles (Owner, Admin, Sales, Bookkeeper, Viewer) and the four "
  "plan tiers (Starter, Shop, Pro, Business).")

subsection("Role-based permission checks")
p("For each endpoint category, log in as each role and attempt the action. Expected outcomes match "
  "RolePermissions.cs.")
case_table([
    ("P1", "Sales role attempts POST /api/expenses", "403 'Permission denied. Sales does not have record_expenses'."),
    ("P2", "Viewer role attempts POST /api/sales", "403."),
    ("P3", "Bookkeeper role attempts POST /api/staff", "403 (no manage_staff permission)."),
    ("P4", "Admin role attempts POST /api/business/start-trial", "403 (ManageSettings is Owner-only)."),
    ("P5", "Admin role attempts GET /api/business/export", "403 (GDPR export gated on ManageSettings)."),
    ("P6", "Admin role attempts DELETE /api/business", "403."),
    ("P7", "Viewer role views /api/reports/daily", "200 — has view_own_reports."),
    ("P8", "Sales role views /api/reports/weekly", "403 — weekly needs view_all_reports."),
    ("P9", "Sales role views /api/reports/staff-sales?staffName=<self>", "200 — own sales visible via view_own_reports."),
])

subsection("Plan gates on advanced features")
p("Run each test on a Starter-tier business first (expect block), then on a Shop+ business (expect pass).")
case_table([
    ("P10", "Starter business attempts GET /api/reports/aging-receivables", "400 with upgrade message pointing at Shop."),
    ("P11", "Starter business attempts GET /api/reports/monthly-pnl", "400 upgrade message."),
    ("P12", "Starter business attempts GET /api/reports/inventory-turnover", "400 upgrade message."),
    ("P13", "Starter business attempts POST /api/import/sales", "400 'CSV Import is available on Pro...' (csv_import gate)."),
    ("P14", "Starter business WhatsApp: 'I owe Tunde 100k'", "Bot replies with 'Ledger is available on the Shop plan...' — plan gate at handler level."),
    ("P15", "Starter business WhatsApp: 'Ada paid 50k'", "Same plan gate block."),
    ("P16", "Shop business attempts all of the above", "All succeed (or produce correct business-logic output)."),
    ("P17", "Pro business during active trial of Business tier attempts multi-branch feature", "Succeeds during trial."),
    ("P18", "Same business after trial expires plus 3-day grace period", "Blocked — reverts to Pro features."),
])

# ══════════════════════════ Section 5: GDPR endpoints ══════════════════════════
story.append(PageBreak())
section("5. GDPR Data Export and Account Closure", [])
p("Two new endpoints, both Owner-only. The export must include every business-scoped entity without "
  "leaking credentials. The deletion must anonymize PII and invalidate all sessions.")

subsection("Export (GET /api/business/export)")
case_table([
    ("G1", "Owner downloads export", "JSON attachment. Content-Disposition filename contains business name and date."),
    ("G2", "Export payload has Users array", "Present. Each entry has FullName, PhoneNumber, Email, Role, IsActive — but NO PasswordHash, PasswordResetCode, or TokenVersion."),
    ("G3", "Export payload has Business object", "Present. Contains profile fields but NO PaystackCustomerCode, PaystackSubscriptionCode, PaystackPlanCode."),
    ("G4", "Export includes voided sales", "IsDeleted=true sales are present. Item rows are nested under each sale."),
    ("G5", "Export includes MessageLogs", "WhatsApp log entries present with RawMessage but NOT ParsedPayloadJson (kept lean)."),
    ("G6", "Admin attempts export", "403 — ManageSettings is Owner-only."),
    ("G7", "Cross-tenant attempt: manipulate JWT to hit another business's export", "ActiveUserMiddleware catches businessId mismatch. 401."),
    ("G8", "Large business (1000+ products, 10k+ sales) export completes", "Succeeds within reasonable time. No timeout. File downloads cleanly."),
])

subsection("Account closure (DELETE /api/business)")
case_table([
    ("G9", "Owner calls DELETE with wrong ConfirmationPassword", "401 'Password does not match'. No side effects."),
    ("G10", "Owner calls DELETE with wrong Confirm phrase", "400 'You must type DELETE MY ACCOUNT exactly'. No side effects."),
    ("G11", "Owner calls DELETE with both correct", "200. Business marked inactive. Paystack cancellation attempted (best-effort). All User rows: FullName='Deleted User', Email=null, PhoneNumber='deleted-<guid>', TokenVersion+1. All Contacts anonymized. Business name overwritten to 'Closed Account XXXX'."),
    ("G12", "After closure, any user tries to log in", "401 'Account is inactive' via middleware."),
    ("G13", "After closure, historical sales records retained", "Financial rows untouched; only PII fields on Users/Contacts/Business overwritten."),
    ("G14", "After closure, the phone number of the ex-Owner can be used for a new signup", "Succeeds — unique suffix means the deleted row doesn't conflict."),
    ("G15", "Paystack webhook fires for a closed business", "Gracefully handled (no crash). Business remains inactive."),
])

# ══════════════════════════ Section 6: CSV async imports ══════════════════════════
story.append(PageBreak())
section("6. CSV Async Imports", [])
p("Imports now queue through Hangfire. The endpoint returns immediately with a job ID, and the worker "
  "processes rows in the background, batching SaveChanges every 200 rows. Test the happy path plus "
  "failure modes (bad CSV, oversized file, concurrent imports).")

subsection("Happy path")
case_table([
    ("C1", "Upload a 5-row inventory CSV", "202 Accepted with job payload. Status starts Queued, progresses to Running, then Completed. All 5 imported."),
    ("C2", "Poll GET /api/import/jobs/{id} during Running", "Returns ProcessedRows increasing. ProgressPercent matches."),
    ("C3", "Owner receives WhatsApp message on completion", "Message contains file name, imported count, skipped count, total rows."),
    ("C4", "Post-completion ImportJob row has RawCsvText NULL", "Confirmed — freed up after processing. FileName preserved."),
    ("C5", "GET /api/import/jobs lists recent jobs", "Returns up to 20 jobs for this business, newest first."),
    ("C6", "Dashboard import page shows progress bar while Running", "Progress bar fills from 0 to 100%. Current row number updates visibly."),
    ("C7", "Dashboard shows success summary with WhatsApp notice on completion", "'A WhatsApp message has been sent...' note appears. 'Start another import' button works."),
])

subsection("Large file and error handling")
case_table([
    ("C8", "Upload a valid 10,000-row inventory CSV", "Queues. Worker batches 200 rows. Completes without timeout. Memory stable on server."),
    ("C9", "Upload a 100,001-row CSV", "400 'File has too many rows. Maximum is 100,000'. No job created."),
    ("C10", "Upload a 60MB CSV", "400 'File too large. Maximum size is 50MB'."),
    ("C11", "Upload a CSV with 100 valid rows and 5 malformed rows", "Status=Completed, SuccessCount=100, ErrorCount=5. Errors array contains specific row messages."),
    ("C12", "Upload a totally corrupt CSV (e.g. binary file)", "Either rejected at parse (400) or job completes with all rows errored. No server crash."),
    ("C13", "Upload a CSV for a product that doesn't exist (sales import)", "Row errored with 'Product not found'. Other rows succeed."),
    ("C14", "Upload 3 CSVs simultaneously from the same business", "All queue. Hangfire processes in parallel (up to its worker count). Each job completes independently."),
    ("C15", "Force an exception mid-import (e.g. DB connection drop)", "Job status=Failed. FailureReason populated. Owner gets no completion WhatsApp (best-effort)."),
    ("C16", "Poll a jobId belonging to a different business (tenant leak attempt)", "404 'Import job not found'. Tenant-scoped query."),
])

subsection("Permissions and plan gate")
case_table([
    ("C17", "Viewer role attempts POST /api/import/inventory", "403 (ManageStock required)."),
    ("C18", "Sales role attempts POST /api/import/expenses", "403 (RecordExpenses required)."),
    ("C19", "Starter business attempts any import", "400 upgrade message (csv_import gate)."),
])

# ══════════════════════════ Section 7: Advanced reports ══════════════════════════
story.append(PageBreak())
section("7. Advanced Reports (15 new endpoints)", [])
p("Each report should return the right shape, enforce the plan gate, and display correctly in the "
  "dashboard's new tabs. Use a business with realistic data — several months of sales, at least a "
  "dozen products, some credit customers, some voided sales — so you can eyeball whether the numbers "
  "are sane.")

subsection("Aging receivables and payables")
case_table([
    ("R1", "GET /api/reports/aging-receivables on a business with mixed-age debts", "Returns contacts grouped into 4 buckets. Bucket totals sum to GrandTotal. Oldest days correct."),
    ("R2", "Create a receivable 45 days ago, no payment", "Appears in 31-60 bucket."),
    ("R3", "Create a receivable 100 days ago, partial payment last week", "Remaining amount in 90+ bucket (FIFO allocation)."),
    ("R4", "Create a receivable today, fully paid", "Does not appear in aging (net zero)."),
    ("R5", "Dashboard Customers tab shows Aging Receivables with 4-bucket grid", "All four cards render. Table lists customers ordered by 90+ descending."),
    ("R6", "Aging Payables with the same structure", "Debts tab renders it identically."),
])

subsection("Monthly P&L")
case_table([
    ("R7", "GET /api/reports/monthly-pnl with no month param", "Returns current month. Revenue = sum of sales. COGS = sum of qty*costPrice."),
    ("R8", "With ?month=2026-01-01 query", "Returns January figures. Previous month = December."),
    ("R9", "Products without cost price set", "Excluded from COGS. IsEstimate=true flags this."),
    ("R10", "Dashboard Financial tab P&L card shows current vs previous month with delta arrows", "Delta percentages render. Net Profit highlighted in colored box."),
])

subsection("Expense breakdown, monthly trend, avg transaction, payment methods")
case_table([
    ("R11", "Expense breakdown for current month", "Categories sum to 100%. Ordered largest first. Inventory expenses separated from operating expenses."),
    ("R12", "Monthly trend 12 months line chart", "Revenue, Expenses, Profit lines render. Profit = Revenue - Expenses."),
    ("R13", "Avg transaction value over 12 months", "Line chart renders. Null months show zero."),
    ("R14", "Payment method split — record a cash sale, a transfer sale, an unpaid sale", "Each appears in the right bucket. Unpaid sales land in Credit regardless of PaymentMethod field."),
    ("R15", "Payment method split stacked bar chart", "Renders with 5 series (Cash, Transfer, POS, Credit, Other)."),
])

subsection("Sales heatmap")
case_table([
    ("R16", "Record a sale on a Tuesday at 3pm Lagos time", "Heatmap cell (DayOfWeek=2, Hour=15) increments."),
    ("R17", "Heatmap peak day/hour displayed above grid", "Correct day label and hour shown."),
    ("R18", "Empty cells render as light background, intense cells darker", "Visual gradient works. Tooltip shows amount on hover."),
])

subsection("Top customers and concentration")
case_table([
    ("R19", "With 1 customer making 50%+ of revenue", "ConcentrationRisk=true. Dashboard shows amber banner."),
    ("R20", "With 20 customers, each contributing ~5%", "ConcentrationRisk=false. No banner."),
    ("R21", "Anonymous cash sales (no Contact)", "Excluded from top-customers aggregation (contact is null)."),
])

subsection("Customer reliability and retention")
case_table([
    ("R22", "Customer with 3 paid receivables averaging 5 days to pay", "Classification=Prompt. AverageDaysToPay=5."),
    ("R23", "Customer with 2 receivables paid after 60+ days", "Classification=Slow or Late."),
    ("R24", "Customer with no paid receivables (all outstanding)", "Excluded from reliability list entirely."),
    ("R25", "New vs returning: customer A makes first purchase this month", "Counted as New in this month."),
    ("R26", "Customer A makes a second purchase next month", "Counted as Returning next month. First purchase still New this month."),
])

subsection("Inventory turnover, reorder, wastage, affinity")
case_table([
    ("R27", "Product sold 30 units in last 30 days, 10 in stock", "DailyVelocity=1. DaysOfStockRemaining=10. Classification=Fast."),
    ("R28", "Product with zero sales in 30 days", "Classification=Dead. DaysOfStockRemaining=999."),
    ("R29", "Reorder suggestion for a product with 2 days of stock", "Urgency=Critical. SuggestedReorderQty >= 2*7*velocity."),
    ("R30", "Reorder suggestion excludes products with no velocity", "Dead products don't appear — no point reordering what doesn't sell."),
    ("R31", "Wastage report with 5 damaged events in last 30 days", "EventCount=5. TotalValue = sum of qty * cost."),
    ("R32", "Product affinity over 90 days: two products sold together 8 times", "Pair appears in affinity list with CoOccurrenceCount=8."),
    ("R33", "Single-item sales don't produce affinity pairs", "Verified — only multi-item sales contribute."),
])

subsection("Dashboard UI tests")
case_table([
    ("R34", "All 5 reports tabs render on a Shop+ account", "Overview, Financial, Customers, Inventory, Debts all clickable and load data."),
    ("R35", "On a Starter account, advanced tabs show upgrade prompts", "Upgrade card appears with 'Shop' as minimum tier."),
    ("R36", "Charts responsive at narrow widths (600px, 400px)", "No horizontal overflow. Labels may truncate but chart stays usable."),
    ("R37", "Empty-data states render correctly", "Sections with no data show italic placeholder text, not loading spinners or errors."),
])

# ══════════════════════════ Section 8: WhatsApp flows ══════════════════════════
story.append(PageBreak())
section("8. WhatsApp Flows", [])
p("The WhatsApp channel has its own handlers for onboarding, sales, expenses, inventory, and ledger. "
  "Plus new plan gates on ledger handlers from the latest round. Test both new-user and existing-user "
  "flows.")

subsection("New-user onboarding")
case_table([
    ("W1", "Unknown phone sends 'hi'", "Menu prompt: 1=create business, 2=staff invite, 3=help."),
    ("W2", "Reply '1', walk through name/type/city/owner prompts, confirm", "Account created. Credentials DM'd. Starter plan, 30-day trial."),
    ("W3", "Reply '2'", "Instructions for getting added as staff."),
    ("W4", "During onboarding, reply 'restart'", "Progress reset. Starts from business name."),
    ("W5", "During onboarding, reply 'cancel'", "State deleted. Menu prompt on next message."),
    ("W6", "Pause 25 hours, then message", "State expired. Welcome menu shown again."),
    ("W7", "Pause 45 minutes, then message (not 'continue')", "Resume prompt: 'continue or restart?'"),
])

subsection("Business operations")
case_table([
    ("W8", "'Sold 5 bags of rice at 3000'", "Sale recorded. Inventory decremented."),
    ("W9", "'Sold 3 rice and 2 shampoo' (multi-item)", "Two-item sale recorded."),
    ("W10", "'Received 50 bottles of shampoo'", "Inventory increased. Expense auto-created if cost price known."),
    ("W11", "'Mary owes me 20k' (Starter business)", "Plan gate blocks: 'Ledger is available on the Shop plan...'"),
    ("W12", "Same command on Shop business", "Receivable created. Mary auto-created as Customer contact."),
    ("W13", "'Mary paid 10k' on Shop business", "Payment recorded. Mary's balance reduced."),
    ("W14", "'Clear all Sara's debt'", "Remaining Sara balance cleared in single transaction."),
    ("W15", "'Clear all debts'", "Every outstanding receivable cleared. Response lists who was cleared."),
    ("W16", "'Check stock'", "Current stock levels for all products returned."),
    ("W17", "'How much did I make today?'", "Today's sales total."),
])

subsection("Staff via WhatsApp")
case_table([
    ("W18", "Owner sends 'add staff Mary 08012345678'", "Mary added. Setup code DM'd to Mary."),
    ("W19", "Sales role attempts 'add staff ...'", "'You don't have permission to manage staff'."),
    ("W20", "Staff member sends 'Sold 5 bags'", "Sale recorded with RecordedByName=staff name."),
    ("W21", "Deactivated staff sends any message", "Does not reach handlers. New-user menu shown."),
])

# ══════════════════════════ Section 9: Security hardening ══════════════════════════
story.append(PageBreak())
section("9. Security Hardening (from prior audits)", [])
p("The earlier audit rounds added webhook signature validation, rate limiting, secure headers, "
  "concurrency tokens, and tenant-scoped indexes. Spot-check each area.")

subsection("Webhook security")
case_table([
    ("H1", "Paystack webhook with valid signature", "200 OK. Subscription state updated."),
    ("H2", "Paystack webhook with invalid signature", "400/401 — request rejected before processing. Use curl to forge."),
    ("H3", "Same Paystack event delivered twice (same event_id)", "Second delivery no-ops. PaystackEventLog prevents double-processing."),
    ("H4", "Twilio webhook with invalid Twilio signature", "Rejected."),
    ("H5", "Twilio webhook with non-text message (e.g. image)", "'I can only process text messages for now.'"),
])

subsection("Concurrency and race conditions")
case_table([
    ("H6", "Two staff record a sale for the last unit of a product at the same time", "One succeeds, the other fails with a concurrency error — not silently oversells."),
    ("H7", "Two requests attempt to add the same staff phone at the same moment", "Serializable transaction means one wins, the other gets 'Phone already registered'."),
    ("H8", "Two requests to create a stock hold for the same product", "Stock held once. Second request fails cleanly or gets queued behind the first."),
])

subsection("Rate limiting and headers")
case_table([
    ("H9", "Attempt 100 rapid login requests from one IP", "Rate limiter kicks in. 429 Too Many Requests returned."),
    ("H10", "Inspect any API response in browser devtools", "Headers include X-Content-Type-Options, X-Frame-Options=DENY, Referrer-Policy. In prod: Strict-Transport-Security."),
    ("H11", "Hangfire dashboard access from public IP", "403/403-like. Only loopback allowed by HangfireLocalAuthFilter."),
    ("H12", "Hangfire dashboard access via SSH tunnel", "Accessible. Jobs visible."),
])

subsection("Tenant isolation")
case_table([
    ("H13", "Log in as Business A. Manipulate any URL with a productId from Business B", "404 — all queries filter BusinessId."),
    ("H14", "Attempt cross-tenant via JWT tampering (forge businessId claim)", "Rejected by ActiveUserMiddleware (claim must match DB)."),
    ("H15", "Attempt cross-tenant GET /api/import/jobs/{id-from-other-business}", "404 — tenant scope enforced at controller query."),
])

# ══════════════════════════ Section 10: Performance & UX ══════════════════════════
story.append(PageBreak())
section("10. Performance and UX Checks", [])
p("Some changes were about speed and ergonomics, not correctness. Confirm they actually deliver.")
case_table([
    ("PF1", "Product name search on a business with 10k products", "Fast (<500ms). Functional LOWER() index should be used. Verify via EXPLAIN if suspect."),
    ("PF2", "Contact name lookup during sale import", "Fast. Same index benefit."),
    ("PF3", "Dashboard overview on a business with 12 months of data", "Loads within 2 seconds. No query timeouts."),
    ("PF4", "Reports tabs switch instantly after initial load", "React Query cache keeps tabs snappy on re-click."),
    ("PF5", "Sidebar nav on mobile width (<640px)", "Hamburger or drawer pattern renders. No horizontal scroll."),
    ("PF6", "Import page on mobile", "Upload zone and progress bar usable. Errors list scrollable."),
    ("PF7", "Heatmap on mobile", "Horizontal scroll allows viewing all 24 hours without breaking layout."),
])

# ══════════════════════════ Appendix: Release gate ══════════════════════════
story.append(PageBreak())
section("Release Gate Checklist", [])
p("Before flipping the new features on for paying customers, confirm these hard requirements.")
case_table([
    ("RG1", "All Section 1 (deploy verification) passes", "Required. Without this nothing else is meaningful."),
    ("RG2", "All Section 2 (auth) passes", "Required. Broken auth blocks everything."),
    ("RG3", "All Section 3 (deleted staff) passes", "Required. This was a known-gap area."),
    ("RG4", "Section 5 (GDPR) G1-G5 and G9-G13 pass", "Required for compliance posture."),
    ("RG5", "Section 6 (CSV async) happy path C1-C7 pass plus any two of C11, C12, C14", "Required."),
    ("RG6", "Section 7 (reports) at minimum R1-R10, R19-R20, R34-R35 pass", "Required — core advanced reports."),
    ("RG7", "Section 9 (security) H1-H7 pass", "Required. Webhook security is non-negotiable."),
    ("RG8", "Any failure in Sections 4, 8, 10 documented in issue tracker", "Acceptable to ship with known medium-severity issues if tracked."),
])

p("<b>If all required sections pass, you're clear to announce the new features to customers.</b> "
  "Anything in the nice-to-have rows can be handled as follow-up patches. Save this document alongside "
  "the sign-off once you've marked each case.", )

# ══════════════════════════ Build ══════════════════════════
doc.build(story)
print(f"✅ Wrote {OUT}")

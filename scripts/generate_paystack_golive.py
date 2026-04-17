#!/usr/bin/env python3
"""Generate the BizPilot Paystack Go-Live Checklist PDF."""
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import inch
from reportlab.lib.colors import HexColor
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, PageBreak, Preformatted, Table, TableStyle
)
import os

OUT = os.path.expanduser("~/Desktop/BizPilot-Paystack-GoLive.pdf")

styles = getSampleStyleSheet()
H1 = ParagraphStyle('H1', parent=styles['Heading1'], fontSize=22, spaceAfter=14, textColor=HexColor("#0ea5e9"))
H2 = ParagraphStyle('H2', parent=styles['Heading2'], fontSize=15, spaceBefore=16, spaceAfter=8, textColor=HexColor("#0f172a"))
H3 = ParagraphStyle('H3', parent=styles['Heading3'], fontSize=12, spaceBefore=10, spaceAfter=4, textColor=HexColor("#475569"))
BODY = ParagraphStyle('Body', parent=styles['BodyText'], fontSize=10, leading=14, spaceAfter=6, textColor=HexColor("#1e293b"))
CODE = ParagraphStyle('Code', parent=styles['Code'], fontSize=8.5, leading=11, leftIndent=10, backColor=HexColor("#f1f5f9"), textColor=HexColor("#0f172a"), borderPadding=6)
SMALL = ParagraphStyle('Small', parent=styles['BodyText'], fontSize=9, leading=12, spaceAfter=4, textColor=HexColor("#64748b"))
WARN = ParagraphStyle('Warn', parent=styles['BodyText'], fontSize=10, leading=14, spaceAfter=8, textColor=HexColor("#92400e"), leftIndent=10, borderColor=HexColor("#fbbf24"), borderWidth=1, borderPadding=8, backColor=HexColor("#fffbeb"))

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

def warn(text):
    story.append(Paragraph(text, WARN))

def spacer(height=0.1):
    story.append(Spacer(1, height*inch))

def checklist_table(rows):
    data = [["#", "Step", "Status"]]
    for i, (step, notes) in enumerate(rows, 1):
        data.append([
            Paragraph(str(i), SMALL),
            Paragraph(f"<b>{step}</b><br/><font size='8' color='#64748b'>{notes}</font>", SMALL),
            Paragraph("☐", SMALL)
        ])
    t = Table(data, colWidths=[0.3*inch, 5.5*inch, 0.7*inch], repeatRows=1)
    t.setStyle(TableStyle([
        ('BACKGROUND', (0, 0), (-1, 0), HexColor("#0ea5e9")),
        ('TEXTCOLOR', (0, 0), (-1, 0), HexColor("#ffffff")),
        ('FONTNAME', (0, 0), (-1, 0), 'Helvetica-Bold'),
        ('FONTSIZE', (0, 0), (-1, 0), 9),
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
    "Paystack Go-Live Checklist",
    ParagraphStyle('sub', parent=BODY, fontSize=14, textColor=HexColor("#475569"), spaceAfter=12)
))
p("Everything you need to do, verify, and watch out for when switching BizPilot from Paystack test mode to live payments. "
  "Work through each section in order. Do not skip the test-mode dry run — it catches most configuration mistakes before "
  "real money is involved.")

spacer(0.2)

# ═══════════════════════════════ Section 1: Prerequisites ═══════════════════════════════
h2("1. Paystack Account Prerequisites")

p("Before you can accept live payments, Paystack requires your business to be fully verified. These steps happen in the "
  "Paystack dashboard and take 1–3 business days for approval.")

checklist_table([
    ("Paystack business verification",
     "Dashboard → Settings → Business Settings. Upload CAC registration (if registered), utility bill, and valid ID. "
     "Paystack reviews and approves. You cannot switch to live mode until this is done."),

    ("Bank account linked",
     "Dashboard → Settings → Bank Details. Add the bank account where Paystack will settle your funds. "
     "Paystack does a ₦50 verification deposit. Confirm the amount to complete linking."),

    ("Live API keys generated",
     "Dashboard → Settings → API Keys &amp; Webhooks. Copy your <b>Secret Key</b> (starts with sk_live_) "
     "and <b>Public Key</b> (starts with pk_live_). You'll need the secret key for the server."),

    ("Webhook URL configured",
     "Same page. Set the webhook URL to:<br/>"
     "<font face='Courier' size='8'>https://api.bizpilot-ai.com/api/subscription/webhook</font><br/>"
     "This is where Paystack sends payment confirmations, subscription events, and failure notifications."),

    ("Notification preferences set",
     "Dashboard → Settings → Notifications. Enable email alerts for failed payments and subscription cancellations "
     "so you get notified when a customer's payment fails."),
])

warn("Do NOT share your live secret key with anyone or commit it to git. It allows full access to your Paystack account "
     "including creating charges and refunds. Treat it like a bank password.")

# ═══════════════════════════════ Section 2: Server Config ═══════════════════════════════
story.append(PageBreak())
h2("2. Server Configuration")

p("Switch the server from test keys to live keys. This is the point of no return — after this, any customer who clicks "
  "'Subscribe' will be charged real money.")

h3("Update the environment file")
story.append(code(
    "ssh bizpilot@46.225.108.35\n"
    "sudo nano /etc/bizpilot/api.env\n\n"
    "# Find the Paystack line and replace the test key with the live key:\n"
    "Paystack__SecretKey=sk_live_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\n\n"
    "# Save and exit (Ctrl-O, Enter, Ctrl-X)\n"
    "sudo systemctl restart bizpilot-api"
))

p("After restarting, verify the API is healthy:")
story.append(code("curl -fs https://api.bizpilot-ai.com/health && echo OK"))

spacer(0.1)

h3("What NOT to change")
p("Leave everything else as-is. Specifically:")
p("• <b>Do not change the webhook endpoint URL in your code.</b> It's already at <code>/api/subscription/webhook</code> — "
  "matching what you set in the Paystack dashboard.")
p("• <b>Do not change plan prices in code.</b> They're defined in <code>PlanLimits.cs</code> (Starter ₦3,500, Shop ₦7,500, "
  "Pro ₦12,500, Business ₦30,000). If you change them, update both the code AND the Paystack plans (or let the code "
  "auto-create new ones on next subscribe).")
p("• <b>Do not disable webhook signature verification.</b> The HMAC-SHA512 check in <code>SubscriptionController</code> "
  "prevents forged webhook calls from crediting fake subscriptions.")

# ═══════════════════════════════ Section 3: Test Mode Dry Run ═══════════════════════════════
story.append(PageBreak())
h2("3. Test-Mode Dry Run (do this BEFORE switching to live)")

p("Run through the full subscription lifecycle using Paystack's test mode and test card numbers. "
  "This validates that your webhook URL, signature verification, plan creation, and subscription state transitions "
  "all work correctly without touching real money.")

h3("Test card numbers (Paystack test mode)")
p("• <b>Successful charge:</b> 408 408 408 408 4081, expiry: any future date, CVV: 408, PIN: 0000, OTP: 123456")
p("• <b>Declined charge:</b> 408 408 408 408 4082 — use this to test failed-payment handling")

spacer(0.1)

h3("Dry-run checklist")
checklist_table([
    ("Subscribe to Starter from the dashboard",
     "Click 'Subscribe to Starter — ₦3,500/mo' on the settings page. Complete payment with test card 4081. "
     "Verify: redirected back to dashboard, plan shows 'Active subscription', SubscriptionEndsAt is set."),

    ("Check webhooks arrived",
     "On the server: <font face='Courier' size='8'>sudo journalctl -u bizpilot-api --since '5 min ago' | grep -i paystack</font><br/>"
     "You should see 'Subscription activated' and 'Payment successful' log lines. "
     "Also check the DB: <font face='Courier' size='8'>SELECT \"PaystackSubscriptionCode\", \"SubscribedPlan\", \"SubscriptionEndsAt\" FROM \"Businesses\" WHERE \"Id\"='...';</font>"),

    ("Check PaystackEventLog for idempotency",
     "<font face='Courier' size='8'>SELECT * FROM \"PaystackEventLogs\" ORDER BY \"ReceivedAtUtc\" DESC LIMIT 5;</font><br/>"
     "Each webhook should have a unique EventId recorded. Replaying the same webhook should be a no-op."),

    ("Verify WhatsApp payment confirmation was sent",
     "The owner should receive a WhatsApp message like '✅ Payment successful! Your Shop plan is now active.'"),

    ("Upgrade from Starter to Shop",
     "Click 'Upgrade to Shop'. Pay with test card. Verify plan changes to Shop. Check that the old Starter "
     "subscription was cancelled (PaystackSubscriptionCode changes)."),

    ("Downgrade from Shop to Starter",
     "Click 'Downgrade to Starter'. Verify PendingPlanChange is set in the DB. Verify the plan does NOT change "
     "immediately — it should stay on Shop until SubscriptionEndsAt passes."),

    ("Cancel subscription",
     "Click 'Cancel subscription'. Verify PaystackSubscriptionCode is cleared. If SubscriptionEndsAt is in the future, "
     "plan should stay. If past/null, plan should revert to Starter."),

    ("Test failed payment (optional but recommended)",
     "Use test card 4082 to simulate a decline. Check that the webhook fires and is logged. The plan should NOT "
     "change immediately — Paystack retries before giving up."),
])

warn("If ANY of these steps fail in test mode, fix the issue before switching to live keys. "
     "Common failures: webhook URL typo (404), signature mismatch (wrong secret key), plan price mismatch "
     "(code says ₦7,500 but Paystack plan says ₦75,000 — the code sends kobo, so 750000 kobo = ₦7,500).")

# ═══════════════════════════════ Section 4: Go-Live Steps ═══════════════════════════════
story.append(PageBreak())
h2("4. Go-Live Steps (the actual switch)")

p("Once the dry run passes cleanly in test mode, the switch to live is a 5-minute operation.")

checklist_table([
    ("Back up the production database",
     "<font face='Courier' size='8'>ssh bizpilot@46.225.108.35</font><br/>"
     "<font face='Courier' size='8'>PGPASSWORD='...' pg_dump -h localhost -U bizpilot -d bizpilot -Fc -f ~/pre-paystack-live.dump</font><br/>"
     "If anything goes wrong with the first live charge, you can restore to this point."),

    ("Switch the secret key in /etc/bizpilot/api.env",
     "Replace <font face='Courier' size='8'>sk_test_...</font> with <font face='Courier' size='8'>sk_live_...</font> "
     "and restart the service. See Section 2 for exact commands."),

    ("Verify the webhook URL in Paystack dashboard points to production",
     "<font face='Courier' size='8'>https://api.bizpilot-ai.com/api/subscription/webhook</font><br/>"
     "NOT localhost, NOT a test URL. Double-check this — a wrong webhook URL means payments succeed on Paystack's "
     "side but your app never learns about them."),

    ("Make one real test payment yourself",
     "Subscribe to Starter with your own card. Verify the full loop: payment charged → webhook arrives → "
     "plan activates → WhatsApp confirmation sent → DB state correct. Then cancel to get your money back "
     "(Paystack refunds take 3-5 business days)."),

    ("Confirm settlement account",
     "Paystack settles funds to your linked bank account on T+1 (next business day). Verify that the first "
     "settlement arrives. If it doesn't, check your bank details in the Paystack dashboard."),
])

# ═══════════════════════════════ Section 5: Subscription Lifecycle ═══════════════════════════════
story.append(PageBreak())
h2("5. Subscription Lifecycle Reference")

p("How each state transition works in production, with the exact database fields that change at each step.")

h3("State transitions")

data = [
    ["Event", "What happens", "DB fields affected"],
    [Paragraph("Customer subscribes", SMALL),
     Paragraph("Paystack charges card. Two webhooks fire: charge.success + subscription.create. App stores subscription details and activates the plan.", SMALL),
     Paragraph("Plan, SubscribedPlan, PaystackSubscriptionCode, PaystackPlanCode, PaystackCustomerCode, SubscriptionEndsAt, TrialEndsAt=null", SMALL)],

    [Paragraph("Monthly renewal", SMALL),
     Paragraph("Paystack auto-charges. charge.success webhook fires. App bumps SubscriptionEndsAt to next billing date.", SMALL),
     Paragraph("SubscriptionEndsAt (bumped forward 1 month)", SMALL)],

    [Paragraph("Customer upgrades", SMALL),
     Paragraph("Old subscription cancelled via Paystack API. New payment page shown. On success, new subscription replaces old.", SMALL),
     Paragraph("Plan, SubscribedPlan, PaystackSubscriptionCode, PaystackPlanCode, SubscriptionEndsAt (all updated to new plan)", SMALL)],

    [Paragraph("Customer downgrades", SMALL),
     Paragraph("PendingPlanChange set. Current plan stays active until SubscriptionEndsAt. Background job (TrialRevertJobService, every 4h) switches plan when billing period expires.", SMALL),
     Paragraph("PendingPlanChange (set). Later: Plan, SubscribedPlan (changed), PendingPlanChange (cleared)", SMALL)],

    [Paragraph("Customer cancels", SMALL),
     Paragraph("Paystack subscription disabled via API. PaystackSubscriptionCode cleared. If billing period remains, access continues. If expired/null, reverts to Starter.", SMALL),
     Paragraph("PaystackSubscriptionCode=null, PaystackPlanCode=null. Possibly Plan='starter', SubscribedPlan=null", SMALL)],

    [Paragraph("Payment fails", SMALL),
     Paragraph("Paystack retries automatically (up to 3 times over ~10 days). If all retries fail, subscription.disable webhook fires. App treats like a cancellation.", SMALL),
     Paragraph("Same as cancel path. PaystackSubscriptionCode cleared, plan eventually reverts.", SMALL)],

    [Paragraph("Refund issued", SMALL),
     Paragraph("Manual from Paystack dashboard. Refund doesn't trigger a webhook that changes plan state — you'd need to manually adjust if the customer should lose access.", SMALL),
     Paragraph("None automatically. Manual DB update if needed.", SMALL)],
]

t = Table(data, colWidths=[1.3*inch, 2.8*inch, 2.7*inch], repeatRows=1)
t.setStyle(TableStyle([
    ('BACKGROUND', (0, 0), (-1, 0), HexColor("#0ea5e9")),
    ('TEXTCOLOR', (0, 0), (-1, 0), HexColor("#ffffff")),
    ('FONTNAME', (0, 0), (-1, 0), 'Helvetica-Bold'),
    ('FONTSIZE', (0, 0), (-1, 0), 9),
    ('GRID', (0, 0), (-1, -1), 0.3, HexColor("#cbd5e1")),
    ('VALIGN', (0, 0), (-1, -1), 'TOP'),
    ('ROWBACKGROUNDS', (0, 1), (-1, -1), [HexColor("#ffffff"), HexColor("#f8fafc")]),
    ('LEFTPADDING', (0, 0), (-1, -1), 5),
    ('RIGHTPADDING', (0, 0), (-1, -1), 5),
    ('TOPPADDING', (0, 0), (-1, -1), 4),
    ('BOTTOMPADDING', (0, 0), (-1, -1), 4),
]))
story.append(t)

# ═══════════════════════════════ Section 6: Pricing & Plans ═══════════════════════════════
story.append(PageBreak())
h2("6. Pricing and Plan Configuration")

p("Current pricing as defined in <code>BizPilot.API/Common/PlanLimits.cs</code>:")

price_data = [
    ["Plan", "Monthly Price", "Products", "Messages", "Staff", "Key Features"],
    [Paragraph("Starter", SMALL), Paragraph("₦3,500", SMALL), Paragraph("30", SMALL), Paragraph("150", SMALL), Paragraph("1 (owner only)", SMALL), Paragraph("WhatsApp bot, basic dashboard, daily summaries", SMALL)],
    [Paragraph("Shop", SMALL), Paragraph("₦7,500", SMALL), Paragraph("Unlimited", SMALL), Paragraph("850", SMALL), Paragraph("4", SMALL), Paragraph("+ Ledger, stock holds, CSV import", SMALL)],
    [Paragraph("Pro", SMALL), Paragraph("₦12,500", SMALL), Paragraph("Unlimited", SMALL), Paragraph("Unlimited", SMALL), Paragraph("11", SMALL), Paragraph("+ Advanced reports, charts, CSV import", SMALL)],
    [Paragraph("Business", SMALL), Paragraph("₦30,000", SMALL), Paragraph("Unlimited", SMALL), Paragraph("Unlimited", SMALL), Paragraph("Unlimited", SMALL), Paragraph("+ Multi-branch, API access, custom exports", SMALL)],
]

t = Table(price_data, colWidths=[0.8*inch, 0.9*inch, 0.8*inch, 0.8*inch, 1.0*inch, 2.5*inch], repeatRows=1)
t.setStyle(TableStyle([
    ('BACKGROUND', (0, 0), (-1, 0), HexColor("#0ea5e9")),
    ('TEXTCOLOR', (0, 0), (-1, 0), HexColor("#ffffff")),
    ('FONTNAME', (0, 0), (-1, 0), 'Helvetica-Bold'),
    ('FONTSIZE', (0, 0), (-1, 0), 9),
    ('GRID', (0, 0), (-1, -1), 0.3, HexColor("#cbd5e1")),
    ('VALIGN', (0, 0), (-1, -1), 'TOP'),
    ('ROWBACKGROUNDS', (0, 1), (-1, -1), [HexColor("#ffffff"), HexColor("#f8fafc")]),
    ('LEFTPADDING', (0, 0), (-1, -1), 4),
    ('RIGHTPADDING', (0, 0), (-1, -1), 4),
    ('TOPPADDING', (0, 0), (-1, -1), 3),
    ('BOTTOMPADDING', (0, 0), (-1, -1), 3),
]))
story.append(t)

spacer(0.15)

p("<b>Business plan</b> is not self-serve — the dashboard shows a 'Contact Us' button that opens a pre-filled email "
  "to contact@bizpilot-ai.com. You set up Business-tier accounts manually after a sales conversation.")

h3("Changing prices")
p("If you change prices, update TWO places:")
p("1. <b><code>BizPilot.API/Common/PlanLimits.cs</code></b> — the <code>PricePerMonth</code> field for each plan. "
  "This is what the backend uses for amount verification on webhooks.")
p("2. <b><code>dashboard/src/app/(dashboard)/settings/page.tsx</code></b> — the <code>price</code> field in "
  "<code>PLAN_DETAILS</code>. This is what users see on the settings page.")
p("Paystack plans are auto-created by name ('BizPilot starter', 'BizPilot shop', etc). If you change a price, "
  "the old Paystack plan still exists at the old price. Either delete it in the Paystack dashboard, or rename "
  "the plans in code so new ones are created (e.g. 'BizPilot shop v2').")

warn("Amount verification matters. The webhook handler checks that the charged amount matches the expected plan price. "
     "If Paystack charges ₦7,500 but your code expects ₦10,000, the webhook will log a warning and reject the upgrade. "
     "Always keep code and Paystack prices in sync.")

# ═══════════════════════════════ Section 7: Monitoring ═══════════════════════════════
story.append(PageBreak())
h2("7. Monitoring Payments in Production")

h3("Server-side checks")
p("After every deploy and periodically (weekly):")
story.append(code(
    "# Recent Paystack webhook events\n"
    "SELECT \"EventId\", \"EventType\", \"ReceivedAtUtc\"\n"
    "FROM \"PaystackEventLogs\"\n"
    "ORDER BY \"ReceivedAtUtc\" DESC LIMIT 20;\n\n"
    "# Current subscription state for all businesses\n"
    "SELECT \"Name\", \"Plan\", \"SubscribedPlan\", \"PaystackSubscriptionCode\",\n"
    "       \"SubscriptionEndsAt\", \"PendingPlanChange\"\n"
    "FROM \"Businesses\"\n"
    "WHERE \"IsActive\" = true\n"
    "ORDER BY \"SubscriptionEndsAt\" DESC NULLS LAST;"
))

h3("Paystack dashboard checks")
p("• <b>Transactions</b> tab — see every charge with amount, status, customer email, date. Any 'failed' charges "
  "should have corresponding retry attempts.")
p("• <b>Subscriptions</b> tab — see active, cancelled, and expired subscriptions. Cross-reference with your DB to "
  "make sure states match.")
p("• <b>Settlements</b> tab — confirm that funds are settling to your bank account on T+1.")

h3("Alerts to set up")
p("In the Paystack dashboard under Settings → Notifications, enable:")
p("• Email on <b>failed transaction</b> — you want to know when a customer's card declines.")
p("• Email on <b>subscription cancellation</b> — track churn as it happens.")
p("• Email on <b>settlement</b> — confirm money is actually moving to your bank.")

# ═══════════════════════════════ Section 8: Gotchas ═══════════════════════════════
story.append(PageBreak())
h2("8. Gotchas and Edge Cases")

h3("Kobo, not Naira")
p("Paystack's API uses <b>kobo</b> (1 Naira = 100 kobo). Your code already handles this — <code>GetOrCreatePlanAsync</code> "
  "multiplies by 100. But if you ever create plans manually in the Paystack dashboard, enter the amount in kobo "
  "(e.g. 750000 for ₦7,500), not Naira.")

h3("Webhook delivery is not instant")
p("Paystack webhooks typically arrive within 1–5 seconds, but can be delayed up to a few minutes during peak loads. "
  "Your app should not assume the plan is active immediately after the customer pays — the webhook has to land first. "
  "The current flow handles this correctly: the dashboard polls <code>plan-status</code> and updates when the backend "
  "state changes.")

h3("Webhook retries")
p("If your server returns a non-2xx status for a webhook, Paystack retries up to 10 times with exponential backoff. "
  "The <code>PaystackEventLog</code> idempotency table prevents double-processing if the same event arrives twice.")

h3("Customer email")
p("Paystack requires a customer email. Your code uses <code>user.Email ?? user.PhoneNumber + '@bizpilot-ai.com'</code> "
  "as a fallback. This works but means Paystack's customer records won't have real email addresses for phone-only users. "
  "This doesn't affect payments but makes Paystack dashboard search harder.")

h3("Subscription code can change")
p("When a customer upgrades (cancels old sub, creates new), the <code>PaystackSubscriptionCode</code> changes. "
  "Any code that caches or references the old subscription code will break. The current implementation always "
  "reads from the database, so this is handled correctly.")

h3("Currency")
p("Paystack charges in NGN (Nigerian Naira). If you expand to other countries, you'll need separate Paystack "
  "sub-accounts or a different payment provider. BizPilot currently assumes NGN everywhere.")

h3("Refunds")
p("Paystack refunds are manual — initiated from the Paystack dashboard, not from your app. A refund does NOT "
  "automatically downgrade the customer's plan. If you refund someone, you also need to manually update their "
  "plan in the database, or tell them to cancel from the dashboard.")

h3("Complimentary accounts (IsBillable = false)")
p("Accounts with <code>IsBillable = false</code> skip all subscription checks. The subscribe button doesn't appear, "
  "plan gates don't apply, and no charges are made. Use this for your own test account, beta testers, or partners. "
  "Set it in the DB: <code>UPDATE \"Businesses\" SET \"IsBillable\" = false WHERE \"Id\" = '...';</code>")

# ═══════════════════════════════ Section 9: Quick Reference ═══════════════════════════════
story.append(PageBreak())
h2("9. Quick Reference")

h3("Key URLs")
p("• <b>Paystack dashboard:</b> https://dashboard.paystack.com")
p("• <b>Webhook endpoint:</b> https://api.bizpilot-ai.com/api/subscription/webhook")
p("• <b>Settings page (subscribe/cancel):</b> https://app.bizpilot-ai.com/settings")
p("• <b>Health check:</b> https://api.bizpilot-ai.com/health")

h3("Key files in the codebase")
p("• <code>BizPilot.API/Services/PaystackService.cs</code> — all Paystack API calls, webhook handling, plan creation")
p("• <code>BizPilot.API/Controllers/SubscriptionController.cs</code> — initialize, cancel, change-plan, webhook endpoints")
p("• <code>BizPilot.API/Common/PlanLimits.cs</code> — plan pricing and feature limits")
p("• <code>BizPilot.API/Common/PlanGuard.cs</code> — trial lifecycle, feature gating, plan rank comparison")
p("• <code>BizPilot.API/Jobs/TrialRevertJobService.cs</code> — background job that reverts expired trials and pending plan changes")
p("• <code>dashboard/src/app/(dashboard)/settings/page.tsx</code> — frontend plan card with subscribe/upgrade/downgrade/cancel")

h3("Key environment variables (/etc/bizpilot/api.env)")
p("• <code>Paystack__SecretKey</code> — the live or test secret key from Paystack dashboard")
p("• <code>Admin__AnalyticsKey</code> — your admin dashboard access key (unrelated to Paystack but on the same server)")

h3("Background jobs that affect subscriptions")
p("• <b>trial-revert</b> (every 4 hours) — reverts expired trials to their subscribed plan, and applies pending plan changes "
  "when SubscriptionEndsAt passes. If this job stops running, downgrades and trial reverts won't happen.")
p("• <b>trial-reminders</b> (daily 10 AM Lagos) — sends WhatsApp reminders at 7, 3, 1, 0 days before trial expiry.")

h3("Emergency: wrong charge or broken webhooks")
p("1. <b>Pause everything:</b> Set <code>Paystack__SecretKey</code> to an empty string and restart. The subscribe button "
  "will still show but initialization will fail with an error. Existing subscribers keep access.")
p("2. <b>Refund from Paystack dashboard:</b> Transactions → find the charge → Refund. Takes 3-5 business days to "
  "reach the customer's bank.")
p("3. <b>Fix the code/config and redeploy.</b>")
p("4. <b>Restore the secret key and restart.</b>")
p("5. <b>If webhooks were lost:</b> check PaystackEventLogs for gaps. Manually update business Plan/SubscribedPlan "
  "in the database if needed. Paystack doesn't replay missed webhooks — you'd need to reconcile manually.")


# ═══════════════════════════════ Build ═══════════════════════════════
doc.build(story)
print(f"✅ Wrote {OUT}")

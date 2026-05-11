#!/usr/bin/env python3
"""Generate end-user end-to-end testing guide PDF for the Ojunai app.

Output: ~/Desktop/Ojunai-E2E-Testing-Guide.pdf

Audience: a non-technical tester walking through every user-facing feature.
Each test case has: numbered steps, expected outcome, a pass/fail checkbox.
"""
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import inch
from reportlab.lib.colors import HexColor
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, PageBreak, Preformatted, Table, TableStyle, KeepTogether
)
import os

OUT = os.path.expanduser("~/Desktop/Ojunai-E2E-Testing-Guide.pdf")

styles = getSampleStyleSheet()
H1 = ParagraphStyle('H1', parent=styles['Heading1'], fontSize=24, spaceAfter=16, textColor=HexColor("#06b6d4"))
H2 = ParagraphStyle('H2', parent=styles['Heading2'], fontSize=15, spaceBefore=18, spaceAfter=8, textColor=HexColor("#0f172a"))
H3 = ParagraphStyle('H3', parent=styles['Heading3'], fontSize=12, spaceBefore=10, spaceAfter=4, textColor=HexColor("#475569"))
BODY = ParagraphStyle('Body', parent=styles['BodyText'], fontSize=10, leading=14, spaceAfter=6, textColor=HexColor("#1e293b"))
STEP = ParagraphStyle('Step', parent=BODY, leftIndent=14, spaceAfter=3)
EXPECT = ParagraphStyle('Expect', parent=BODY, fontSize=10, textColor=HexColor("#047857"), leftIndent=14, spaceAfter=8)
CODE = ParagraphStyle('Code', parent=styles['Code'], fontSize=8.5, leading=11, leftIndent=12, backColor=HexColor("#f1f5f9"), textColor=HexColor("#0f172a"), borderPadding=6)
SMALL = ParagraphStyle('Small', parent=styles['BodyText'], fontSize=9, leading=12, spaceAfter=4, textColor=HexColor("#64748b"))
INTRO = ParagraphStyle('Intro', parent=BODY, textColor=HexColor("#334155"), spaceAfter=10)
NOTE = ParagraphStyle('Note', parent=styles['BodyText'], fontSize=9, leading=12, spaceAfter=6, textColor=HexColor("#64748b"), leftIndent=14, backColor=HexColor("#fef9c3"), borderPadding=4)
PASS = ParagraphStyle('Pass', parent=BODY, textColor=HexColor("#64748b"), spaceAfter=14, fontSize=9)

story = []

def h1(t): story.append(Paragraph(t, H1))
def h2(t): story.append(Paragraph(t, H2))
def h3(t): story.append(Paragraph(t, H3))
def p(t): story.append(Paragraph(t, BODY))
def intro(t): story.append(Paragraph(t, INTRO))
def step(n, t): story.append(Paragraph(f"<b>{n}.</b> {t}", STEP))
def expect(t): story.append(Paragraph(f"<b>Expected:</b> {t}", EXPECT))
def note(t): story.append(Paragraph(f"<b>Note:</b> {t}", NOTE))
def code(t): story.append(Preformatted(t, CODE))
def spacer(h=0.1): story.append(Spacer(1, h * inch))
def pagebreak(): story.append(PageBreak())

def case(title, steps, expectation, *, note_text=None):
    """Render a numbered test case with steps, expected outcome, pass/fail line."""
    block = [Paragraph(f"<b>{title}</b>", H3)]
    for i, s in enumerate(steps, 1):
        block.append(Paragraph(f"<b>{i}.</b> {s}", STEP))
    block.append(Paragraph(f"<b>Expected:</b> {expectation}", EXPECT))
    if note_text:
        block.append(Paragraph(f"<b>Note:</b> {note_text}", NOTE))
    block.append(Paragraph("☐ Pass &nbsp;&nbsp; ☐ Fail &nbsp;&nbsp; Notes: ____________________________________________", PASS))
    story.append(KeepTogether(block))


# ─────────────────────── COVER ───────────────────────────────────────────────
h1("Ojunai · End-to-End Testing Guide")
intro(
    "This guide walks you through every customer-facing feature of the Ojunai app — both the "
    "web dashboard and the WhatsApp bot. Work through each section in order. For every test, "
    "tick <b>Pass</b> or <b>Fail</b> and note anything unexpected. Voice AI is excluded from "
    "this round of testing."
)
spacer(0.1)
intro(
    "<b>Estimated time:</b> 90 to 120 minutes for a full pass.<br/>"
    "<b>What you'll need:</b> a phone with WhatsApp, a computer or phone browser, "
    "and an email address you can read."
)
note(
    "If a step fails, write down the exact error message, what you typed or clicked, "
    "and the time. Send that back to the team — it makes fixes much faster."
)

pagebreak()

# ─────────────────────── 0. PRE-FLIGHT ───────────────────────────────────────
h2("0. Before you start")
p("Open these in advance and keep them handy:")
h3("Test data to invent")
code(
    "Business name:     Test Store (or anything)\n"
    "Phone number:      a real phone you have WhatsApp on\n"
    "Email:             a real inbox you can read\n"
    "Owner full name:   Your Test Name\n"
    "Password idea:     something you'll remember (10+ chars, mixed case, number, symbol)\n"
    "WhatsApp bot:      +1 415 523 8886   ← save this contact as 'Ojunai Bot'"
)
note(
    "Don't reuse a real-business phone number for testing — you'll send and receive lots of "
    "WhatsApp messages and create real DB entries. Use a personal phone instead."
)

pagebreak()

# ─────────────────────── 1. SIGN-UP ──────────────────────────────────────────
h2("1. Account creation & onboarding")

case(
    "1.1 Open the dashboard",
    [
        "On a desktop or phone browser, go to <b>https://app.ojunai.com</b> (or your dashboard URL).",
        "Confirm the page loads with the Ojunai eye logo and a dark navy background.",
    ],
    "The login page renders fully, no errors, no white flashes."
)

case(
    "1.2 Register a new business",
    [
        "Click <b>Register here</b>.",
        "Fill in: Full name, Phone number, Email (optional), Business name, Business type, State, City.",
        "Pick a password that meets the strength requirements (you'll see hints update as you type).",
        "Click <b>Create Account</b>.",
    ],
    "You're prompted to verify your phone with a 6-digit code sent via WhatsApp."
)

case(
    "1.3 Phone verification",
    [
        "Open WhatsApp on the phone you registered with.",
        "You should see a message from the Ojunai bot containing a 6-digit code.",
        "Enter the code in the dashboard and click <b>Verify</b>.",
    ],
    "Account is created, you're logged in, and you land on the Today page.",
    note_text="If the WhatsApp message doesn't arrive within 30 seconds, check that you saved the bot's number and that you sent any prior 'join' code if Twilio sandbox requires it."
)

case(
    "1.4 Email verification banner",
    [
        "If you provided an email, you should see a yellow banner at the top: 'Verify your email to enable account recovery'.",
        "Click <b>Resend verification email</b>.",
        "Open the inbox and click the link in the email.",
    ],
    "You return to the dashboard and the banner disappears. Settings → Your Account shows email as Verified."
)

# ─────────────────────── 2. DASHBOARD WALKTHROUGH ─────────────────────────────
h2("2. Dashboard walkthrough")

case(
    "2.1 Today page — top stat cards",
    [
        "On the Today page, locate the four stat cards at the top: Today's Net, Cash on Hand, Receivables, Low Stock.",
        "Resize the browser window or rotate your phone — confirm numbers do not overflow the cards.",
    ],
    "Stat values stay inside their cards. Numbers shrink to fit when very large (e.g. NGN 677,176,179)."
)

case(
    "2.2 Notification bell",
    [
        "Click the bell icon at the top right (or in the sidebar on desktop).",
        "Confirm the dropdown opens fully and is not cut off at any edge.",
        "If you have any alerts, click one to expand. Look for the <b>View source →</b> button on alerts that link to a page.",
    ],
    "Dropdown fits in the viewport on both desktop and mobile. Clicking an alert expands the full message in place; clicking 'View source' navigates to the related page."
)

case(
    "2.3 Sidebar navigation",
    [
        "Click each item in the sidebar: Today, Sales, Expenses, Bookings, Inventory, Contacts, Reports, Activity, Import, Export, Settings.",
        "On mobile, open the hamburger menu and confirm the bell does not appear inside the drawer (only at the top bar).",
    ],
    "Every page loads without errors. Mobile drawer shows only one bell (top bar)."
)

case(
    "2.4 Command palette",
    [
        "On desktop, press <b>Cmd+K</b> (Mac) or <b>Ctrl+K</b> (Windows).",
        "Type 'sale' — search results should suggest going to Sales.",
        "Press Enter to navigate.",
    ],
    "Command palette opens, typing filters live, Enter navigates to the chosen route."
)

pagebreak()

# ─────────────────────── 3. SALES (DASHBOARD) ────────────────────────────────
h2("3. Sales — recording from the dashboard")

case(
    "3.1 Record a basic sale",
    [
        "Go to <b>Sales</b> → <b>Record a Sale</b>.",
        "Pick a product (or type a new product name; you'll be prompted to add it to inventory).",
        "Enter quantity and amount.",
        "Pick the customer (or skip / add new).",
        "Click <b>Record Sale</b>.",
    ],
    "Toast: 'Sale recorded'. The sale appears at the top of the Sales list. Today's Net on the dashboard reflects the new amount."
)

case(
    "3.2 Sale with multiple products",
    [
        "On the Record-a-Sale form, click <b>+ Add another product</b>.",
        "Add two more products with different quantities and amounts.",
        "Submit.",
    ],
    "All three line items appear in the recorded sale. The total amount equals the sum of line totals."
)

case(
    "3.3 Sale with partial payment (creates receivable)",
    [
        "Record a sale where the customer pays less than the total (e.g. total NGN 5000, customer pays NGN 3000).",
        "Confirm the difference is recorded as a receivable.",
        "Go to <b>Contacts</b> and find the customer.",
    ],
    "The customer's record shows an outstanding receivable equal to the unpaid portion. Receivables stat card on Today increases."
)

case(
    "3.4 Sale with overpayment confirmation",
    [
        "Record a sale where the customer pays more than the total.",
        "Confirm the dialog asking what to do with the extra amount.",
    ],
    "A confirmation dialog appears asking how to handle the overpayment (refund vs. credit). After choosing, sale records correctly."
)

case(
    "3.5 Sale receipt actions",
    [
        "Click any sale in the list to open the side drawer.",
        "Try <b>Send via WhatsApp</b> — confirm a toast appears.",
        "Try <b>Email receipt</b> (if customer has an email).",
        "Try <b>Download PDF</b>.",
    ],
    "WhatsApp message lands on customer's phone (or the test customer's WhatsApp). Email receipt arrives. PDF downloads with the correct branding."
)

# ─────────────────────── 4. WHATSAPP BOT ─────────────────────────────────────
h2("4. WhatsApp bot — recording via natural language")

intro(
    "All these tests are done from the test phone you used to register, by sending messages "
    "to the Ojunai Bot contact. Wait for the bot's reply before sending the next message."
)

case(
    "4.1 Help",
    [
        "Send: <b>help</b>",
    ],
    "Bot replies with a list of available commands and example phrasings."
)

case(
    "4.2 Record a sale (natural language)",
    [
        "Send: <b>I sold 2 bottles of Coke for 1500</b>",
        "If asked to confirm or pick from a product list, reply with the matching number.",
    ],
    "Bot confirms the sale was recorded. Reload the dashboard's Sales page — the sale appears with the correct product, quantity, and amount."
)

case(
    "4.3 Record a sale with customer name",
    [
        "Send: <b>Mary bought 1 hair cream for 4500</b>",
    ],
    "Bot records the sale and links it to a contact named Mary. Mary appears in Contacts."
)

case(
    "4.4 Collected vs. paid (disambiguation test)",
    [
        "Send: <b>I collected 5000 from John for shoe</b>",
    ],
    "Bot records this as a sale (cash IN), not a debt payment. John appears in Contacts; the sale shows in Sales.",
    note_text="The model was specifically tuned to not confuse 'collected ... from X' with paying off X's debt — the verb 'collected' should always mean inflow."
)

case(
    "4.5 Record an expense",
    [
        "Send: <b>I paid 2000 for printing</b>",
    ],
    "Bot confirms the expense. Open the dashboard's Expenses page — the entry is there with category 'Printing' (or close)."
)

case(
    "4.6 Add to inventory",
    [
        "Send: <b>Add 10 bottles of water to inventory</b>",
    ],
    "Bot confirms inventory add. Inventory page shows the new stock added to the existing product (or creates a new product)."
)

case(
    "4.7 Record a debt payment",
    [
        "Pre-condition: Mary should already have a receivable from test 4.3 or 3.3.",
        "Send: <b>Mary paid 2000</b>",
    ],
    "Bot records the payment and reduces Mary's outstanding balance by 2000. Contacts page reflects the new balance."
)

case(
    "4.8 Daily summary on demand",
    [
        "Send: <b>summary</b> or <b>today's summary</b>",
    ],
    "Bot replies with today's totals: sales count, total cash in, expenses out, net."
)

case(
    "4.9 Large-sale confirmation flow",
    [
        "Pre-condition: in Settings → WhatsApp, ensure 'Confirm large sales' is on, threshold low (e.g. NGN 1000).",
        "Send: <b>I sold 10 phones for 50000</b>",
        "Bot should ask you to confirm before recording.",
        "Reply <b>yes</b>.",
    ],
    "Bot acknowledges and records only after your confirmation."
)

case(
    "4.10 Misclassification recovery",
    [
        "Send: <b>I just sold something</b> (deliberately vague)",
    ],
    "Bot replies asking for missing details (product, amount, etc.) rather than guessing or recording a bad entry."
)

pagebreak()

# ─────────────────────── 5. INVENTORY ────────────────────────────────────────
h2("5. Inventory")

case(
    "5.1 Add a product",
    [
        "Go to <b>Inventory</b> → <b>+ Add Product</b>.",
        "Fill in name, unit price, cost price, current stock, low-stock threshold.",
        "Save.",
    ],
    "Product appears in the inventory list."
)

case(
    "5.2 Edit a product",
    [
        "Click any product → edit price or threshold.",
        "Save.",
    ],
    "Changes persist after page reload."
)

case(
    "5.3 Stock adjustment",
    [
        "On a product's row, use <b>Adjust Stock</b> to add or remove quantity.",
        "Provide a reason (e.g. 'damaged', 'audit').",
    ],
    "Stock level updates and an entry shows up in Activity log."
)

case(
    "5.4 Low-stock alert",
    [
        "Reduce a product's stock below its threshold (e.g. via 'Adjust Stock').",
        "Wait up to 1 hour or trigger via the WhatsApp bot ('summary').",
    ],
    "A 'Low stock' alert shows in the bell dropdown and (if enabled in Settings) on WhatsApp."
)

# ─────────────────────── 6. CONTACTS ─────────────────────────────────────────
h2("6. Contacts (Customers & Suppliers)")

case(
    "6.1 Add a contact",
    [
        "Go to <b>Contacts</b> → <b>+ Add Contact</b>.",
        "Fill in name, phone, type (customer/supplier).",
        "Save.",
    ],
    "Contact appears in the list with — for receivables and 0 for outstanding."
)

case(
    "6.2 Filter & search",
    [
        "Use the filter chips at the top: All, Customers, Suppliers.",
        "Type a name in the search box.",
    ],
    "List narrows correctly. Filter shows the right counts."
)

case(
    "6.3 Contact detail drawer",
    [
        "Click a contact to open the side drawer.",
        "Confirm: contact info, balance summary, transaction history.",
    ],
    "Drawer shows full transaction history with running balance."
)

case(
    "6.4 Settle a debt from the dashboard",
    [
        "On a contact with an outstanding receivable, click <b>Record Payment</b>.",
        "Enter amount, save.",
    ],
    "Balance decreases. Activity log records the payment."
)

# ─────────────────────── 7. EXPENSES ─────────────────────────────────────────
h2("7. Expenses")

case(
    "7.1 Record an expense",
    [
        "Go to <b>Expenses</b> → <b>+ Record Expense</b>.",
        "Pick a category, enter amount, description, date.",
        "Save.",
    ],
    "Expense appears in the list. Today's Net adjusts."
)

case(
    "7.2 Edit an expense",
    [
        "Click any expense to edit category or amount.",
        "Save.",
    ],
    "Updated values persist after refresh."
)

case(
    "7.3 Delete an expense",
    [
        "Choose an expense and use the delete action (confirm in dialog).",
    ],
    "Expense disappears. Today's Net adjusts back."
)

# ─────────────────────── 8. REPORTS ─────────────────────────────────────────
h2("8. Reports")

case(
    "8.1 Tab navigation",
    [
        "Open <b>Reports</b>.",
        "Click each tab: Today, This Week, This Month, This Year.",
    ],
    "Each tab shows the right totals and date range."
)

case(
    "8.2 CSV export",
    [
        "On any tab, click <b>Export CSV</b>.",
        "Open the downloaded file.",
    ],
    "CSV opens in Excel or Numbers without errors. Columns aren't broken by commas in fields."
)

case(
    "8.3 Charts (Pro / Business plan only)",
    [
        "Open the Monthly chart.",
    ],
    "Chart renders. If on a free plan, a friendly upgrade prompt appears instead.",
    note_text="Skip this if your test account is on Starter / Free plan."
)

# ─────────────────────── 9. SETTINGS ─────────────────────────────────────────
h2("9. Settings")

case(
    "9.1 Accordion behavior",
    [
        "Open <b>Settings</b>.",
        "Click each section header (Business, Receipts, WhatsApp, Alerts, etc.) — they should expand and collapse.",
        "Refresh the page with a section open.",
    ],
    "Sections expand/collapse smoothly. Section state persists if you used the URL hash."
)

case(
    "9.2 Sale Confirmations (under WhatsApp)",
    [
        "Settings → WhatsApp → Sale Confirmations.",
        "Toggle 'Confirm large sales' on, set a threshold (e.g. NGN 1000).",
        "From WhatsApp, send a sale above the threshold.",
    ],
    "Bot asks for confirmation before recording. With the toggle off, sales record without confirmation."
)

case(
    "9.3 Daily sales goal",
    [
        "Settings → Alerts → Dashboard.",
        "Toggle 'Daily Sales Goal' on, enter an amount.",
        "Record sales totaling more than the goal.",
    ],
    "Bell dropdown shows a 'Goal hit' alert. With the toggle off, no such alert is created."
)

case(
    "9.4 Custom dashboard background (Pro / Business only)",
    [
        "Settings → Business → Branding.",
        "Upload a JPG, PNG, or WebP under 5MB.",
        "Drag the opacity slider.",
    ],
    "Image previews instantly. After saving, the dashboard background reflects the image at the chosen overlay opacity. <b>Free plan:</b> a 'Upgrade to use custom branding' card appears instead.",
    note_text="The server only accepts JPEG, PNG, or WebP. Try uploading an SVG or a renamed-text-file with a .jpg extension — both should be rejected with a clear error."
)

case(
    "9.5 Receipts customization",
    [
        "Settings → Receipts.",
        "Edit header text, footer text, accent color.",
        "Save, then re-send a receipt from any sale.",
    ],
    "Updated header/footer/color appear on the new receipt PDF and email."
)

case(
    "9.6 Categories",
    [
        "Settings → Categories.",
        "Add a custom expense category, save.",
        "Go to Expenses → record expense — your new category appears in the dropdown.",
    ],
    "Custom category usable when recording expenses."
)

pagebreak()

# ─────────────────────── 10. AUTH FLOWS ─────────────────────────────────────
h2("10. Authentication & recovery flows")

case(
    "10.1 Logout",
    [
        "Click <b>Sign Out</b> in the sidebar.",
    ],
    "You return to the login page. Trying to visit a dashboard URL redirects to login."
)

case(
    "10.2 Login again",
    [
        "Enter phone (or email) and password — try variations: 08012345678, +2348012345678, 0801 234 5678.",
        "Submit.",
    ],
    "All sensible Nigerian phone formats work — you don't have to type +234 explicitly. Login succeeds with correct password."
)

case(
    "10.3 Wrong password",
    [
        "Try logging in with a deliberately wrong password 5 times in a row.",
    ],
    "After several wrong attempts, you should see a rate-limit / 'try again in N seconds' message rather than the system letting you brute-force forever."
)

case(
    "10.4 Forgot password (Owner / Admin only)",
    [
        "On login, click <b>Forgot password?</b>.",
        "Enter your phone (use a local format like 08012345678 — the app should accept it).",
        "Receive a 6-digit code on WhatsApp.",
        "Enter the code and choose a new password.",
        "Sign in with the new password.",
    ],
    "WhatsApp delivers the code, password reset succeeds, login works.",
    note_text="Sales / Bookkeeper / Viewer staff cannot self-reset by WhatsApp — their owner or admin has to reset their password from Settings → Team. This is intentional."
)

case(
    "10.5 Account recovery via email",
    [
        "On the login page, click <b>Lost access to your phone?</b>.",
        "Enter the verified email on your account.",
        "Open the email, click the recovery link.",
        "Reset password OR add a new phone (whichever your business needs).",
    ],
    "Recovery flow lets you regain access without the original phone."
)

case(
    "10.6 Change password from inside the app",
    [
        "Settings → Your Account → Change Password.",
        "Enter current password and new password.",
    ],
    "Password updates. You receive an email and a Bell alert: 'Password changed'."
)

# ─────────────────────── 11. DATA IMPORT/EXPORT ─────────────────────────────
h2("11. Data import & export")

case(
    "11.1 Export sales / contacts / inventory",
    [
        "Go to <b>Export</b>.",
        "Tick the data types to export, click Export.",
    ],
    "CSV files download. Open them — values look correct, no formula injection (a cell starting with =, +, - should be quoted or prefixed)."
)

case(
    "11.2 Import contacts (CSV)",
    [
        "Go to <b>Import</b>.",
        "Upload a small CSV (3–5 rows) following the template.",
        "Confirm the preview, then run the import.",
    ],
    "All rows imported. Contacts page shows the new entries.",
    note_text="Try uploading a malformed CSV (extra commas, broken quotes) — should fail gracefully with a useful error, not silently import garbage."
)

# ─────────────────────── 12. PWA INSTALL ────────────────────────────────────
h2("12. Install as an app (PWA)")

case(
    "12.1 Install on iPhone",
    [
        "Open the dashboard URL in Safari on iPhone.",
        "Tap the Share button → 'Add to Home Screen'.",
        "Open the new home-screen icon.",
    ],
    "App opens full-screen with the Ojunai splash. Icon on home screen shows the new logo."
)

case(
    "12.2 Install on Android",
    [
        "Open the dashboard URL in Chrome on Android.",
        "Tap the menu → 'Install app' (or accept the prompt that appears).",
    ],
    "App installs and launches like a native app. Icon shows correctly on home screen."
)

case(
    "12.3 Offline behavior",
    [
        "While installed, switch the phone to airplane mode.",
        "Try to navigate within the app.",
    ],
    "An <b>offline page</b> appears for routes you haven't visited yet. Pages you've already loaded continue to render from cache."
)

# ─────────────────────── 13. NEGATIVE PATHS ─────────────────────────────────
h2("13. Negative paths — confirm errors are handled gracefully")

case(
    "13.1 Server unreachable",
    [
        "On the dashboard, switch to airplane mode.",
        "Try recording a sale.",
    ],
    "An error toast appears explaining the request failed. The app does not silently swallow the error."
)

case(
    "13.2 Stale session (cookie tampering)",
    [
        "While signed in, open browser devtools → Application → Cookies → modify the value of <b>oj_auth</b> to garbage characters.",
        "Reload the page.",
    ],
    "You're cleanly redirected to /login. The bad cookie is auto-cleared so the next request doesn't loop. Logging in again works first try."
)

case(
    "13.3 Form validation",
    [
        "Try submitting key forms with missing required fields.",
    ],
    "Inline error messages appear next to bad fields. The form is not submitted."
)

# ─────────────────────── 14. WRAP-UP ─────────────────────────────────────────
h2("14. Final checks & sign-off")

p("After completing all sections above, do a final pass:")
p("&nbsp;&nbsp;☐ Browser console is free of red errors (open devtools → Console).")
p("&nbsp;&nbsp;☐ All toasts disappear correctly (no permanent toasts stuck on screen).")
p("&nbsp;&nbsp;☐ Logo and favicons look correct on every page (login, dashboard, browser tab).")
p("&nbsp;&nbsp;☐ Numbers in stat cards never overflow.")
p("&nbsp;&nbsp;☐ Bell dropdown opens fully on mobile and desktop.")
p("&nbsp;&nbsp;☐ Dark theme renders consistently across pages.")
p("&nbsp;&nbsp;☐ All WhatsApp bot replies arrive within ~10 seconds of your message.")
p("&nbsp;&nbsp;☐ No emails went to spam; if any did, mark them 'Not spam' and note it.")

spacer(0.2)
h3("Reporting failures")
p(
    "For every <b>Fail</b> ticked above, capture: a screenshot or screen recording, "
    "the exact step that failed, the time, the device/browser, and any console error. "
    "Send the bundle back to the team for triage."
)

# ─────────────────────── BUILD ───────────────────────────────────────────────
doc = SimpleDocTemplate(
    OUT, pagesize=letter,
    leftMargin=0.6 * inch, rightMargin=0.6 * inch,
    topMargin=0.6 * inch, bottomMargin=0.6 * inch,
    title="Ojunai E2E Testing Guide",
    author="Ojunai",
)
doc.build(story)
print(f"Wrote {OUT}")

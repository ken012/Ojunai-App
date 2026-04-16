#!/usr/bin/env python3
"""Generate PDF documenting all changes made in the Apr 10-11 2026 session."""
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import inch
from reportlab.lib.colors import HexColor
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, PageBreak, Preformatted, Table, TableStyle
)
import os

OUT = os.path.expanduser("~/Desktop/BizPilot-Session-Changes-Apr10-11.pdf")

styles = getSampleStyleSheet()
H1 = ParagraphStyle('H1', parent=styles['Heading1'], fontSize=22, spaceAfter=14, textColor=HexColor("#0ea5e9"))
H2 = ParagraphStyle('H2', parent=styles['Heading2'], fontSize=15, spaceBefore=16, spaceAfter=8, textColor=HexColor("#0f172a"))
H3 = ParagraphStyle('H3', parent=styles['Heading3'], fontSize=12, spaceBefore=10, spaceAfter=4, textColor=HexColor("#475569"))
BODY = ParagraphStyle('Body', parent=styles['BodyText'], fontSize=10, leading=14, spaceAfter=6, textColor=HexColor("#1e293b"))
CODE = ParagraphStyle('Code', parent=styles['Code'], fontSize=8.5, leading=11, leftIndent=10, backColor=HexColor("#f1f5f9"), textColor=HexColor("#0f172a"), borderPadding=6)
SMALL = ParagraphStyle('Small', parent=styles['BodyText'], fontSize=9, leading=12, spaceAfter=4, textColor=HexColor("#64748b"))

def code(text):
    return Preformatted(text, CODE)

def table(data, col_widths=None):
    if col_widths is None:
        col_widths = [1.5*inch] + [(5.5*inch)/(len(data[0])-1)]*(len(data[0])-1)
    t = Table(data, colWidths=col_widths)
    t.setStyle(TableStyle([
        ('BACKGROUND', (0,0), (-1,0), HexColor("#0ea5e9")),
        ('TEXTCOLOR', (0,0), (-1,0), HexColor("#ffffff")),
        ('FONTNAME', (0,0), (-1,0), 'Helvetica-Bold'),
        ('FONTSIZE', (0,0), (-1,-1), 8),
        ('GRID', (0,0), (-1,-1), 0.5, HexColor("#cbd5e1")),
        ('VALIGN', (0,0), (-1,-1), 'TOP'),
        ('LEFTPADDING', (0,0), (-1,-1), 6),
        ('RIGHTPADDING', (0,0), (-1,-1), 6),
        ('TOPPADDING', (0,0), (-1,-1), 4),
        ('BOTTOMPADDING', (0,0), (-1,-1), 4),
    ]))
    return t

doc = SimpleDocTemplate(OUT, pagesize=letter, leftMargin=0.7*inch, rightMargin=0.7*inch, topMargin=0.8*inch, bottomMargin=0.8*inch)
story = []

# ─── Title ────────────────────────────────────────────────────────────────────
story.append(Paragraph("BizPilot AI — Session Changes", H1))
story.append(Paragraph("April 10-11, 2026", ParagraphStyle('sub', parent=BODY, fontSize=14, textColor=HexColor("#475569"), spaceAfter=4)))
story.append(Paragraph("Comprehensive record of all features, fixes, and improvements made during this development session.", BODY))
story.append(Spacer(1, 0.2*inch))

# ─── Dashboard UI Changes ────────────────────────────────────────────────────
story.append(Paragraph("Dashboard UI Changes", H2))

story.append(Paragraph("Mobile Responsive Sidebar", H3))
story.append(Paragraph("Added hamburger menu on mobile. Sidebar collapses to a drawer that slides in. Desktop sidebar unchanged. Auto-closes on navigation.", BODY))

story.append(Paragraph("Dialog Overlay Fix", H3))
story.append(Paragraph("Fixed nearly invisible modal backdrop. Changed from Tailwind class (bg-black/10) to inline style (rgba(0,0,0,0.65)) for guaranteed rendering. Dialog content changed to bg-white with shadow-2xl.", BODY))

story.append(Paragraph("KPI Cards Responsive", H3))
story.append(Paragraph("Changed grid from grid-cols-2 to grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 for better mobile display.", BODY))

story.append(Paragraph("Clickable Dashboard KPI Boxes", H3))
story.append(Paragraph("All 6 KPI cards are now interactive:", BODY))
story.append(table([
    ["Card", "Action"],
    ["Today's Sales", "Navigate to /sales"],
    ["Today's Expenses", "Navigate to /expenses"],
    ["Net Today", "Expand inline breakdown panel"],
    ["Receivables", "Expand list of who owes you"],
    ["Payables", "Expand list of who you owe"],
    ["Low Stock", "Navigate to /inventory with filter"],
], col_widths=[1.5*inch, 5*inch]))

story.append(Paragraph("Voided Sales Tab", H3))
story.append(Paragraph("Sales page now has Active/Voided tabs. Voided tab shows greyed rows with strikethrough amounts, voided date, no action buttons. Read-only.", BODY))

story.append(Paragraph("Editable Business Settings", H3))
story.append(Paragraph("Settings page: edit pencil on Business card opens modal. Currency dropdown (NGN/USD/GBP/EUR/CAD). Location split into City/Town/Country. Business name locked (not editable). Large sale alert threshold configurable.", BODY))

story.append(Paragraph("Editable Contacts", H3))
story.append(Paragraph("Pencil icon on each contact row opens edit dialog (name, phone, type).", BODY))

story.append(Paragraph("Add Buttons on All Pages", H3))
story.append(Paragraph("+ Record Sale (sales), + Add Expense (expenses), + Add Product (inventory), + Add Contact (contacts), Hold Stock (inventory). All with modal forms.", BODY))

story.append(Paragraph("Source Badge", H3))
story.append(Paragraph("Sales and expenses tables show WhatsApp (green) or Manual (grey) badge per row.", BODY))

story.append(Paragraph("Settings Verbiage Updates", H3))
story.append(Paragraph("Removed 'Claude AI' reference, replaced with 'AI'. Updated WhatsApp integration intro. Added green 'Chat with BizPilot on WhatsApp' button.", BODY))

# ─── Product Categories ──────────────────────────────────────────────────────
story.append(PageBreak())
story.append(Paragraph("Product Categories", H2))

story.append(Paragraph("12 Preset Categories", H3))
story.append(Paragraph("Food & Beverages, Beauty & Personal Care, Health & Wellness, Clothing & Apparel, Electronics, Home & Kitchen, Office & Stationery, Baby & Kids, Agriculture & Farming, Tools & Hardware, Industrial & Bulk Supplies, General/Other. Each with 5-10 subcategories.", BODY))

story.append(Paragraph("Category Picker (datalist)", H3))
story.append(Paragraph("Text input with browser autocomplete suggestions from preset + custom categories. Works reliably inside dialogs (replaced native select which had issues).", BODY))

story.append(Paragraph("Manage Categories (Settings)", H3))
story.append(Paragraph("New card in Settings page. Shows 12 presets as read-only pills. Custom categories editable — add via text input, delete via X button. Saved as JSON on Business entity.", BODY))

story.append(Paragraph("Claude Auto-Inference", H3))
story.append(Paragraph("When products are created via WhatsApp, Claude silently infers the most likely category + subcategory (e.g. 'rice' -> Food & Beverages / Grains & Rice). Full category map included in the system prompt.", BODY))

story.append(Paragraph("Clickable Stat Boxes + Category Filter", H3))
story.append(Paragraph("Inventory stat boxes (All/Low/Sufficient) are clickable filters with highlight rings. Category dropdown filters the product grid. Both combinable.", BODY))

# ─── Stock Holds ──────────────────────────────────────────────────────────────
story.append(Paragraph("Stock Holds System", H2))

story.append(Paragraph("New StockHold entity (Active/Released/Converted status). Reserve stock for customers who call ahead.", BODY))

story.append(Paragraph("WhatsApp Commands", H3))
story.append(code('"Hold 5 bags of rice for Ada"  -> hold created\n"Ada came for her rice"       -> converts to sale\n"Release Ada\'s hold"          -> frees stock\n"What\'s on hold?"             -> lists all holds'))

story.append(Paragraph("Dashboard UI", H3))
story.append(Paragraph("Active Holds panel on inventory page with Sell/Release buttons. Hold Stock button in header opens create dialog with product picker, customer name, quantity.", BODY))

story.append(Paragraph("Stock Display", H3))
story.append(Paragraph("Stock queries now show held quantities: 'Rice: 10 bag (3 on hold, 7 avail)'. Sale validation subtracts held stock from available.", BODY))

# ─── Alerts System ────────────────────────────────────────────────────────────
story.append(PageBreak())
story.append(Paragraph("Alerts System", H2))

story.append(table([
    ["Alert", "Trigger", "Toggle"],
    ["Low Stock", "After sale drops product below threshold", "AlertLowStock"],
    ["Stock-out", "Product hits 0", "Bundled with Low Stock"],
    ["Daily Summary", "8 PM Lagos via Hangfire (now sends WhatsApp)", "AlertDailySummary"],
    ["Large Sale", "Sale exceeds configurable threshold", "AlertLargeSale"],
    ["Weekly Summary", "8 AM Monday (already existed)", "Always on"],
], col_widths=[1.3*inch, 2.7*inch, 2.5*inch]))

story.append(Paragraph("Settings page has WhatsApp Alerts card with checkboxes for each toggle. Large sale threshold editable in Business settings. Daily summary now includes debtor count: '3 customers owe you: Ada (20k), Emeka (30k)'.", BODY))

# ─── Staff & Roles ────────────────────────────────────────────────────────────
story.append(Paragraph("Staff & Role System", H2))

story.append(table([
    ["Role", "Sales", "Expenses", "Stock", "Void", "Reports", "Staff", "Settings"],
    ["Owner", "Yes", "Yes", "Yes", "Yes", "All", "Yes", "Yes"],
    ["Admin", "Yes", "Yes", "Yes", "Yes", "All", "Yes", "No"],
    ["Sales", "Yes", "No", "View", "No", "Own", "No", "No"],
    ["Bookkeeper", "No", "Yes", "View", "No", "All", "No", "No"],
    ["Viewer", "No", "No", "View", "No", "All", "No", "No"],
], col_widths=[0.85*inch, 0.7*inch, 0.8*inch, 0.6*inch, 0.5*inch, 0.6*inch, 0.5*inch, 0.7*inch]))

story.append(Paragraph("Team Members card in Settings. Add staff with name, phone, password, role. Remove button (not on Owner). RecordedByUserId + RecordedByName tracked on every Sale/Expense. 'What did Mary sell today?' via WhatsApp.", BODY))

# ─── WhatsApp Bot Improvements ────────────────────────────────────────────────
story.append(PageBreak())
story.append(Paragraph("WhatsApp Bot Improvements", H2))

story.append(Paragraph("New Intents Added", H3))
story.append(table([
    ["Intent", "Trigger Example", "Response"],
    ["get_today_sales_detail", "'What did I sell today?'", "Product-by-product breakdown + total"],
    ["get_product_sales_today", "'How many lip gloss sold today?'", "Single product qty + revenue"],
    ["get_specific_stock", "'Show me rice, beans, oil'", "Filtered stock for named products"],
    ["get_staff_sales", "'What did Mary sell today?'", "Staff member's sales breakdown"],
    ["get_transaction_history", "'Show today's transactions'", "Full log: time, items, buyer, staff"],
    ["get_dead_stock", "'What hasn't sold?'", "Products with no sales in 14 days"],
    ["get_profit_by_product", "'Most profitable product?'", "Ranked by profit + margin %"],
    ["get_stockout_prediction", "'When will I run out?'", "Days left per product, color-coded"],
    ["get_today_expenses", "'What did I spend today?'", "Expense list with total"],
    ["get_recent_expenses", "'Show expenses'", "Last 7 days, 10 items"],
    ["hold_stock", "'Hold 5 rice for Ada'", "Creates stock reservation"],
    ["release_hold", "'Ada came for her rice'", "Converts to sale or releases"],
    ["get_active_holds", "'What's on hold?'", "Lists all active holds"],
    ["update_low_stock_threshold", "'Alert me when juice drops to 5'", "Sets low stock threshold"],
    ["delete_product (batch)", "'Delete all products'", "Batch delete all or by category"],
], col_widths=[1.5*inch, 2*inch, 3*inch]))

story.append(Paragraph("Multi-Product Operations", H3))
story.append(Paragraph("<b>add_inventory items[]</b> — 'Bought 30 rice, 40 juice, 25 shampoo' processes all in one message. Partial success supported (skips failures, reports what worked).", BODY))
story.append(Paragraph("<b>remove_inventory items[] + zeroAll</b> — 'Delete shampoo and conditioner stocks' zeros multiple. 'Clear all stock' zeros everything.", BODY))
story.append(Paragraph("<b>delete_product deleteAll + deleteCategory</b> — 'Delete all products' or 'Delete beauty products' batch operations.", BODY))

story.append(Paragraph("Auto-Debt Creation", H3))
story.append(Paragraph("<b>Credit sale</b>: 'Sold 2 foundation to Sarah, pay later' automatically creates sale (Credit status) + receivable ledger entry. <b>Credit purchase</b>: 'Received 50 mascara, pay later' automatically creates inventory + payable.", BODY))

story.append(Paragraph("Debt Clearing", H3))
story.append(Paragraph("'Clear Sara's debt' auto-looks up outstanding balance and clears full amount. 'Clear all debts' batch clears ALL outstanding receivables/payables. No more 'how much?' when user says 'everything'.", BODY))

story.append(Paragraph("Payment Method Tracking", H3))
story.append(Paragraph("'Sold 3 rice, paid cash' records paymentMethod='Cash'. Also supports 'Transfer' and 'Mixed'.", BODY))

# ─── Prompt Improvements ─────────────────────────────────────────────────────
story.append(PageBreak())
story.append(Paragraph("Claude Prompt Improvements", H2))

story.append(Paragraph("Based on real user test chats, the following prompt improvements were made:", BODY))

story.append(Paragraph("Unit Inference", H3))
story.append(Paragraph("No longer defaults everything to 'bag'. bottles->bottle, pcs->piece, cartons->carton, sachets->sachet, tins->tin, pairs->pair, packs->pack, rolls->roll, boxes->box. Only 'bag' when user says 'bags' or product is typically bagged (rice, cement).", BODY))

story.append(Paragraph("Banned Vague Clarifications", H3))
story.append(Paragraph("NEVER respond with 'Could you be more specific?' alone. Must always state what specific information is needed.", BODY))

story.append(Paragraph("Combined Intents", H3))
story.append(Paragraph("'Received 10 bottles conditioner, sell for 10k each' now sets sellingPrice inline during add_inventory. No second message needed.", BODY))

story.append(Paragraph("No Auto-Create on Sales", H3))
story.append(Paragraph("Selling a product that doesn't exist no longer creates phantom inventory. Instead: 'rice isn't in your inventory yet. Add it first.' Prevents bad bookkeeping.", BODY))

story.append(Paragraph("Specific Price Queries", H3))
story.append(Paragraph("'How much is rice?' answers directly from product context instead of dumping full inventory list.", BODY))

story.append(Paragraph("Delete vs Clear Disambiguation", H3))
story.append(Paragraph("'Delete all stock' = remove products from catalogue. 'Clear all stock' = set quantities to 0 but keep products.", BODY))

story.append(Paragraph("Emotional/Conversational Handling", H3))
story.append(Paragraph("'Business is slow today' -> empathize, offer summary. 'Wait/hold on' -> 'Take your time!' 'Open dashboard' -> provides URL. 'I'm closing for the day' -> shows daily summary.", BODY))

story.append(Paragraph("Shorthand Parsing", H3))
story.append(Paragraph("'x5', '5pcs', '@ 2k', 'a dozen'=12, '3dz'=36 all parsed correctly.", BODY))

story.append(Paragraph("Structured Expense Categories", H3))
story.append(Paragraph("Standard categories: Transport, Fuel, Electricity, Rent, Salaries, Advertising, Internet & Data, Maintenance, Inventory, General.", BODY))

# ─── Bug Fixes ────────────────────────────────────────────────────────────────
story.append(PageBreak())
story.append(Paragraph("Bug Fixes", H2))

story.append(table([
    ["Bug", "Fix"],
    ["costPrice not captured on sale auto-create", "Now reads costPrice from parsed payload"],
    ["Hold message hardcoded 'Ada'", "Uses actual hold.ContactName"],
    ["get_top_products had no handler", "Added HandleGetTopProductsAsync + switch routing"],
    ["CategoryPicker select didn't work in dialog", "Replaced with datalist (browser-native autocomplete)"],
    ["Dialog overlay nearly invisible", "Inline style rgba(0,0,0,0.65) + removed isolate"],
    ["EF tracking double-count on stock display", "Snapshot stockBefore before mutation"],
    ["FAQ on website didn't expand", "Removed duplicate onclick (was toggling twice = no change)"],
    ["CORS blocked dashboard-API on production", "Made CORS origins configurable via env var"],
    ["appsettings.Development.json uploaded to prod", "Deploy script now strips it before scp"],
    ["Phone normalization missing +1 for Canadian numbers", "Enhanced NormalizePhone for 10-digit North American format"],
], col_widths=[2.5*inch, 4*inch]))

# ─── Database Migrations ─────────────────────────────────────────────────────
story.append(Paragraph("Database Migrations", H2))
story.append(table([
    ["Migration", "Changes"],
    ["AddProductCategory", "Category + Subcategory on Products table"],
    ["AddStockHoldsAndProductCategory", "New StockHolds table"],
    ["AddLargeSaleThreshold", "Business.LargeSaleThreshold"],
    ["AddAlertToggles", "AlertLowStock, AlertDailySummary, AlertLargeSale on Business"],
    ["AddCustomCategories", "Business.CustomCategories (JSON text)"],
    ["AddStaffRolesAndRecordedBy", "Expanded UserRole enum + RecordedByUserId/Name on Sale + Expense"],
], col_widths=[2.5*inch, 4*inch]))

# ─── New Files ────────────────────────────────────────────────────────────────
story.append(Paragraph("New Files Created", H2))
story.append(table([
    ["File", "Purpose"],
    ["Common/EntrySource.cs", "Manual/WhatsApp constants"],
    ["Common/RolePermissions.cs", "Role-based permission system"],
    ["Models/StockHold.cs", "Stock hold entity + HoldStatus enum"],
    ["DTOs/Inventory/StockHoldDtos.cs", "Hold create request + response DTO"],
    ["DTOs/Business/BusinessDtos.cs", "UpdateBusinessRequest"],
    ["DTOs/Auth/StaffDtos.cs", "AddStaffRequest + StaffDto"],
    ["Services/BusinessService.cs", "Business CRUD"],
    ["Services/StockHoldService.cs", "Hold create/release/convert/list"],
    ["Services/Interfaces/IBusinessService.cs", "Business interface"],
    ["Services/Interfaces/IStockHoldService.cs", "Hold interface"],
    ["Controllers/BusinessController.cs", "GET/PUT /api/business"],
    ["Controllers/StockHoldsController.cs", "Stock holds CRUD"],
    ["Controllers/StaffController.cs", "Staff management CRUD"],
    ["dashboard/src/lib/categories.ts", "12 preset categories + subcategories"],
    ["dashboard/src/components/source-badge.tsx", "WhatsApp/Manual badge component"],
], col_widths=[2.8*inch, 3.7*inch]))

# ─── Website Changes ─────────────────────────────────────────────────────────
story.append(PageBreak())
story.append(Paragraph("Marketing Website Changes", H2))
story.append(Paragraph("File: ~/Desktop/BizPilot-AI Website/index.html", SMALL))

story.append(table([
    ["Change", "Details"],
    ["WhatsApp contact number", "Replaced placeholder 1234567890 with 16137128154 in 5 places"],
    ["'Sign In' header link", "New link to app.bizpilot-ai.com"],
    ["Hero CTAs reordered", "Primary: Create Free Account (app/register). Secondary: Chat on WhatsApp"],
    ["How It Works updated", "Step 1: Create Account. Step 2: Chat on WhatsApp"],
    ["Pricing 'Get Started Free'", "Changed from wa.me to app/register"],
    ["Bottom CTA", "Two buttons: Create Free Account + Chat on WhatsApp"],
    ["FAQ fix", "Removed duplicate onclick that caused double-toggle (no expand)"],
    ["Google verification meta tag", "Added google-site-verification meta tag"],
], col_widths=[2*inch, 4.5*inch]))

# ─── Outstanding Items ────────────────────────────────────────────────────────
story.append(Paragraph("Outstanding Items (Not Yet Built)", H2))

story.append(Paragraph("Tier 4 — Lower Priority", H3))
story.append(table([
    ["#", "Feature", "Effort"],
    ["4.1", "Backdating transactions", "40 min"],
    ["4.2", "Multi-action per message", "1-2 hrs"],
    ["4.3", "Returns/refunds", "1 hr"],
    ["4.4", "Undo last transaction", "1 hr"],
    ["4.5", "Custom role permissions (Option B)", "1.5 hrs"],
    ["4.6", "Scheduled payment reminders", "1 hr"],
    ["4.7", "Discount tracking", "45 min"],
    ["4.8", "Unusual sale confirmation", "1 hr"],
], col_widths=[0.4*inch, 3.6*inch, 2.5*inch]))

story.append(Paragraph("Pre-launch Security TODOs", H3))
story.append(Paragraph("1. Rotate Claude API key  2. Rotate Twilio Auth Token  3. Rotate JWT secret  4. Update /etc/bizpilot/api.env  5. Daily Postgres backups  6. Git init + push to private GitHub", BODY))

# ─── Deploy Commands ──────────────────────────────────────────────────────────
story.append(Paragraph("Deploy Commands", H2))
story.append(code("~/Desktop/BizPilot-AI/scripts/deploy-api.sh\n~/Desktop/BizPilot-AI/scripts/deploy-dashboard.sh"))
story.append(Paragraph("Rollback if needed:", BODY))
story.append(code("~/Desktop/BizPilot-AI/scripts/rollback-api.sh\n~/Desktop/BizPilot-AI/scripts/rollback-dashboard.sh"))

doc.build(story)
print(f"PDF written to {OUT}")

using System.Text.Json;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Expenses;
using Ojunai.API.DTOs.Ledger;
using Ojunai.API.DTOs.Parsing;
using Ojunai.API.DTOs.Products;
using Ojunai.API.DTOs.Sales;
using Ojunai.API.Models;
using Ojunai.API.Models.Messaging;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services.Channels.Telegram;

/// <summary>
/// Implementation of <see cref="ITelegramIntentHandler"/>. Reuses channel-agnostic services
/// (Claude, Sales, Expense, Ledger, Receipt) so the Telegram NL pipeline is a thin
/// orchestration layer rather than a parallel implementation of the WhatsApp bot logic.
///
/// MVP intent coverage (Phase 2.5):
///   - <c>record_sale</c>            → ISalesService.CreateAsync + PDF receipt via Telegram
///   - <c>record_expense</c>         → IExpenseService.CreateAsync
///   - <c>record_receivable_payment</c> → ILedgerService.RecordPaymentAsync (customer paid debt)
///   - anything else                 → polite "I can do sales/expenses/debt payments" reply
///
/// Errors during action dispatch are surfaced to the user with a short friendly message; the
/// orchestrator's MessageLog already records the raw inbound for post-mortem.
/// </summary>
public sealed class TelegramIntentHandler : ITelegramIntentHandler
{
    private readonly AppDbContext _db;
    private readonly IClaudeParsingService _claude;
    private readonly ISalesService _sales;
    private readonly IExpenseService _expenses;
    private readonly ILedgerService _ledger;
    private readonly IReceiptService _receipts;
    private readonly IEntityResolverService _resolver;
    private readonly IProductService _products;
    private readonly IPendingTelegramActionService _pending;
    private readonly TelegramAdapter _telegram;
    private readonly IWhatsAppService _whatsappDispatch;
    private readonly IAlertService _alerts;
    private readonly IPdfExportService _pdfExports;
    private readonly ILogger<TelegramIntentHandler> _logger;

    public TelegramIntentHandler(
        AppDbContext db,
        IClaudeParsingService claude,
        ISalesService sales,
        IExpenseService expenses,
        ILedgerService ledger,
        IReceiptService receipts,
        IEntityResolverService resolver,
        IProductService products,
        IPendingTelegramActionService pending,
        TelegramAdapter telegram,
        IWhatsAppService whatsappDispatch,
        IAlertService alerts,
        IPdfExportService pdfExports,
        ILogger<TelegramIntentHandler> logger)
    {
        _db = db;
        _claude = claude;
        _sales = sales;
        _expenses = expenses;
        _ledger = ledger;
        _receipts = receipts;
        _resolver = resolver;
        _products = products;
        _pending = pending;
        _telegram = telegram;
        _whatsappDispatch = whatsappDispatch;
        _alerts = alerts;
        _pdfExports = pdfExports;
        _logger = logger;
    }

    // Telegram-specific help text. Uses *bold* and _italic_ markers that Telegram renders via
    // parse_mode=Markdown. Avoids the "Coming soon" wording of the old Phase 2 stub — every
    // feature listed here is live as of Phase 3.6. When new intents are added to the bot, the
    // top-level grouping here is the right place to surface them so users discover them.
    private const string TelegramHelpText =
        "*Ojunai on Telegram*\n\n" +
        "I'm your business assistant. Type naturally and I'll record sales, expenses, and " +
        "payments, or answer questions about your data.\n\n" +
        "*Record*\n" +
        "• \"sold 2 rice for 1500\" — record a sale\n" +
        "• \"sold 3 rice and 2 beans for 5000\" — multi-item sale\n" +
        "• \"paid 3000 for printing\" — log an expense\n" +
        "• \"Mary paid 5000\" — customer payment\n" +
        "• \"bought 10 rice at 800\" — restock inventory\n\n" +
        "*Ask*\n" +
        "• *stock* / *inventory* — current stock levels\n" +
        "• *low stock* — what's running low\n" +
        "• *today's sales* — revenue + transaction count\n" +
        "• *this week* — weekly summary\n" +
        "• *this month* — month-to-date P&L\n" +
        "• *today's expenses* — today's expense list\n" +
        "• *summary* — full daily snapshot\n" +
        "• *who owes me* — outstanding receivables\n" +
        "• *who do i owe* — outstanding payables\n" +
        "• *my plan* — current subscription\n\n" +
        "*Reports & exports (delivered as PDF documents)*\n" +
        "• *export sales* — last 30 days as PDF\n" +
        "• *export inventory* — current stock snapshot\n" +
        "• *export expenses* — last 30 days\n" +
        "• *monthly p&l* — profit & loss statement\n\n" +
        "*Quick tips*\n" +
        "• When you sell an unknown product, I'll ask if you want to add it — tap Yes/No\n" +
        "• Sale receipts arrive as PDF documents you can tap to open or forward\n" +
        "• Corrections work: \"cancel that\", \"add Mary to that\", \"undo\"\n" +
        "• /start <token> — re-link this chat from the dashboard if needed";

    /// <summary>
    /// Intents we route through WhatsApp's dispatcher for full parity (read queries, exports,
    /// plan info, etc.). Excludes channel-specific writes we handle locally (create_sale,
    /// create_expense, record_receivable_payment) and excludes WhatsApp-side-effect intents
    /// that would push prompts back through Twilio rather than this channel.
    ///
    /// Adding a new intent to this set is the standard way to extend Telegram/Messenger reach.
    /// When in doubt, check WhatsAppService.ExecuteIntentAsync — if the handler is read-only
    /// and doesn't call SetPendingActionAsync or send a separate Twilio message, it's safe.
    /// </summary>
    private static readonly HashSet<string> DelegatedIntents = new()
    {
        // Read-only queries — all safe
        "get_today_sales", "get_today_sales_detail",
        "get_week_sales", "get_week_comparison",
        "get_daily_summary",
        "get_all_stock", "get_specific_stock", "get_low_stock",
        "get_dead_stock", "get_stock_value", "get_stockout_prediction",
        "get_outstanding_receivables", "get_outstanding_payables",
        "get_customer_balance", "get_supplier_balance",
        "get_profit_estimate", "get_profit_by_product", "get_product_profit",
        "get_top_products", "get_product_sales_today",
        "get_product_staff", "get_product_buyers",
        "get_today_expenses", "get_recent_expenses",
        "get_transaction_history",
        "get_staff_sales", "get_staff_list",
        "get_active_holds",
        "get_my_account", "get_my_plan", "get_plans",
        // get_export_link is handled locally (not delegated) so we can ship the PDF as a native
        // Telegram document instead of a URL+PIN — the chat is already authenticated by the
        // ContactIdentity binding, so a second-factor PIN buys nothing.
        "show_roles", "show_reports",
        // "help" handled locally so the text stays Telegram-tailored (Markdown + native PDF mention).
        "greet",
        // Writes that don't trigger WhatsApp-side-effect prompts and don't record a Source field
        // — safe to delegate. Source-attribution for the ones that DO write a Source ("WhatsApp"
        // hardcoded in WhatsAppService) is a known limitation, fix in follow-up.
        "add_inventory", "remove_inventory", "mark_damaged_inventory",
        "create_product", "update_product_price", "delete_product",
        "create_receivable", "create_payable", "record_payable_payment",
        "correct_last_sale", "update_last_sale", "correct_last_expense",
        "correct_debt", "undo_last_action", "return_product", "stocktake",
        "repeat_last_sale", "add_to_last_sale",
        "hold_stock", "release_hold",
        "update_low_stock_threshold",
        "create_contact", "add_staff",
        "subscribe", "cancel_subscription", "cancel_plan_change",
    };

    public async Task HandleAsync(ConversationMessage message, ContactIdentity boundIdentity, CancellationToken ct = default)
    {
        if (boundIdentity.UserId is null || boundIdentity.BusinessId is null)
        {
            _logger.LogWarning("TelegramIntentHandler invoked with unbound identity — orchestrator should have caught this");
            return;
        }

        var businessId = boundIdentity.BusinessId.Value;
        var userId = boundIdentity.UserId.Value;
        var rawText = message.Text ?? string.Empty;

        // ── 0. Inline-keyboard callbacks ─────────────────────────────────────────
        // Callbacks come through as ConversationMessage.Text = the callback_data string we set
        // on the inline-keyboard button. We use "pa:yes:<token>" and "pa:no:<token>" as our
        // convention. Token references a PendingTelegramAction row that holds the rich context
        // (full sale items, missing products, etc.) — the inline button can only carry 64 bytes,
        // so server-side state lives here.
        if (rawText.StartsWith("pa:", StringComparison.Ordinal))
        {
            await HandleCallbackAsync(rawText, businessId, userId, message, ct);
            return;
        }

        // ── 1. Load context Claude needs (business, products, contacts) ──────────
        var context = await BuildBusinessContextAsync(businessId, ct);
        if (context is null)
        {
            await Reply(message, "I couldn't find your business record. Try logging into the dashboard first.", ct);
            return;
        }

        // ── 2. Parse the message ─────────────────────────────────────────────────
        ParsedMessage parsed;
        try
        {
            parsed = await _claude.ParseAsync(rawText, context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Claude parse failed for Telegram chat {Chat}", message.SenderIdentity);
            await Reply(message, "I had trouble understanding that. Try rephrasing — something like \"I sold 2 of X for 5000\".", ct);
            return;
        }

        // ── 3. Surface clarification questions before intent dispatch ────────────
        // When Claude flags NeedsClarification=true (e.g. user said "sold 2 bags of rice" with
        // no amount), it provides a specific question like "How much did they pay?". Sending that
        // back gives the user a chance to provide the missing piece in their next message — far
        // more useful than the generic "couldn't pull the details" fallback. Without this check,
        // we'd dispatch into HandleSaleAsync, fail extraction, and reply with the unhelpful
        // canned message.
        //
        // Note: v2.5 doesn't persist partial state between turns. When the user re-sends, they
        // need to provide enough context that Claude can parse it standalone. Conversation
        // history (built from MessageLog) gives Claude one or two turns of memory, which is
        // usually enough — full pending-action state machine ships in Phase 2.9.
        if (parsed.NeedsClarification && !string.IsNullOrEmpty(parsed.ClarificationQuestion))
        {
            await Reply(message, parsed.ClarificationQuestion, ct);
            return;
        }

        // ── 4. Dispatch by intent ────────────────────────────────────────────────
        // Intent names match Claude's canonical vocabulary (defined in the system prompt and
        // wired through WhatsAppService): create_sale / create_expense / record_receivable_payment.
        // Channel-specific write flows live here — they have UX tailored to Telegram (inline
        // keyboard for add-product-on-the-fly, native PDF receipt button, etc.). Everything else
        // — every read query, plan/billing intent, and write-that-doesn't-need-channel-specific-UX
        // — delegates to WhatsAppService.ExecuteIntentForUserAsync for parity with WhatsApp.
        switch (parsed.Intent)
        {
            case "create_sale":
                await HandleSaleAsync(parsed, businessId, userId, message, ct);
                return;

            case "create_expense":
                await HandleExpenseAsync(parsed, businessId, userId, message, ct);
                return;

            case "record_receivable_payment":
                await HandlePaymentAsync(parsed, businessId, userId, message, ct);
                return;

            case "get_export_link":
                await HandleExportAsync(parsed, businessId, message, ct);
                return;

            case "help":
                await Reply(message, TelegramHelpText, ct);
                return;
        }

        if (DelegatedIntents.Contains(parsed.Intent))
        {
            await DelegateToWhatsAppDispatcherAsync(parsed, userId, message, ct);
            return;
        }

        // Unknown / smalltalk fall through. Forward those to WhatsApp's dispatcher too — its
        // HandleUnknown / HandleGreet response is friendlier than a stock one and stays consistent.
        await DelegateToWhatsAppDispatcherAsync(parsed, userId, message, ct);
    }

    /// <summary>
    /// Loads the User with Business included, runs WhatsApp's dispatcher, sends the result back
    /// through Telegram. Used for every intent we don't handle locally so Telegram users get
    /// the same answers (and same formatting) as WhatsApp users.
    /// </summary>
    private async Task DelegateToWhatsAppDispatcherAsync(ParsedMessage parsed, Guid userId, ConversationMessage inbound, CancellationToken ct)
    {
        var user = await _db.Users
            .Include(u => u.Business)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || user.Business is null)
        {
            await Reply(inbound, "I couldn't find your account record. Try logging into the dashboard first.", ct);
            return;
        }
        try
        {
            var reply = await _whatsappDispatch.ExecuteIntentForUserAsync(user, parsed, EntrySource.Telegram);
            await Reply(inbound, reply, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delegated intent {Intent} threw for user {User} via Telegram", parsed.Intent, userId);
            await Reply(inbound, $"Couldn't process that: {FriendlyErrorMessage(ex)}", ct);
        }
    }

    // ── Intent: create_sale (multi-line, fuzzy-matched) ───────────────────────
    private async Task HandleSaleAsync(
        ParsedMessage parsed,
        Guid businessId,
        Guid userId,
        ConversationMessage inbound,
        CancellationToken ct)
    {
        // Claude returns business_action.items as an array — one entry per line in a multi-product
        // sale like "sold 2 rice and 3 beans". Each item has productName + quantity + unitPrice
        // (or totalAmount). We loop, resolve each product fuzzily, and stop on the first
        // unresolved product — partial sales aren't recorded, the user gets a clean error.
        var items = ExtractSaleItems(parsed);
        if (items.Count == 0)
        {
            await Reply(inbound, "I caught a sale but couldn't pull the details. Try \"sold 2 of X for 5000\".", ct);
            return;
        }

        // Distribute prices before resolving products. Real-user messages like
        // "sold 2 widget and 3 gizmo for 4000" route through Claude as items where only ONE has
        // a totalAmount (or none have a price, but the sale's business_action has a top-level
        // totalAmount/amount). We detect that case and split the grand total across all items
        // by quantity ratio — what the user actually meant. Without this, the user gets a
        // confusing "I couldn't pull the price for gizmo" error even though they specified a total.
        var pricedItems = DistributePricesAcrossItems(items, parsed.BusinessAction);

        // Resolve every product up front. Track which items have a known product and which don't —
        // unknowns trigger the Phase-2.8 "add on the fly" inline-keyboard prompt instead of a hard error.
        var resolvedItems = new List<(Product Product, decimal Quantity, decimal UnitPrice)>();
        var unknownItems = new List<(string ProductName, decimal Quantity, decimal UnitPrice)>();

        foreach (var item in pricedItems)
        {
            var (product, _) = await _resolver.FindProductAsync(businessId, item.ProductName, ct);

            if (product is not null)
            {
                // Known product → fall back to stored SellingPrice when the user didn't say a
                // price. Matches WhatsApp's behavior: "sold 2 rice" with rice priced at ₦800
                // records as ₦1,600 without forcing the user to repeat the price every time.
                var unitPrice = item.UnitPrice > 0
                    ? item.UnitPrice
                    : product.SellingPrice ?? 0m;

                if (unitPrice <= 0)
                {
                    await Reply(inbound,
                        $"No selling price is set for *{product.Name}*. " +
                        $"Tell me what to charge — e.g. \"sold {item.Quantity} {product.Name} for [amount]\" — " +
                        "or set a default price in the dashboard.",
                        ct);
                    return;
                }
                resolvedItems.Add((product, item.Quantity, unitPrice));
            }
            else
            {
                // Unknown product → we need an explicit price; there's no stored value to fall back
                // to (the product doesn't exist yet). If the user didn't include one, ask.
                if (item.UnitPrice <= 0)
                {
                    await Reply(inbound,
                        $"I don't have *{item.ProductName}* yet and you didn't mention a price. " +
                        $"Try \"sold {item.Quantity} {item.ProductName} for [amount]\".",
                        ct);
                    return;
                }
                unknownItems.Add((item.ProductName, item.Quantity, item.UnitPrice));
            }
        }

        // Phase-2.8 — when one or more products are unknown, save the full sale context as a
        // pending action and ask the user whether to create them on the fly. Telegram's
        // callback_data is 64-byte capped so we store the rich state server-side and just embed
        // a short token in the button.
        if (unknownItems.Count > 0)
        {
            // Resolve customer name now so it's part of the cached payload (avoid re-doing the
            // resolve on callback resume — by then the user might have edited contacts).
            string? customerNameForLater = null;
            if (parsed.BusinessAction.ValueKind == JsonValueKind.Object
                && parsed.BusinessAction.TryGetProperty("contactName", out var cnEarly)
                && cnEarly.ValueKind == JsonValueKind.String)
            {
                customerNameForLater = cnEarly.GetString();
            }

            // Persist the post-distribution prices so the Yes-resume handler doesn't have to
            // re-derive them — and so it can't drift from the prices we showed the user in the prompt.
            var payload = new AddProductAndSellPayload
            {
                Items = pricedItems.Select(i => new AddProductAndSellPayloadItem
                {
                    ProductName = i.ProductName,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                }).ToList(),
                CustomerName = customerNameForLater,
            };
            var token = await _pending.CreateAsync(
                businessId, userId,
                inbound.SenderIdentity,
                actionType: "add_product_and_sell",
                payloadJson: JsonSerializer.Serialize(payload),
                ct);

            var unknownList = string.Join(", ", unknownItems.Select(u => $"*{u.ProductName}*"));
            // Renamed from `quickReplies` to avoid shadowing the later `quickReplies` declared
            // in the outer method scope for the receipt-button reply path. C# disallows shadowing
            // a variable that exists in an enclosing scope even when control flow can't reach both.
            var addPromptButtons = new List<QuickReply>
            {
                new($"Yes, add {(unknownItems.Count == 1 ? "it" : "them")}", $"pa:yes:{token}"),
                new("No, cancel", $"pa:no:{token}"),
            };

            await _telegram.SendAsync(inbound.SenderIdentity, new ReplyComposition
            {
                Text = $"I don't have {unknownList} in your inventory yet. " +
                       $"Add {(unknownItems.Count == 1 ? "it" : "them")} now and record the sale?\n\n" +
                       "_I'll use the sale price as both cost and selling price — you can edit later from the dashboard._",
                QuickReplies = addPromptButtons,
            }, ct);
            return;
        }

        // Resolve customer if Claude extracted one — fuzzy lookup, but unresolved customer doesn't
        // block the sale (walk-in is a valid outcome). We just skip the contact link and warn.
        Guid? contactId = null;
        string? customerName = null;
        if (parsed.BusinessAction.ValueKind == JsonValueKind.Object
            && parsed.BusinessAction.TryGetProperty("contactName", out var cn)
            && cn.ValueKind == JsonValueKind.String)
        {
            var typedName = cn.GetString();
            if (!string.IsNullOrEmpty(typedName))
            {
                var (contact, _) = await _resolver.FindContactAsync(businessId, typedName, ct);
                contactId = contact?.Id;
                customerName = contact?.Name ?? typedName; // fall back to whatever Claude got
            }
        }

        var request = new CreateSaleRequest
        {
            Items = resolvedItems.Select(r => new SaleItemRequest
            {
                ProductId = r.Product.Id,
                Quantity = r.Quantity,
                UnitPrice = r.UnitPrice,
            }).ToList(),
            ContactId = contactId,
            PaymentStatus = PaymentStatus.Paid,
        };

        SaleDto sale;
        try
        {
            sale = await _sales.CreateAsync(businessId, request, source: EntrySource.Telegram, recordedByUserId: userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram sale create failed for business {Business}", businessId);
            await Reply(inbound, $"Couldn't record the sale: {FriendlyErrorMessage(ex)}", ct);
            return;
        }

        // Fire post-sale alerts (low stock, large sale, daily goal) — channel-aware so the per-source
        // large-sale toggle on Business kicks in. Best-effort: failure here doesn't roll back the sale.
        try { await _alerts.EmitPostSaleAlertsAsync(businessId, sale.TotalAmount, sale.Id, EntrySource.Telegram); }
        catch (Exception ex) { _logger.LogWarning(ex, "Post-sale alert emit failed (Telegram)"); }

        // Text confirmation + PDF receipt as native Telegram document attachment. Multi-line sales
        // get each item on its own row so the user can verify the bot understood correctly.
        var customerLine = contactId.HasValue && !string.IsNullOrEmpty(sale.CustomerName)
            ? $" to {sale.CustomerName}"
            : !string.IsNullOrEmpty(customerName) ? $" to {customerName} (not in contacts)" : "";
        var receiptNumberLine = !string.IsNullOrEmpty(sale.ReceiptNumber) ? $" · #{sale.ReceiptNumber}" : "";

        var lineItems = string.Join("\n",
            resolvedItems.Select(r => $"• *{r.Quantity:0.##}× {r.Product.Name}* — {FormatAmount(r.Quantity * r.UnitPrice, businessId)}"));

        // Phase 2.8.2 — receipts are never auto-sent. Generating + uploading a PDF for every
        // logged sale floods the chat (a shop owner logging 20 sales/day got 20 PDFs). Every
        // confirmation carries a single "📄 Get Receipt" inline button; one tap fetches the PDF
        // when actually needed. Matches WhatsApp's behavior — confirmation only by default.
        var quickReplies = await BuildReceiptButtonAsync(businessId, userId, inbound.SenderIdentity, sale.Id, ct);

        await _telegram.SendAsync(inbound.SenderIdentity, new ReplyComposition
        {
            Text =
                $"✅ Sale recorded{customerLine}{receiptNumberLine}\n" +
                lineItems +
                $"\n*Total: {FormatAmount(sale.TotalAmount, businessId)}*",
            QuickReplies = quickReplies,
        }, ct);
    }

    /// <summary>
    /// Pulls per-item details out of Claude's create_sale payload. Returns empty list when items
    /// array is missing/empty. Quantities ≤ 0 are filtered out (Claude occasionally emits 0 when
    /// it can't tell).
    /// </summary>
    private static List<SaleItemParse> ExtractSaleItems(ParsedMessage parsed)
    {
        var result = new List<SaleItemParse>();
        var action = parsed.BusinessAction;
        if (action.ValueKind != JsonValueKind.Object) return result;
        if (!action.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return result;

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var productName = item.TryGetProperty("productName", out var pn) ? pn.GetString() : null;
            if (string.IsNullOrEmpty(productName)) continue;

            decimal quantity = 1;
            if (item.TryGetProperty("quantity", out var q) && q.ValueKind == JsonValueKind.Number) quantity = q.GetDecimal();
            if (quantity <= 0) continue;

            decimal? unitPrice = null;
            decimal? totalAmount = null;
            if (item.TryGetProperty("unitPrice", out var up) && up.ValueKind == JsonValueKind.Number) unitPrice = up.GetDecimal();
            if (item.TryGetProperty("totalAmount", out var tot) && tot.ValueKind == JsonValueKind.Number) totalAmount = tot.GetDecimal();

            result.Add(new SaleItemParse(productName, quantity, unitPrice, totalAmount));
        }
        return result;
    }

    private readonly record struct SaleItemParse(string ProductName, decimal Quantity, decimal? UnitPrice, decimal? TotalAmount);

    /// <summary>
    /// A priced item — every entry returned has a definite UnitPrice (may be 0 only when we
    /// genuinely couldn't infer one). This is the shape downstream code consumes.
    /// </summary>
    private readonly record struct PricedSaleItem(string ProductName, decimal Quantity, decimal UnitPrice);

    /// <summary>
    /// Resolves missing per-item prices using whatever total Claude returned. Real user messages
    /// like "sold 2 widget and 3 gizmo for 4000" trip up the naive per-item-totalAmount approach
    /// because Claude often attaches the "4000" to one item and leaves the other priceless.
    ///
    /// Strategy, in order:
    ///   1. Items that explicitly have unitPrice → keep as-is.
    ///   2. Items that explicitly have totalAmount → unitPrice = totalAmount / quantity.
    ///   3. If anything still missing, look for a "grand total" in either:
    ///      a. The business_action top-level (totalAmount / amount / saleTotal / unitPrice).
    ///      b. The single non-zero totalAmount among items, when the typical "for X" phrasing
    ///         attached it to just one line.
    ///   4. Distribute the grand total across ALL items by quantity ratio: unitPrice = grandTotal / totalQty.
    ///   5. Anything still missing stays at zero — caller bails with a "specify price" error.
    /// </summary>
    private static List<PricedSaleItem> DistributePricesAcrossItems(List<SaleItemParse> items, JsonElement businessAction)
    {
        if (items.Count == 0) return new();

        // First pass: settle the easy wins (explicit per-item price).
        var priced = items.Select(i =>
        {
            var unitPrice = i.UnitPrice.HasValue && i.UnitPrice.Value > 0
                ? i.UnitPrice.Value
                : i.TotalAmount.HasValue && i.TotalAmount.Value > 0 && i.Quantity > 0
                    ? i.TotalAmount.Value / i.Quantity
                    : 0m;
            return new PricedSaleItem(i.ProductName, i.Quantity, unitPrice);
        }).ToList();

        // Nothing to distribute? Done.
        if (priced.All(p => p.UnitPrice > 0)) return priced;

        // Find a grand total. Look at top-level fields first, then fall back to "exactly one item
        // carries the whole amount" heuristic.
        decimal? grandTotal = null;
        if (businessAction.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "totalAmount", "amount", "saleTotal", "unitPrice" })
            {
                if (businessAction.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
                {
                    var candidate = v.GetDecimal();
                    if (candidate > 0) { grandTotal = candidate; break; }
                }
            }
        }

        // Fallback heuristic — exactly one item has a totalAmount, all others are price-less.
        // That's the "sold 2 X and 3 Y for 4000" pattern Claude tends to emit. Treat that one
        // totalAmount as the grand total and redistribute across all items.
        if (grandTotal is null)
        {
            var withTotals = items.Where(i => i.TotalAmount.HasValue && i.TotalAmount.Value > 0).ToList();
            var withoutAnyPrice = items.Count(i => (i.UnitPrice ?? 0) <= 0 && (i.TotalAmount ?? 0) <= 0);
            if (withTotals.Count == 1 && withoutAnyPrice > 0)
            {
                grandTotal = withTotals[0].TotalAmount;
            }
        }

        if (grandTotal is null || grandTotal.Value <= 0) return priced;

        // Compute per-unit price by spreading the grand total across the total quantity of
        // items missing a price. We deliberately don't override items that already had explicit
        // prices — the user gave us those numbers, we trust them.
        var totalQuantityMissing = priced.Where(p => p.UnitPrice == 0).Sum(p => p.Quantity);
        if (totalQuantityMissing <= 0) return priced;

        // Subtract any explicitly-priced items' contribution from the grand total, then divide.
        // E.g. if user said "1 X at 1000 and 2 Y for 4000 total", we infer Y unit = (4000 - 1000) / 2.
        var alreadyPaid = priced.Where(p => p.UnitPrice > 0).Sum(p => p.Quantity * p.UnitPrice);
        var remainder = grandTotal.Value - alreadyPaid;
        if (remainder <= 0) return priced;  // grand total was less than already-priced subtotal — punt

        var fillPrice = Math.Round(remainder / totalQuantityMissing, 2);

        return priced.Select(p => p.UnitPrice > 0
            ? p
            : p with { UnitPrice = fillPrice }
        ).ToList();
    }

    // ── Intent: record_expense ─────────────────────────────────────────────────
    private async Task HandleExpenseAsync(
        ParsedMessage parsed,
        Guid businessId,
        Guid userId,
        ConversationMessage inbound,
        CancellationToken ct)
    {
        if (!TryExtractExpense(parsed, out var amount, out var category, out var paidTo))
        {
            await Reply(inbound, "I caught an expense but couldn't pull the amount or category. Try \"paid 2000 for printing\".", ct);
            return;
        }

        var request = new CreateExpenseRequest
        {
            Category = category ?? "General",
            Amount = amount,
            PaidTo = paidTo,
            Notes = $"Recorded via Telegram",
        };

        ExpenseDto expense;
        try
        {
            expense = await _expenses.CreateAsync(businessId, request, source: EntrySource.Telegram, recordedByUserId: userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram expense create failed for business {Business}", businessId);
            await Reply(inbound, $"Couldn't record the expense: {FriendlyErrorMessage(ex)}", ct);
            return;
        }

        await Reply(inbound,
            $"✅ Expense recorded\n*{expense.Category}*{(string.IsNullOrEmpty(paidTo) ? "" : $" — {paidTo}")}\n" +
            $"Amount: {FormatAmount(amount, businessId)}",
            ct);
    }

    // ── Intent: record_receivable_payment ──────────────────────────────────────
    private async Task HandlePaymentAsync(
        ParsedMessage parsed,
        Guid businessId,
        Guid userId,
        ConversationMessage inbound,
        CancellationToken ct)
    {
        if (!TryExtractPayment(parsed, out var amount, out var customerName))
        {
            await Reply(inbound, "I caught a payment but couldn't pull the amount or customer. Try \"Mary paid 5000\".", ct);
            return;
        }

        var (contact, contactError) = await _resolver.FindContactAsync(businessId, customerName, ct);
        if (contact is null)
        {
            await Reply(inbound, contactError ?? $"I couldn't find *{customerName}* in your contacts.", ct);
            return;
        }

        var request = new RecordPaymentRequest
        {
            ContactId = contact.Id,
            Amount = amount,
            PaymentType = "Receivable",
            Notes = "Recorded via Telegram",
        };

        try
        {
            await _ledger.RecordPaymentAsync(businessId, request, source: EntrySource.Telegram, recordedByUserId: userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram payment record failed for business {Business}", businessId);
            await Reply(inbound, $"Couldn't record the payment: {FriendlyErrorMessage(ex)}", ct);
            return;
        }

        await Reply(inbound,
            $"✅ Payment recorded\n*{contact.Name}* paid {FormatAmount(amount, businessId)}",
            ct);
    }

    // ── Intent: get_export_link (PDF delivered as native document) ─────────────
    //
    // Telegram bypasses the URL+PIN flow that WhatsApp uses. The chat is already authenticated
    // by the ContactIdentity binding (we know exactly which user this is) so a second-factor
    // PIN buys nothing. We generate the PDF inline and push it via TelegramAdapter.SendDocumentAsync
    // — same machinery as sale receipts. Covers last 30 days for the date-range reports;
    // inventory ignores the dates because it's a snapshot.
    private async Task HandleExportAsync(
        ParsedMessage parsed,
        Guid businessId,
        ConversationMessage inbound,
        CancellationToken ct)
    {
        // Same normalization as WhatsAppService.HandleGetExportLink — accept Claude's casing
        // wobble + common synonyms ("stock" → "inventory", "pnl" → "monthly-pnl").
        var raw = parsed.BusinessAction.ValueKind == JsonValueKind.Object
            && parsed.BusinessAction.TryGetProperty("reportType", out var rt)
            && rt.ValueKind == JsonValueKind.String
                ? rt.GetString() ?? ""
                : "";

        var reportType = raw.Trim().ToLowerInvariant() switch
        {
            "stock" => "inventory",
            "pnl" or "profit-and-loss" or "p&l" => "monthly-pnl",
            "expense" => "expenses",
            "sale" => "sales",
            var s => s,
        };

        if (string.IsNullOrWhiteSpace(reportType))
        {
            await Reply(inbound,
                "*Export your data*\n\n" +
                "Which report do you want as a PDF?\n" +
                "• *Sales* — last 30 days\n" +
                "• *Expenses* — last 30 days\n" +
                "• *Inventory* — current stock snapshot\n" +
                "• *Monthly P&L* — profit & loss\n\n" +
                "Just say which one — e.g. \"export my sales\".",
                ct);
            return;
        }

        var validTypes = new HashSet<string> { "sales", "expenses", "inventory", "monthly-pnl" };
        if (!validTypes.Contains(reportType))
        {
            await Reply(inbound,
                "I can generate PDFs for sales, expenses, inventory, or monthly P&L. " +
                "Try \"export sales\" or \"inventory report\".",
                ct);
            return;
        }

        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-30);
        byte[] pdfBytes;
        try
        {
            pdfBytes = await _pdfExports.GenerateReportPdfAsync(businessId, reportType, from, to);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram PDF export failed for {Report} (business {Business})", reportType, businessId);
            await Reply(inbound, "Couldn't generate the report just now. Try again in a moment.", ct);
            return;
        }

        var label = reportType switch
        {
            "sales" => "Sales-Report",
            "expenses" => "Expenses-Report",
            "inventory" => "Inventory-Report",
            "monthly-pnl" => "PnL-Statement",
            _ => "Report",
        };
        var fileName = reportType == "inventory"
            ? $"Ojunai-{label}-{to:yyyyMMdd}.pdf"
            : $"Ojunai-{label}-{from:yyyyMMdd}-{to:yyyyMMdd}.pdf";

        var caption = reportType switch
        {
            "sales" => $"*Sales Report* · {from:dd MMM} – {to:dd MMM yyyy}",
            "expenses" => $"*Expenses Report* · {from:dd MMM} – {to:dd MMM yyyy}",
            "inventory" => $"*Inventory Report* · as of {to:dd MMM yyyy}",
            "monthly-pnl" => $"*Profit & Loss* · {from:dd MMM} – {to:dd MMM yyyy}",
            _ => "Report",
        };

        using var stream = new MemoryStream(pdfBytes);
        await _telegram.SendDocumentAsync(inbound.SenderIdentity, stream, fileName, caption, ct);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<BusinessContext?> BuildBusinessContextAsync(Guid businessId, CancellationToken ct)
    {
        var business = await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == businessId, ct);
        if (business is null) return null;

        // For v1 Telegram we send a compact slice of products (top 50 by stock) and contacts.
        // Full lists for big businesses would bloat the Claude prompt — that optimization can
        // come once Telegram usage data shows the real distribution.
        var products = await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive)
            .OrderByDescending(p => p.CurrentStock)
            .Take(50)
            .Select(p => new ProductContext(p.Name, p.Unit, p.CurrentStock, p.Category))
            .ToListAsync(ct);

        var contacts = await _db.Contacts
            .Where(c => c.BusinessId == businessId)
            .OrderBy(c => c.Name)
            .Take(100)
            .Select(c => new ContactContext(c.Name, c.Type.ToString()))
            .ToListAsync(ct);

        var totalProducts = await _db.Products
            .CountAsync(p => p.BusinessId == businessId && p.IsActive, ct);

        return new BusinessContext
        {
            BusinessName = business.Name,
            Currency = business.Currency,
            Timezone = business.Timezone ?? "Africa/Lagos",
            Products = products,
            Contacts = contacts,
            TotalProducts = totalProducts,
        };
    }

    private async Task SendReceiptAsync(ConversationMessage inbound, Guid saleId, Guid businessId, CancellationToken ct)
    {
        try
        {
            var (pdfBytes, receiptNumber) = await _receipts.GenerateAsync(saleId, businessId);
            using var stream = new MemoryStream(pdfBytes);
            await _telegram.SendDocumentAsync(
                inbound.SenderIdentity,
                stream,
                fileName: $"Receipt-{receiptNumber}.pdf",
                caption: $"Receipt #{receiptNumber}",
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram PDF receipt send failed for sale {Sale}", saleId);
            // Don't surface the failure to the user — the sale itself succeeded; they can download
            // the PDF from the dashboard if needed. Receipt-send is best-effort.
        }
    }

    private async Task Reply(ConversationMessage inbound, string text, CancellationToken ct)
    {
        await _telegram.SendAsync(inbound.SenderIdentity, new ReplyComposition { Text = text }, ct);
    }

    private string FormatAmount(decimal amount, SaleDto sale)
        => BillingConfig.FormatPrice(amount, "NGN"); // sale.Source doesn't carry currency; rely on business — see overload below

    private string FormatAmount(decimal amount, Guid businessId)
    {
        var currency = _db.Businesses.AsNoTracking().Where(b => b.Id == businessId).Select(b => b.Currency).FirstOrDefault() ?? "NGN";
        return BillingConfig.FormatPrice(amount, currency);
    }

    private static string FriendlyErrorMessage(Exception ex) => ex switch
    {
        InvalidOperationException => ex.Message,
        ArgumentException => ex.Message,
        _ => "Something went wrong. Try again in a moment.",
    };

    // ── Intent payload extractors ──────────────────────────────────────────────
    // Claude returns business_action as a JsonElement. The exact shape depends on the intent
    // (see ClaudeParsingService prompt). We pull the most-common fields with safe defaults;
    // missing fields trigger the "couldn't pull details" branch above.

    private static bool TryExtractExpense(
        ParsedMessage parsed,
        out decimal amount,
        out string? category,
        out string? paidTo)
    {
        amount = 0;
        category = null;
        paidTo = null;

        var action = parsed.BusinessAction;
        if (action.ValueKind != JsonValueKind.Object) return false;

        // create_expense payload: { amount, category, paidTo, notes } — all camelCase.
        if (action.TryGetProperty("amount", out var amt) && amt.ValueKind == JsonValueKind.Number) amount = amt.GetDecimal();
        if (action.TryGetProperty("category", out var cat)) category = cat.GetString();
        if (action.TryGetProperty("paidTo", out var pt)) paidTo = pt.GetString();

        return amount > 0;
    }

    private static bool TryExtractPayment(
        ParsedMessage parsed,
        out decimal amount,
        out string customerName)
    {
        amount = 0;
        customerName = string.Empty;

        var action = parsed.BusinessAction;
        if (action.ValueKind != JsonValueKind.Object) return false;

        // record_receivable_payment payload: { contactName, amount, paymentType }.
        if (action.TryGetProperty("amount", out var amt) && amt.ValueKind == JsonValueKind.Number) amount = amt.GetDecimal();
        if (action.TryGetProperty("contactName", out var cn)) customerName = cn.GetString() ?? "";

        return amount > 0 && !string.IsNullOrEmpty(customerName);
    }

    // ── Phase-2.8 callback handling ────────────────────────────────────────────

    /// <summary>
    /// Routes an inline-keyboard callback to its specific resume handler. callback_data format:
    /// <c>pa:&lt;yes|no&gt;:&lt;token&gt;</c>. The token references a server-side
    /// <see cref="PendingTelegramAction"/> row holding the rich state.
    ///
    /// After handling, strips the inline keyboard from the original bot message so the user
    /// can't tap the same button twice (Phase 2.8.1). Best-effort — strip failures don't break
    /// the action.
    /// </summary>
    private async Task HandleCallbackAsync(string callbackData, Guid businessId, Guid userId, ConversationMessage inbound, CancellationToken ct)
    {
        // Strip the inline keyboard from the originating bot message regardless of decision —
        // this prevents stale "No" taps after a "Yes" was already processed (and vice versa),
        // which is the UX bug from real-user testing in Phase 2.8.
        if (inbound.InReplyToMessageId.HasValue)
        {
            await _telegram.RemoveInlineKeyboardAsync(inbound.SenderIdentity, inbound.InReplyToMessageId.Value, ct);
        }

        var parts = callbackData.Split(':', 3);
        if (parts.Length != 3 || parts[0] != "pa")
        {
            _logger.LogWarning("Malformed Telegram callback_data: {Data}", callbackData);
            return;
        }
        var decision = parts[1];
        var token = parts[2];

        // Both Yes and No go through ConsumeAsync so we can distinguish three cases:
        //   - Token valid + we consumed it → act on the decision (record sale or actually cancel)
        //   - Token already consumed       → user tapped a stale button; reply truthfully
        //   - Token expired/missing        → tell them to try again
        // Without this, a "No" tap on an already-Yes-handled message would falsely say
        // "Nothing recorded" — misleading users into thinking their sale was undone.
        var consumed = await _pending.ConsumeAsync(token, inbound.SenderIdentity, ct);

        if (decision == "no")
        {
            if (consumed is null)
                await Reply(inbound, "This action was already handled. Tap on a fresh prompt next time.", ct);
            else
                await Reply(inbound, "OK, cancelled. Nothing was added or recorded.", ct);
            return;
        }

        if (decision != "yes")
        {
            _logger.LogWarning("Unknown callback decision: {Decision}", decision);
            return;
        }

        if (consumed is null)
        {
            await Reply(inbound,
                "This action was already handled or expired. Send your sale again to start over.",
                ct);
            return;
        }

        // Sanity check: the action's business/user must match the bound chat. ConsumeAsync already
        // checks chat_id but not user — belt and braces.
        if (consumed.BusinessId != businessId || consumed.UserId != userId)
        {
            _logger.LogWarning("Pending action user mismatch: expected ({Biz}, {User}), got ({CBiz}, {CUser})",
                businessId, userId, consumed.BusinessId, consumed.UserId);
            return;
        }

        switch (consumed.ActionType)
        {
            case "add_product_and_sell":
                await ResumeAddProductAndSellAsync(consumed, inbound, ct);
                break;

            case "send_receipt":
                await ResumeSendReceiptAsync(consumed, inbound, ct);
                break;

            default:
                _logger.LogWarning("Unknown PendingTelegramAction type: {Type}", consumed.ActionType);
                await Reply(inbound, "I forgot what this was about. Try again.", ct);
                break;
        }
    }

    /// <summary>
    /// Resume handler for the "📄 Get Receipt" button. Reads the saved sale id, generates the PDF,
    /// pushes it as a Telegram document. The original sale row already has a ReceiptNumber (issued
    /// by IReceiptService.GenerateAsync on first call) so a follow-up tap would re-render the same
    /// receipt — but the button is single-use anyway since we strip the keyboard on tap.
    /// </summary>
    private async Task ResumeSendReceiptAsync(PendingActionConsumeResult consumed, ConversationMessage inbound, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<SendReceiptPayload>(consumed.PayloadJson);
        if (payload?.SaleId is null || payload.SaleId == Guid.Empty)
        {
            await Reply(inbound, "Couldn't find the sale to send a receipt for. Open the dashboard to download it.", ct);
            return;
        }
        await SendReceiptAsync(inbound, payload.SaleId, consumed.BusinessId, ct);
    }

    /// <summary>
    /// Resume handler for the "I don't have X — add it?" flow. Creates products for any names
    /// that are still unknown (a race-tolerant check — user may have added them via dashboard
    /// in the 30-min window), then records the sale with all items resolved.
    /// </summary>
    private async Task ResumeAddProductAndSellAsync(PendingActionConsumeResult consumed, ConversationMessage inbound, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<AddProductAndSellPayload>(consumed.PayloadJson);
        if (payload is null || payload.Items is null || payload.Items.Count == 0)
        {
            await Reply(inbound, "Couldn't read the saved sale details. Try sending the sale again.", ct);
            return;
        }

        var resolvedItems = new List<(Product Product, decimal Quantity, decimal UnitPrice)>();
        foreach (var item in payload.Items)
        {
            // Look up again in case the user added the product via dashboard while the prompt was
            // open. If still missing, create it on the fly.
            var (product, _) = await _resolver.FindProductAsync(consumed.BusinessId, item.ProductName, ct);
            if (product is null)
            {
                try
                {
                    var created = await _products.CreateAsync(consumed.BusinessId, new CreateProductRequest
                    {
                        Name = item.ProductName,
                        Unit = UnitInferrer.Infer(item.ProductName),
                        CostPrice = item.UnitPrice,           // sale price doubles as cost for v1; user can edit
                        SellingPrice = item.UnitPrice,
                        InitialStock = item.Quantity,         // assume current sale was already in stock — net 0 after sale
                        LowStockThreshold = 5,
                    }, recordedByUserId: consumed.UserId);
                    product = await _db.Products.FindAsync(new object?[] { created.Id }, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create product {Name} on the fly", item.ProductName);
                    await Reply(inbound, $"Couldn't add *{item.ProductName}*: {FriendlyErrorMessage(ex)}", ct);
                    return;
                }
            }
            if (product is null)
            {
                await Reply(inbound, $"Something went wrong creating *{item.ProductName}*. Try again.", ct);
                return;
            }
            resolvedItems.Add((product, item.Quantity, item.UnitPrice));
        }

        // Resolve customer if the original parse caught one.
        Guid? contactId = null;
        string? customerNameShown = null;
        if (!string.IsNullOrEmpty(payload.CustomerName))
        {
            var (contact, _) = await _resolver.FindContactAsync(consumed.BusinessId, payload.CustomerName, ct);
            contactId = contact?.Id;
            customerNameShown = contact?.Name ?? payload.CustomerName;
        }

        var request = new CreateSaleRequest
        {
            Items = resolvedItems.Select(r => new SaleItemRequest
            {
                ProductId = r.Product.Id,
                Quantity = r.Quantity,
                UnitPrice = r.UnitPrice,
            }).ToList(),
            ContactId = contactId,
            PaymentStatus = PaymentStatus.Paid,
        };

        SaleDto sale;
        try
        {
            sale = await _sales.CreateAsync(consumed.BusinessId, request, source: EntrySource.Telegram, recordedByUserId: consumed.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram resume-sale create failed for business {Business}", consumed.BusinessId);
            await Reply(inbound, $"Created the product(s) but couldn't record the sale: {FriendlyErrorMessage(ex)}", ct);
            return;
        }

        try { await _alerts.EmitPostSaleAlertsAsync(consumed.BusinessId, sale.TotalAmount, sale.Id, EntrySource.Telegram); }
        catch (Exception ex) { _logger.LogWarning(ex, "Post-sale alert emit failed (Telegram resume)"); }

        var customerLine = contactId.HasValue && !string.IsNullOrEmpty(sale.CustomerName)
            ? $" to {sale.CustomerName}"
            : !string.IsNullOrEmpty(customerNameShown) ? $" to {customerNameShown} (not in contacts)" : "";
        var receiptNumberLine = !string.IsNullOrEmpty(sale.ReceiptNumber) ? $" · #{sale.ReceiptNumber}" : "";
        var lineItems = string.Join("\n",
            resolvedItems.Select(r => $"• *{r.Quantity:0.##}× {r.Product.Name}* — {FormatAmount(r.Quantity * r.UnitPrice, consumed.BusinessId)}"));

        // Same receipt-on-demand pattern as HandleSaleAsync — button only, no auto-PDF.
        var quickReplies = await BuildReceiptButtonAsync(consumed.BusinessId, consumed.UserId, inbound.SenderIdentity, sale.Id, ct);

        await _telegram.SendAsync(inbound.SenderIdentity, new ReplyComposition
        {
            Text =
                $"✅ Added new product(s) and recorded the sale{customerLine}{receiptNumberLine}\n" +
                lineItems +
                $"\n*Total: {FormatAmount(sale.TotalAmount, consumed.BusinessId)}*",
            QuickReplies = quickReplies,
        }, ct);
    }

    /// <summary>
    /// Stashes a "send_receipt" pending action holding the sale id, returns a one-tap quick-reply
    /// button. The button's callback_data is <c>pa:receipt:&lt;token&gt;</c> — the existing
    /// callback dispatcher routes that to <see cref="HandleCallbackAsync"/> which reads the
    /// payload and pushes the PDF.
    /// </summary>
    private async Task<List<QuickReply>> BuildReceiptButtonAsync(Guid businessId, Guid userId, string chatId, Guid saleId, CancellationToken ct)
    {
        var token = await _pending.CreateAsync(
            businessId, userId,
            chatId,
            actionType: "send_receipt",
            payloadJson: JsonSerializer.Serialize(new SendReceiptPayload { SaleId = saleId }),
            ct);
        // Use the "pa:yes:" prefix (not "pa:receipt:") because the callback dispatcher only
        // routes "yes" and "no" decisions; everything else gets logged as unknown and dropped.
        // Decision name doesn't matter for receipt delivery — the dispatch switches on
        // PendingAction.ActionType, which is "send_receipt" for this button.
        return new List<QuickReply>
        {
            new("📄 Get Receipt", $"pa:yes:{token}"),
        };
    }

    // ── Serialization shapes for the pending payload ───────────────────────────

    private sealed class AddProductAndSellPayload
    {
        public List<AddProductAndSellPayloadItem> Items { get; set; } = new();
        public string? CustomerName { get; set; }
    }

    private sealed class AddProductAndSellPayloadItem
    {
        public string ProductName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }

    private sealed class SendReceiptPayload
    {
        public Guid SaleId { get; set; }
    }
}

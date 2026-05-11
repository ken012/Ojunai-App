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
using Ojunai.API.Services.Channels.Telegram;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services.Channels.Messenger;

/// <summary>
/// Implementation of <see cref="IMessengerIntentHandler"/>. Reuses the same channel-agnostic
/// services (Claude, Sales, Expense, Ledger, EntityResolver) that back the Telegram pipeline.
/// The pending-action service is also shared — its ChatId column stores any channel-native
/// sender identifier (Telegram chat_id, Messenger PSID).
///
/// Intent coverage mirrors Telegram (Phase 3c):
///   - <c>record_sale</c>            → ISalesService.CreateAsync (text confirmation only)
///   - <c>record_expense</c>         → IExpenseService.CreateAsync
///   - <c>record_receivable_payment</c> → ILedgerService.RecordPaymentAsync
///   - clarifications surfaced verbatim from Claude before dispatch
///
/// Unlike Telegram, sale confirmations carry no Get-Receipt button — Messenger can't deliver
/// the PDF natively the way Telegram's sendDocument does, and a button that only returns a
/// dashboard link adds visual noise without value. Owners can download PDFs from the
/// dashboard sales page. Quick replies also disappear from the chat automatically when tapped
/// on Messenger, so there's no equivalent to Telegram's editMessageReplyMarkup cleanup.
/// </summary>
public sealed class MessengerIntentHandler : IMessengerIntentHandler
{
    private readonly AppDbContext _db;
    private readonly IClaudeParsingService _claude;
    private readonly ISalesService _sales;
    private readonly IExpenseService _expenses;
    private readonly ILedgerService _ledger;
    private readonly IEntityResolverService _resolver;
    private readonly IProductService _products;
    private readonly IPendingTelegramActionService _pending;
    private readonly MessengerAdapter _messenger;
    private readonly IChatQueryService _queries;
    private readonly ILogger<MessengerIntentHandler> _logger;

    public MessengerIntentHandler(
        AppDbContext db,
        IClaudeParsingService claude,
        ISalesService sales,
        IExpenseService expenses,
        ILedgerService ledger,
        IEntityResolverService resolver,
        IProductService products,
        IPendingTelegramActionService pending,
        MessengerAdapter messenger,
        IChatQueryService queries,
        ILogger<MessengerIntentHandler> logger)
    {
        _db = db;
        _claude = claude;
        _sales = sales;
        _expenses = expenses;
        _ledger = ledger;
        _resolver = resolver;
        _products = products;
        _pending = pending;
        _messenger = messenger;
        _queries = queries;
        _logger = logger;
    }

    public async Task HandleAsync(ConversationMessage message, ContactIdentity boundIdentity, CancellationToken ct = default)
    {
        if (boundIdentity.UserId is null || boundIdentity.BusinessId is null)
        {
            _logger.LogWarning("MessengerIntentHandler invoked with unbound identity — orchestrator should have caught this");
            return;
        }

        var businessId = boundIdentity.BusinessId.Value;
        var userId = boundIdentity.UserId.Value;
        var rawText = message.Text ?? string.Empty;

        // Quick-reply / postback taps surface as ConversationMessage.Text set to the button's
        // payload string (MessengerAdapter pulls it from quick_reply.payload or postback.payload).
        // Same convention as Telegram: "pa:yes:<token>" / "pa:no:<token>".
        if (rawText.StartsWith("pa:", StringComparison.Ordinal))
        {
            await HandleCallbackAsync(rawText, businessId, userId, message, ct);
            return;
        }

        var context = await BuildBusinessContextAsync(businessId, ct);
        if (context is null)
        {
            await Reply(message, "I couldn't find your business record. Try logging into the dashboard first.", ct);
            return;
        }

        ParsedMessage parsed;
        try
        {
            parsed = await _claude.ParseAsync(rawText, context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Claude parse failed for Messenger PSID {Psid}", message.SenderIdentity);
            await Reply(message, "I had trouble understanding that. Try rephrasing — something like \"I sold 2 of X for 5000\".", ct);
            return;
        }

        if (parsed.NeedsClarification && !string.IsNullOrEmpty(parsed.ClarificationQuestion))
        {
            await Reply(message, parsed.ClarificationQuestion, ct);
            return;
        }

        switch (parsed.Intent)
        {
            case "create_sale":
                await HandleSaleAsync(parsed, businessId, userId, message, ct);
                break;

            case "create_expense":
                await HandleExpenseAsync(parsed, businessId, userId, message, ct);
                break;

            case "record_receivable_payment":
                await HandlePaymentAsync(parsed, businessId, userId, message, ct);
                break;

            // ── Read intents (Phase 3.6 — parity with WhatsApp's read commands) ──
            // Mirror of the case block in TelegramIntentHandler. Add new read intents in both
            // handlers (or refactor to a shared base — tracked as Phase 7 cleanup).
            case "get_today_sales":
            case "get_today_sales_detail":
                await Reply(message, await _queries.GetTodaySalesAsync(businessId, ct), ct);
                break;

            case "get_week_sales":
                await Reply(message, await _queries.GetWeekSalesAsync(businessId, ct), ct);
                break;

            case "get_all_stock":
                await Reply(message, await _queries.GetAllStockAsync(businessId, showPrices: false, ct), ct);
                break;

            case "get_low_stock":
                await Reply(message, await _queries.GetLowStockAsync(businessId, ct), ct);
                break;

            case "get_today_expenses":
                await Reply(message, await _queries.GetTodayExpensesAsync(businessId, ct), ct);
                break;

            case "get_recent_expenses":
                await Reply(message, await _queries.GetRecentExpensesAsync(businessId, ct), ct);
                break;

            case "get_outstanding_receivables":
                await Reply(message, await _queries.GetOutstandingAsync(businessId, "receivable", ct), ct);
                break;

            case "get_outstanding_payables":
                await Reply(message, await _queries.GetOutstandingAsync(businessId, "payable", ct), ct);
                break;

            case "get_customer_balance":
            case "get_supplier_balance":
                {
                    var contactName = parsed.BusinessAction.ValueKind == JsonValueKind.Object
                        && parsed.BusinessAction.TryGetProperty("contactName", out var cn)
                        && cn.ValueKind == JsonValueKind.String
                        ? cn.GetString()
                        : null;
                    await Reply(message, await _queries.GetContactBalanceAsync(businessId, contactName, ct), ct);
                    break;
                }

            case "get_daily_summary":
                await Reply(message, await _queries.GetDailySummaryAsync(businessId, ct), ct);
                break;

            case "get_profit_estimate":
                await Reply(message, await _queries.GetCashPositionAsync(businessId, ct), ct);
                break;

            case "help":
                await Reply(message, _queries.GetHelpText(), ct);
                break;

            case "greet":
                {
                    var businessName = await _db.Businesses
                        .AsNoTracking()
                        .Where(b => b.Id == businessId)
                        .Select(b => b.Name)
                        .FirstOrDefaultAsync(ct);
                    await Reply(message, _queries.GetGreetText(businessName), ct);
                    break;
                }

            case "unknown":
            case "smalltalk":
            default:
                await Reply(message,
                    "I can record sales, expenses, and customer payments, and answer questions " +
                    "like \"today's sales\", \"stock\", or \"who owes me\". Say \"help\" for the full list.",
                    ct);
                break;
        }
    }

    // ── Intent: create_sale ────────────────────────────────────────────────────
    private async Task HandleSaleAsync(
        ParsedMessage parsed,
        Guid businessId,
        Guid userId,
        ConversationMessage inbound,
        CancellationToken ct)
    {
        var items = ExtractSaleItems(parsed);
        if (items.Count == 0)
        {
            await Reply(inbound, "I caught a sale but couldn't pull the details. Try \"sold 2 of X for 5000\".", ct);
            return;
        }

        var pricedItems = DistributePricesAcrossItems(items, parsed.BusinessAction);

        var resolvedItems = new List<(Product Product, decimal Quantity, decimal UnitPrice)>();
        var unknownItems = new List<(string ProductName, decimal Quantity, decimal UnitPrice)>();

        foreach (var item in pricedItems)
        {
            var (product, _) = await _resolver.FindProductAsync(businessId, item.ProductName, ct);

            if (product is not null)
            {
                var unitPrice = item.UnitPrice > 0
                    ? item.UnitPrice
                    : product.SellingPrice ?? 0m;

                if (unitPrice <= 0)
                {
                    await Reply(inbound,
                        $"No selling price is set for {product.Name}. " +
                        $"Tell me what to charge — e.g. \"sold {item.Quantity} {product.Name} for [amount]\" — " +
                        "or set a default price in the dashboard.",
                        ct);
                    return;
                }
                resolvedItems.Add((product, item.Quantity, unitPrice));
            }
            else
            {
                if (item.UnitPrice <= 0)
                {
                    await Reply(inbound,
                        $"I don't have {item.ProductName} yet and you didn't mention a price. " +
                        $"Try \"sold {item.Quantity} {item.ProductName} for [amount]\".",
                        ct);
                    return;
                }
                unknownItems.Add((item.ProductName, item.Quantity, item.UnitPrice));
            }
        }

        if (unknownItems.Count > 0)
        {
            string? customerNameForLater = null;
            if (parsed.BusinessAction.ValueKind == JsonValueKind.Object
                && parsed.BusinessAction.TryGetProperty("contactName", out var cnEarly)
                && cnEarly.ValueKind == JsonValueKind.String)
            {
                customerNameForLater = cnEarly.GetString();
            }

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

            var unknownList = string.Join(", ", unknownItems.Select(u => u.ProductName));
            var addPromptButtons = new List<QuickReply>
            {
                new($"Yes, add {(unknownItems.Count == 1 ? "it" : "them")}", $"pa:yes:{token}"),
                new("No, cancel", $"pa:no:{token}"),
            };

            await _messenger.SendAsync(inbound.SenderIdentity, new ReplyComposition
            {
                Text = $"I don't have {unknownList} in your inventory yet. " +
                       $"Add {(unknownItems.Count == 1 ? "it" : "them")} now and record the sale?\n\n" +
                       "(I'll use the sale price as both cost and selling price — you can edit later from the dashboard.)",
                QuickReplies = addPromptButtons,
            }, ct);
            return;
        }

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
                customerName = contact?.Name ?? typedName;
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
            sale = await _sales.CreateAsync(businessId, request, source: "Messenger", recordedByUserId: userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Messenger sale create failed for business {Business}", businessId);
            await Reply(inbound, $"Couldn't record the sale: {FriendlyErrorMessage(ex)}", ct);
            return;
        }

        var customerLine = contactId.HasValue && !string.IsNullOrEmpty(sale.CustomerName)
            ? $" to {sale.CustomerName}"
            : !string.IsNullOrEmpty(customerName) ? $" to {customerName} (not in contacts)" : "";
        var receiptNumberLine = !string.IsNullOrEmpty(sale.ReceiptNumber) ? $" · #{sale.ReceiptNumber}" : "";

        var lineItems = string.Join("\n",
            resolvedItems.Select(r => $"• {r.Quantity:0.##}× {r.Product.Name} — {FormatAmount(r.Quantity * r.UnitPrice, businessId)}"));

        await _messenger.SendAsync(inbound.SenderIdentity, new ReplyComposition
        {
            Text =
                $"✅ Sale recorded{customerLine}{receiptNumberLine}\n" +
                lineItems +
                $"\nTotal: {FormatAmount(sale.TotalAmount, businessId)}",
        }, ct);
    }

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
    private readonly record struct PricedSaleItem(string ProductName, decimal Quantity, decimal UnitPrice);

    private static List<PricedSaleItem> DistributePricesAcrossItems(List<SaleItemParse> items, JsonElement businessAction)
    {
        if (items.Count == 0) return new();

        var priced = items.Select(i =>
        {
            var unitPrice = i.UnitPrice.HasValue && i.UnitPrice.Value > 0
                ? i.UnitPrice.Value
                : i.TotalAmount.HasValue && i.TotalAmount.Value > 0 && i.Quantity > 0
                    ? i.TotalAmount.Value / i.Quantity
                    : 0m;
            return new PricedSaleItem(i.ProductName, i.Quantity, unitPrice);
        }).ToList();

        if (priced.All(p => p.UnitPrice > 0)) return priced;

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

        var totalQuantityMissing = priced.Where(p => p.UnitPrice == 0).Sum(p => p.Quantity);
        if (totalQuantityMissing <= 0) return priced;

        var alreadyPaid = priced.Where(p => p.UnitPrice > 0).Sum(p => p.Quantity * p.UnitPrice);
        var remainder = grandTotal.Value - alreadyPaid;
        if (remainder <= 0) return priced;

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
            Notes = "Recorded via Messenger",
        };

        ExpenseDto expense;
        try
        {
            expense = await _expenses.CreateAsync(businessId, request, source: "Messenger", recordedByUserId: userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Messenger expense create failed for business {Business}", businessId);
            await Reply(inbound, $"Couldn't record the expense: {FriendlyErrorMessage(ex)}", ct);
            return;
        }

        await Reply(inbound,
            $"✅ Expense recorded\n{expense.Category}{(string.IsNullOrEmpty(paidTo) ? "" : $" — {paidTo}")}\n" +
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
            await Reply(inbound, contactError ?? $"I couldn't find {customerName} in your contacts.", ct);
            return;
        }

        var request = new RecordPaymentRequest
        {
            ContactId = contact.Id,
            Amount = amount,
            PaymentType = "Receivable",
            Notes = "Recorded via Messenger",
        };

        try
        {
            await _ledger.RecordPaymentAsync(businessId, request, source: "Messenger", recordedByUserId: userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Messenger payment record failed for business {Business}", businessId);
            await Reply(inbound, $"Couldn't record the payment: {FriendlyErrorMessage(ex)}", ct);
            return;
        }

        await Reply(inbound,
            $"✅ Payment recorded\n{contact.Name} paid {FormatAmount(amount, businessId)}",
            ct);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<BusinessContext?> BuildBusinessContextAsync(Guid businessId, CancellationToken ct)
    {
        var business = await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == businessId, ct);
        if (business is null) return null;

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

    private async Task Reply(ConversationMessage inbound, string text, CancellationToken ct)
    {
        await _messenger.SendAsync(inbound.SenderIdentity, new ReplyComposition { Text = text }, ct);
    }

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

        if (action.TryGetProperty("amount", out var amt) && amt.ValueKind == JsonValueKind.Number) amount = amt.GetDecimal();
        if (action.TryGetProperty("contactName", out var cn)) customerName = cn.GetString() ?? "";

        return amount > 0 && !string.IsNullOrEmpty(customerName);
    }

    // ── Callback handling ──────────────────────────────────────────────────────

    /// <summary>
    /// Messenger callbacks arrive as ConversationMessage.Text set to the button's payload.
    /// Same "pa:&lt;decision&gt;:&lt;token&gt;" convention as Telegram. Messenger quick replies
    /// disappear from the chat automatically when tapped, so we skip the explicit cleanup that
    /// Telegram needs (editMessageReplyMarkup).
    /// </summary>
    private async Task HandleCallbackAsync(string callbackData, Guid businessId, Guid userId, ConversationMessage inbound, CancellationToken ct)
    {
        var parts = callbackData.Split(':', 3);
        if (parts.Length != 3 || parts[0] != "pa")
        {
            _logger.LogWarning("Malformed Messenger callback payload: {Data}", callbackData);
            return;
        }
        var decision = parts[1];
        var token = parts[2];

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
            _logger.LogWarning("Unknown Messenger callback decision: {Decision}", decision);
            return;
        }

        if (consumed is null)
        {
            await Reply(inbound,
                "This action was already handled or expired. Send your sale again to start over.",
                ct);
            return;
        }

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

            default:
                _logger.LogWarning("Unknown pending action type: {Type}", consumed.ActionType);
                await Reply(inbound, "I forgot what this was about. Try again.", ct);
                break;
        }
    }

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
            var (product, _) = await _resolver.FindProductAsync(consumed.BusinessId, item.ProductName, ct);
            if (product is null)
            {
                try
                {
                    var created = await _products.CreateAsync(consumed.BusinessId, new CreateProductRequest
                    {
                        Name = item.ProductName,
                        Unit = UnitInferrer.Infer(item.ProductName),
                        CostPrice = item.UnitPrice,
                        SellingPrice = item.UnitPrice,
                        InitialStock = item.Quantity,
                        LowStockThreshold = 5,
                    }, recordedByUserId: consumed.UserId);
                    product = await _db.Products.FindAsync(new object?[] { created.Id }, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create product {Name} on the fly via Messenger", item.ProductName);
                    await Reply(inbound, $"Couldn't add {item.ProductName}: {FriendlyErrorMessage(ex)}", ct);
                    return;
                }
            }
            if (product is null)
            {
                await Reply(inbound, $"Something went wrong creating {item.ProductName}. Try again.", ct);
                return;
            }
            resolvedItems.Add((product, item.Quantity, item.UnitPrice));
        }

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
            sale = await _sales.CreateAsync(consumed.BusinessId, request, source: "Messenger", recordedByUserId: consumed.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Messenger resume-sale create failed for business {Business}", consumed.BusinessId);
            await Reply(inbound, $"Created the product(s) but couldn't record the sale: {FriendlyErrorMessage(ex)}", ct);
            return;
        }

        var customerLine = contactId.HasValue && !string.IsNullOrEmpty(sale.CustomerName)
            ? $" to {sale.CustomerName}"
            : !string.IsNullOrEmpty(customerNameShown) ? $" to {customerNameShown} (not in contacts)" : "";
        var receiptNumberLine = !string.IsNullOrEmpty(sale.ReceiptNumber) ? $" · #{sale.ReceiptNumber}" : "";
        var lineItems = string.Join("\n",
            resolvedItems.Select(r => $"• {r.Quantity:0.##}× {r.Product.Name} — {FormatAmount(r.Quantity * r.UnitPrice, consumed.BusinessId)}"));

        await _messenger.SendAsync(inbound.SenderIdentity, new ReplyComposition
        {
            Text =
                $"✅ Added new product(s) and recorded the sale{customerLine}{receiptNumberLine}\n" +
                lineItems +
                $"\nTotal: {FormatAmount(sale.TotalAmount, consumed.BusinessId)}",
        }, ct);
    }

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
}

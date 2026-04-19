using System.Text.Json;
using BizPilot.API.Common;
using BizPilot.API.Data;
using BizPilot.API.DTOs.Inventory;
using BizPilot.API.DTOs.Ledger;
using BizPilot.API.DTOs.Parsing;
using BizPilot.API.DTOs.Sales;
using BizPilot.API.Models;
using BizPilot.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace BizPilot.API.Services;

public class WhatsAppService : IWhatsAppService
{
    // In-memory rate limiter: phone → list of recent message timestamps
    private static readonly Dictionary<string, List<DateTime>> _rateLimits = new();
    private static readonly object _rateLimitLock = new();
    private const int RateLimitMaxMessages = 15;
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);

    // Tracks which sales have already triggered a "big sale" alert so we don't re-fire the same alert on
    // every subsequent stock-affecting message within the 2-minute recency window. In-memory is fine here
    // — a server restart at worst causes one duplicate alert, which is a trivial regression vs. the
    // current behavior of a duplicate alert on every message. Cleaned up opportunistically.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTime> _alertedSales = new();

    // Tracks (businessId, productId) → last-alerted-time for low-stock alerts. Without this, every sale of
    // a low-stock product re-fires the alert, which is noisy. The 60-minute cooldown means a user restocking
    // then going low again within the hour won't re-alert, but that's a rare edge case worth the UX trade.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Guid, Guid), DateTime> _alertedLowStock = new();
    private static readonly TimeSpan LowStockAlertCooldown = TimeSpan.FromMinutes(60);

    // Per-phone-number lock to prevent concurrent message processing for the same user.
    // Without this, two messages arriving in quick succession can read stale stock levels.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _phoneLocks = new();

    private string _cs = "{_cs}";
    private TimeZoneInfo _tz = TimeZoneInfo.Utc;

    /// <summary>
    /// Returns an error message safe to show to end users over WhatsApp. Business-logic exceptions
    /// (our own InvalidOperationException/KeyNotFoundException/ArgumentException) carry meaningful
    /// messages like "Insufficient stock for rice" and are surfaced verbatim. Everything else —
    /// database errors, connection timeouts, Npgsql driver errors — gets a generic line so we don't
    /// leak technical internals like "The connection is already in a transaction" to customers.
    /// The full exception is always logged separately for diagnosis.
    /// </summary>
    private static string FriendlyErrorMessage(Exception ex) =>
        ex is InvalidOperationException or KeyNotFoundException or ArgumentException
            ? ex.Message
            : "something went wrong on our end. Please try again in a moment.";

    // Regex matchers for context-free smalltalk — hitting these skips the Claude call entirely.
    // Kept deliberately narrow: only messages that cannot possibly be completing a pending intent
    // get short-circuited. Ambiguous single-word replies like "ok", "yes", "sure" still go to Claude
    // because they may be confirmations of a previously proposed action.
    private static readonly System.Text.RegularExpressions.Regex GreetingRegex = new(
        @"^(hi|hello|hey|hiya|howdy|good\s*(morning|afternoon|evening|day)|greetings)[!\.\s]*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex ThanksRegex = new(
        @"^(thanks|thank\s*you|thx|ty|much\s*appreciated|appreciated|gracias)[!\.\s]*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// If the message is an unambiguous greeting or thanks AND there's no pending question from the bot,
    /// returns a canned reply. Otherwise returns null so the caller falls through to Claude parsing.
    /// A "pending question" is detected by checking whether the most recent assistant message in history
    /// did NOT start with ✅ (the marker we use for completed actions) — that indicates the bot asked
    /// something that might still need answering.
    ///
    /// Greetings delegate to HandleGreet so the response matches exactly what users got before this
    /// short-circuit was added. Only the route is different (skipping Claude) — the message is the same.
    /// </summary>
    private static string? TryHandleSmalltalk(string text, List<(string Role, string Content)> history, string businessName)
    {
        var trimmed = text.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        // If the last assistant message was a pending question (not ✅ completed), don't short-circuit —
        // let Claude interpret the reply in context. Even a "hi" mid-conversation might be a deliberate
        // topic switch that needs to abandon the pending action, which the system prompt handles.
        var lastAssistant = history.LastOrDefault(h => h.Role == "assistant");
        if (lastAssistant != default && !lastAssistant.Content.TrimStart().StartsWith("✅")
            && !lastAssistant.Content.Contains("Welcome") // don't let onboarding/welcome banners suppress smalltalk
           )
        {
            return null;
        }

        if (GreetingRegex.IsMatch(trimmed))
        {
            return HandleGreet(businessName);
        }

        if (ThanksRegex.IsMatch(trimmed))
        {
            return "You're welcome! Anything else I can help with?";
        }

        return null;
    }

    // Lifetime of a pending action before it's considered stale. Ten minutes is long enough for a user
    // to handle a real-world interruption (customer at the counter, phone call) and short enough that
    // stale questions don't bleed into unrelated new conversations.
    private static readonly TimeSpan PendingActionLifetime = TimeSpan.FromMinutes(10);

    // Negative / cancellation keywords that abandon any pending action.
    private static readonly HashSet<string> CancelWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "cancel", "nevermind", "never mind", "stop", "abort", "no", "scratch that",
        "forget it", "wait", "actually no", "don't", "dont"
    };

    /// <summary>
    /// Upsert a pending action for this (business, user). Overwrites any prior pending row — a user only
    /// ever has one "we're waiting on an answer" record at a time. The unique index on (BusinessId, UserId)
    /// enforces this at the DB layer.
    /// </summary>
    private async Task SetPendingActionAsync(Guid businessId, Guid userId, string intent, string payloadJson, string awaitingField, string question)
    {
        var existing = await _db.PendingActions
            .FirstOrDefaultAsync(p => p.BusinessId == businessId && p.UserId == userId);

        if (existing != null)
        {
            existing.Intent = intent;
            existing.PartialPayloadJson = payloadJson;
            existing.AwaitingField = awaitingField;
            existing.QuestionText = question;
            existing.CreatedAtUtc = DateTime.UtcNow;
            existing.ExpiresAtUtc = DateTime.UtcNow.Add(PendingActionLifetime);
        }
        else
        {
            _db.PendingActions.Add(new PendingAction
            {
                BusinessId = businessId,
                UserId = userId,
                Intent = intent,
                PartialPayloadJson = payloadJson,
                AwaitingField = awaitingField,
                QuestionText = question,
                ExpiresAtUtc = DateTime.UtcNow.Add(PendingActionLifetime)
            });
        }
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Fetch a user's pending action if present and unexpired. Expired rows are deleted as a side effect
    /// (lazy cleanup — no separate cron needed for a low-churn table).
    /// </summary>
    private async Task<PendingAction?> GetPendingActionAsync(Guid businessId, Guid userId)
    {
        var pending = await _db.PendingActions
            .FirstOrDefaultAsync(p => p.BusinessId == businessId && p.UserId == userId);
        if (pending == null) return null;
        if (pending.ExpiresAtUtc < DateTime.UtcNow)
        {
            _db.PendingActions.Remove(pending);
            await _db.SaveChangesAsync();
            return null;
        }
        return pending;
    }

    private async Task ClearPendingActionAsync(Guid businessId, Guid userId)
    {
        var pending = await _db.PendingActions
            .FirstOrDefaultAsync(p => p.BusinessId == businessId && p.UserId == userId);
        if (pending != null)
        {
            _db.PendingActions.Remove(pending);
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Heuristically infer what field the clarification question is asking for, by scanning the question
    /// text for common patterns. Falls back to "value" if we can't tell. This is just a hint for Claude
    /// on the next turn — it's not critical if we guess wrong.
    /// </summary>
    private static string InferAwaitingField(string question)
    {
        var lower = question.ToLowerInvariant();
        if (lower.Contains("price") || lower.Contains("cost") || lower.Contains("how much")) return "unitPrice";
        if (lower.Contains("quantity") || lower.Contains("how many") || lower.Contains("how much")) return "quantity";
        if (lower.Contains("customer") || lower.Contains("who") || lower.Contains("their name")) return "contactName";
        if (lower.Contains("product") || lower.Contains("what item")) return "productName";
        if (lower.Contains("amount")) return "amount";
        if (lower.Contains("category")) return "category";
        return "value";
    }

    /// <summary>
    /// When Claude returns low confidence without a clarification question, generate a contextual fallback
    /// with examples based on the partially-detected intent. This replaces the old generic
    /// "Could you be more specific?" which taught users nothing.
    /// </summary>
    private static string BuildClarificationFallback(string intent, string originalText)
    {
        var examples = intent switch
        {
            "create_sale" => "• \"Sold 3 bags of rice at 5000\"\n• \"Sold 5 rice to Ama on credit\"\n• \"Sold 2 beans worth 3000\"",
            "create_expense" => "• \"Spent 5k on transport\"\n• \"Paid 100k for rent\"\n• \"Bought airtime 1000\"",
            "add_inventory" => "• \"Bought 30 bags rice at 4000\"\n• \"Received 50 shampoo from supplier\"\n• \"Restocked 10 cartons juice\"",
            "create_receivable" => "• \"Ama owes me 20k\"\n• \"Kofi owes 50k for the rice\"",
            "create_payable" => "• \"I owe Market Mama 30k\"\n• \"Owe NEPA 10k\"",
            "record_receivable_payment" or "record_payable_payment" => "• \"Ama paid 10k\"\n• \"Clear Kofi's debt\"\n• \"Clear all debts\"",
            "update_product_price" => "• \"Rice now sells at 5000\"\n• \"Reduce shampoo cost by 500\"",
            "hold_stock" => "• \"Hold 5 bags of rice for Ama\"",
            "release_hold" => "• \"Ama came for her rice\"\n• \"Release Ama's hold\"",
            _ => "• \"Sold 3 bags of rice at 3000\"\n• \"Bought 10 shampoo at 1200\"\n• \"Check stock\"\n• \"Today's sales\""
        };

        var header = intent switch
        {
            "create_sale" => "I think you're recording a sale but I'm missing some details. Try one of these:",
            "create_expense" => "I think you're recording an expense but I'm missing some details. Try:",
            "add_inventory" => "I think you're restocking but I'm missing some details. Try:",
            "create_receivable" or "create_payable" => "I think you're recording a debt but need a bit more detail. Try:",
            "record_receivable_payment" or "record_payable_payment" => "I think you're recording a payment but need more detail. Try:",
            _ => "I'm not sure what you'd like me to do. Try one of these:"
        };

        return $"{header}\n\n{examples}\n\nOr type *help* to see all commands.";
    }

    private readonly AppDbContext _db;
    private readonly IClaudeParsingService _claude;
    private readonly ISalesService _sales;
    private readonly IExpenseService _expenses;
    private readonly IInventoryService _inventory;
    private readonly ILedgerService _ledger;
    private readonly IReportService _reports;
    private readonly IStockHoldService _holds;
    private readonly PlanGuard _planGuard;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _config;
    private readonly ILogger<WhatsAppService> _logger;

    public WhatsAppService(
        AppDbContext db,
        IClaudeParsingService claude,
        ISalesService sales,
        IExpenseService expenses,
        IInventoryService inventory,
        ILedgerService ledger,
        IReportService reports,
        IStockHoldService holds,
        PlanGuard planGuard,
        IServiceProvider serviceProvider,
        IConfiguration config,
        ILogger<WhatsAppService> logger)
    {
        _db = db;
        _claude = claude;
        _sales = sales;
        _expenses = expenses;
        _inventory = inventory;
        _ledger = ledger;
        _reports = reports;
        _holds = holds;
        _planGuard = planGuard;
        _serviceProvider = serviceProvider;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Main entry point for every inbound WhatsApp message. Called from WebhooksController after Twilio signature verification.
    /// Flow: idempotency → rate limit → user lookup → onboarding (if new) → plan check → Claude parsing → intent execution.
    /// </summary>
    public async Task HandleInboundAsync(string from, string messageId, string text)
    {
        // Twilio sometimes retries webhooks (e.g., our response took too long). Skip if we've already processed this message.
        if (await _db.MessageLogs.AnyAsync(m => m.WhatsAppMessageId == messageId))
        {
            _logger.LogInformation("Duplicate message {MessageId}, skipping.", messageId);
            return;
        }

        // Twilio sends the sender as "whatsapp:+2348012345678". Strip the prefix and normalize to E.164.
        var phone = NormalizePhone(from.Replace("whatsapp:", "").Trim());

        // In-memory rate limiter: 15 messages per minute per phone number. Prevents abuse and runaway loops.
        if (IsRateLimited(phone))
        {
            _logger.LogWarning("Rate limit hit for phone ending in {Last4}", RedactPhone(phone));
            await SendMessageAsync(from, "⏳ You're sending messages too fast. Please wait a minute and try again.");
            return;
        }

        // Acquire per-phone lock to prevent concurrent processing of messages from the same number.
        var phoneLock = _phoneLocks.GetOrAdd(phone, _ => new SemaphoreSlim(1, 1));
        await phoneLock.WaitAsync();
        try
        {

        // Only look up ACTIVE users attached to ACTIVE businesses. Deactivated staff / businesses are treated as unknown.
        var user = await _db.Users
            .Include(u => u.Business)
            .FirstOrDefaultAsync(u => u.PhoneNumber == phone && u.IsActive && u.Business.IsActive);

        // Log the inbound message before processing — this ensures we can audit even failed interactions.
        // UserId and BusinessId are null for unknown numbers (onboarding flow will set them after account creation).
        var log = new MessageLog
        {
            WhatsAppMessageId = messageId,
            BusinessId = user?.BusinessId,
            UserId = user?.Id,
            Direction = MessageDirection.Inbound,
            RawMessage = text,
            ProcessingStatus = MessageProcessingStatus.Received
        };
        _db.MessageLogs.Add(log);
        await _db.SaveChangesAsync();

        // Unknown number OR deactivated account → hand off to the onboarding flow.
        // Onboarding presents a menu (1=new business, 2=staff, 3=help) and guides new signups.
        if (user == null)
        {
            var onboarding = _serviceProvider.GetRequiredService<OnboardingService>();
            var step = await onboarding.HandleIfOnboardingAsync(from, phone, text);
            if (step != null)
            {
                // The onboarding service returns the step name, which we store on the log for funnel analytics.
                log.ProcessingStatus = MessageProcessingStatus.Executed;
                log.ParsedIntent = step;
                await _db.SaveChangesAsync();
                return;
            }
            log.ProcessingStatus = MessageProcessingStatus.Failed;
            await _db.SaveChangesAsync();
            return;
        }

        // Enforce monthly message limit per plan (e.g., Starter = 150/month, Shop = 850, Pro = unlimited).
        // This only counts registered users' messages — onboarding messages (null BusinessId) are excluded.
        var msgLimitErr = await _planGuard.CheckMessageLimitAsync(user.BusinessId);
        if (msgLimitErr != null)
        {
            await SendMessageAsync(from, $"⚠️ {msgLimitErr}");
            log.ProcessingStatus = MessageProcessingStatus.Failed;
            await _db.SaveChangesAsync();
            return;
        }

        // Build context. Product and contact lists are passed to Claude in the system prompt so it can
        // resolve names against real inventory. For small businesses the full list fits easily; for larger
        // ones (500+ products) we truncate by recent-sale frequency to keep prompt size and token cost
        // bounded. Claude's own fuzzy-match guidance plus our server-side FindProductAsync handle
        // references to products not in the prompt.
        const int MaxProductsInPrompt = 50;

        var totalProducts = await _db.Products
            .CountAsync(p => p.BusinessId == user.BusinessId && p.IsActive);

        List<ProductContext> products;
        if (totalProducts <= MaxProductsInPrompt)
        {
            products = await _db.Products
                .Where(p => p.BusinessId == user.BusinessId && p.IsActive)
                .OrderBy(p => p.Name)
                .Select(p => new ProductContext(p.Name, p.Unit, p.CurrentStock, p.Category))
                .ToListAsync();
        }
        else
        {
            // Rank products by recent sale velocity. Products sold in the last 30 days appear first,
            // then pad with newest-created products so never-sold-but-recently-added items aren't hidden.
            var cutoff = DateTime.UtcNow.AddDays(-30);
            var velocity = await _db.SaleItems
                .Where(si => si.Sale.BusinessId == user.BusinessId && si.Sale.CreatedAtUtc >= cutoff)
                .GroupBy(si => si.ProductId)
                .Select(g => new { ProductId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ProductId, x => x.Count);

            var allProducts = await _db.Products
                .Where(p => p.BusinessId == user.BusinessId && p.IsActive)
                .ToListAsync();

            products = allProducts
                .OrderByDescending(p => velocity.GetValueOrDefault(p.Id, 0))
                .ThenByDescending(p => p.CreatedAtUtc)
                .Take(MaxProductsInPrompt)
                .Select(p => new ProductContext(p.Name, p.Unit, p.CurrentStock, p.Category))
                .ToList();
        }

        var contacts = await _db.Contacts
            .Where(c => c.BusinessId == user.BusinessId)
            .Select(c => new ContactContext(c.Name, c.Type.ToString()))
            .ToListAsync();

        _cs = BillingConfig.Symbol(user.Business.Currency);
        _tz = TimeZoneInfo.FindSystemTimeZoneById(user.Business.Timezone ?? "Africa/Lagos");

        var context = new BusinessContext
        {
            BusinessName = user.Business.Name,
            Currency = user.Business.Currency,
            Timezone = user.Business.Timezone ?? "Africa/Lagos",
            Products = products,
            Contacts = contacts,
            TotalProducts = totalProducts
        };

        // Fetch last ~5 turns as conversation history for Claude
        var recentLogs = await _db.MessageLogs
            .Where(m => m.BusinessId == user.BusinessId && m.UserId == user.Id
                        && m.WhatsAppMessageId != messageId
                        && m.ProcessingStatus != MessageProcessingStatus.Received)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(10)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync();

        // Build conversation history for Claude. Role = user/assistant. Content is the raw message text
        // as stored in the log (for both inbound and outbound — outbound is what the bot sent).
        var history = recentLogs.Select(m => (
            Role: m.Direction == MessageDirection.Inbound ? "user" : "assistant",
            Content: m.RawMessage
        )).ToList();

        // Fast-path for unambiguous smalltalk. Skips the Claude roundtrip for pure greetings and
        // acknowledgments, saving tokens and latency. We deliberately only handle cases where the reply
        // is context-free — confirmations like "ok" or "yes" still go to Claude because they may
        // be completing a pending action.
        var smalltalk = TryHandleSmalltalk(text, history, user.Business.Name);
        if (smalltalk != null)
        {
            await SendMessageAsync(from, smalltalk, user.BusinessId, user.Id);
            log.ProcessingStatus = MessageProcessingStatus.Executed;
            log.ParsedIntent = "smalltalk";
            await _db.SaveChangesAsync();
            return;
        }

        // Check for a pending action — a prior message where the bot asked for specific info that wasn't
        // provided. If the user's reply is an explicit cancellation, abandon it. Otherwise the pending
        // action is passed to Claude as extra context so it can merge the answer into the partial payload.
        var pending = await GetPendingActionAsync(user.BusinessId, user.Id);
        if (pending != null)
        {
            var trimmedText = text.Trim();
            if (CancelWords.Contains(trimmedText))
            {
                await ClearPendingActionAsync(user.BusinessId, user.Id);
                await SendMessageAsync(from, "OK, cancelled. What would you like to do instead?", user.BusinessId, user.Id);
                log.ProcessingStatus = MessageProcessingStatus.Executed;
                log.ParsedIntent = "cancel_pending";
                await _db.SaveChangesAsync();
                return;
            }
        }

        // If there's a pending action, attach it to the context so Claude knows what we're waiting for.
        // The system prompt already explains how to merge the user's reply into the partial payload.
        if (pending != null)
        {
            context.PendingAction = new PendingActionContext
            {
                Intent = pending.Intent,
                AwaitingField = pending.AwaitingField,
                QuestionText = pending.QuestionText,
                PartialPayloadJson = pending.PartialPayloadJson
            };
        }

        ParsedMessage parsed;
        try
        {
            parsed = await _claude.ParseAsync(text, context, history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Claude parse failed for {MessageId}", messageId);
            await SendMessageAsync(from, "Sorry, I had trouble understanding that. Please try again.", user.BusinessId, user.Id);
            log.ProcessingStatus = MessageProcessingStatus.Failed;
            await _db.SaveChangesAsync();
            return;
        }

        log.ParsedIntent = parsed.Intent;
        log.ConfidenceScore = (decimal)parsed.Confidence;
        log.ParsedPayloadJson = JsonSerializer.Serialize(parsed.BusinessAction);
        log.ProcessingStatus = MessageProcessingStatus.Parsed;
        await _db.SaveChangesAsync();

        // Four-tier confidence handling (conservative — preserves pre-existing behavior for <0.75):
        //   very high (>= 0.90): execute silently (same as before)
        //   high      (0.75 - 0.90): execute with a small "🤔 not 100% sure" nudge so users can catch mis-parses (NEW — previously silent)
        //   medium    (0.60 - 0.75): don't execute — ask for clarification (same as before)
        //   low       (< 0.60): don't execute — ask for clarification with intent-specific examples (same gate, richer message + pending action)
        var isLow = parsed.NeedsClarification || parsed.Confidence < 0.75;
        var isMedium = !isLow && parsed.Confidence < 0.90;

        if (isLow)
        {
            var message = !string.IsNullOrWhiteSpace(parsed.ClarificationQuestion)
                ? parsed.ClarificationQuestion!
                : BuildClarificationFallback(parsed.Intent, text);
            await SendMessageAsync(from, message, user.BusinessId, user.Id);
            log.ProcessingStatus = MessageProcessingStatus.NeedsClarification;

            // Persist the partial intent + payload as a pending action so the user's next reply can
            // complete it deterministically. If Claude gave us a partial intent (not "unknown"), we
            // save it — otherwise a follow-up like "6000" would have nothing to merge into.
            if (!string.IsNullOrWhiteSpace(parsed.Intent) && parsed.Intent != "unknown")
            {
                var awaitingField = InferAwaitingField(message);
                await SetPendingActionAsync(
                    user.BusinessId,
                    user.Id,
                    parsed.Intent,
                    JsonSerializer.Serialize(parsed.BusinessAction),
                    awaitingField,
                    message);
            }

            await _db.SaveChangesAsync();
            return;
        }

        try
        {
            var reply = await ExecuteIntentAsync(user, parsed);

            // Medium-confidence tier: we still executed, but add a small "double-check" hint so the user
            // can catch mis-parses quickly. This replaces the old behavior of bailing out at 0.60-0.75.
            if (isMedium)
            {
                reply += "\n\n🤔 I wasn't 100% sure about that one — let me know if I got it wrong.";
            }

            var trialWarning = await _planGuard.GetTrialWarningAsync(user.BusinessId);
            if (trialWarning != null)
                reply += $"\n\n⚠️ {trialWarning}";

            await SendMessageAsync(from, reply, user.BusinessId, user.Id);
            log.ProcessingStatus = MessageProcessingStatus.Executed;

            // A successful execution resolves whatever question was pending. Clear the pending row so
            // the next message is handled as a fresh request, not an answer to an already-done question.
            if (pending != null)
            {
                await ClearPendingActionAsync(user.BusinessId, user.Id);
            }

            // Fire proactive alerts after stock-affecting intents
            if (parsed.Intent is "create_sale" or "remove_inventory" or "mark_damaged_inventory" or "batch_action" or "correct_last_sale" or "return_product" or "release_hold")
            {
                await CheckAndSendAlertsAsync(from, user);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Intent execution failed for {Intent}", parsed.Intent);
            await SendMessageAsync(from, $"Sorry, couldn't complete that: {FriendlyErrorMessage(ex)}", user.BusinessId, user.Id);
            log.ProcessingStatus = MessageProcessingStatus.Failed;
        }

        await _db.SaveChangesAsync();

        } // end try (phoneLock)
        finally
        {
            phoneLock.Release();
        }
    }

    private async Task CheckAndSendAlertsAsync(string to, User user)
    {
        var businessId = user.BusinessId;
        var business = await _db.Businesses.FindAsync(businessId);
        if (business == null) return;

        var alerts = new List<string>();

        // 1. Low stock + stock-out alerts (if enabled) — deduped per (business, product) with a 60-min
        // cooldown so sequential sales of the same low-stock product don't spam the user.
        if (business.AlertLowStock)
        {
            var lowStockProducts = await _db.Products
                .Where(p => p.BusinessId == businessId && p.IsActive && p.CurrentStock <= p.LowStockThreshold)
                .OrderBy(p => p.CurrentStock)
                .Take(5)
                .ToListAsync();

            var now = DateTime.UtcNow;
            foreach (var p in lowStockProducts)
            {
                var key = (businessId, p.Id);
                // Check existing entry — if alerted within cooldown, skip. Otherwise record and alert.
                if (_alertedLowStock.TryGetValue(key, out var lastAlerted) && (now - lastAlerted) < LowStockAlertCooldown)
                    continue;
                _alertedLowStock[key] = now;

                if (p.CurrentStock <= 0)
                    alerts.Add($"🚫 *{p.Name}* is out of stock — reorder now!");
                else
                    alerts.Add($"⚠️ *{p.Name}* is running low — {p.CurrentStock:0.##} {p.Unit} left (threshold: {p.LowStockThreshold:0.##})");
            }

            // Opportunistic cleanup of expired cooldown entries. Bounded by cooldown, so the dict
            // never grows beyond the count of recently-alerted (product, business) pairs.
            var expireCutoff = now - LowStockAlertCooldown;
            foreach (var kv in _alertedLowStock.ToArray())
            {
                if (kv.Value < expireCutoff) _alertedLowStock.TryRemove(kv.Key, out _);
            }
        }

        // 2. Large sale alert (if enabled) — only fires once per sale to prevent the alert from
        // re-echoing on every subsequent stock-affecting message within the 2-minute window.
        if (business.AlertLargeSale && business.LargeSaleThreshold > 0)
        {
            var recentSale = await _db.Sales
                .Where(s => s.BusinessId == businessId)
                .OrderByDescending(s => s.CreatedAtUtc)
                .FirstOrDefaultAsync();

            if (recentSale != null && recentSale.TotalAmount >= business.LargeSaleThreshold)
            {
                var timeSince = DateTime.UtcNow - recentSale.CreatedAtUtc;
                if (timeSince.TotalMinutes < 2 && _alertedSales.TryAdd(recentSale.Id, DateTime.UtcNow))
                {
                    alerts.Add($"💰 *Big sale!* {_cs}{recentSale.TotalAmount:N0} just recorded");
                }
            }

            // Opportunistic cleanup: drop tracking entries older than 10 minutes so the dictionary doesn't
            // grow unboundedly over time. We only clean when we check, which is naturally rate-limited.
            var staleCutoff = DateTime.UtcNow.AddMinutes(-10);
            foreach (var kv in _alertedSales.ToArray())
            {
                if (kv.Value < staleCutoff) _alertedSales.TryRemove(kv.Key, out _);
            }
        }

        if (alerts.Count > 0)
        {
            var alertMsg = $"🔔 *Alerts*\n{string.Join("\n", alerts)}";
            await SendMessageAsync(to, alertMsg, businessId, user.Id);
        }
    }

    private static readonly Dictionary<string, string> IntentPermissions = new()
    {
        ["create_sale"] = Permission.RecordSales,
        ["create_expense"] = Permission.RecordExpenses,
        ["add_inventory"] = Permission.ManageStock,
        ["remove_inventory"] = Permission.ManageStock,
        ["mark_damaged_inventory"] = Permission.ManageStock,
        ["create_receivable"] = Permission.ManageDebts,
        ["create_payable"] = Permission.ManageDebts,
        ["record_receivable_payment"] = Permission.ManageDebts,
        ["record_payable_payment"] = Permission.ManageDebts,
        ["create_product"] = Permission.ManageStock,
        ["update_product_price"] = Permission.ManageStock,
        ["update_low_stock_threshold"] = Permission.ManageStock,
        ["delete_product"] = Permission.ManageStock,
        ["hold_stock"] = Permission.ManageStock,
        ["release_hold"] = Permission.ManageStock,
        ["correct_last_sale"] = Permission.VoidSales,
        ["correct_last_expense"] = Permission.RecordExpenses,
        ["update_last_sale"] = Permission.VoidSales,
        ["correct_debt"] = Permission.ManageDebts,
        ["undo_last_action"] = Permission.VoidSales,
        ["stocktake"] = Permission.ManageStock,
        ["add_staff"] = Permission.ManageStaff,
        ["subscribe"] = Permission.ManageSettings,
        ["cancel_subscription"] = Permission.ManageSettings,
        ["cancel_plan_change"] = Permission.ManageSettings,
        ["return_product"] = Permission.ManageStock,
        ["repeat_last_sale"] = Permission.RecordSales,
        ["add_to_last_sale"] = Permission.RecordSales,
        ["batch_action"] = Permission.RecordSales,
    };
    // Query intents (get_*) → no permission check needed, all roles can query

    private async Task<string> ExecuteIntentAsync(User user, ParsedMessage parsed)
    {
        // Permission check
        if (IntentPermissions.TryGetValue(parsed.Intent, out var requiredPermission))
        {
            if (!RolePermissions.HasPermission(user.Role, requiredPermission))
                return $"⛔ Your role ({user.Role}) doesn't have permission for this action. Ask the business owner to help.";
        }

        var businessId = user.BusinessId;
        var ba = parsed.BusinessAction;

        return parsed.Intent switch
        {
            "create_sale" => await HandleCreateSaleAsync(businessId, ba, user),
            "create_expense" => await HandleCreateExpenseAsync(businessId, ba, user),
            "add_inventory" => await HandleAddInventoryAsync(businessId, ba, user),
            "remove_inventory" => await HandleRemoveInventoryAsync(businessId, ba, user),
            "mark_damaged_inventory" => await HandleMarkDamagedAsync(businessId, ba, user),
            "create_receivable" => await HandleCreateReceivableAsync(businessId, ba, user),
            "create_payable" => await HandleCreatePayableAsync(businessId, ba, user),
            "record_receivable_payment" => await HandleRecordPaymentAsync(businessId, ba, "receivable", user),
            "record_payable_payment" => await HandleRecordPaymentAsync(businessId, ba, "payable", user),
            "create_product" => await HandleCreateProductAsync(businessId, ba, user),
            "update_product_price" => await HandleUpdateProductPriceAsync(businessId, ba, user),
            "get_today_sales" => await HandleGetTodaySalesAsync(businessId),
            "get_daily_summary" => await HandleGetDailySummaryAsync(businessId),
            "get_week_sales" => await HandleGetWeekSalesAsync(businessId),
            "get_all_stock" => await HandleGetAllStockAsync(businessId, ba),
            "get_low_stock" => await HandleGetLowStockAsync(businessId),
            "get_outstanding_receivables" => await HandleGetOutstandingAsync(businessId, "receivable"),
            "get_outstanding_payables" => await HandleGetOutstandingAsync(businessId, "payable"),
            "get_customer_balance" => await HandleGetContactBalanceAsync(businessId, ba),
            "get_supplier_balance" => await HandleGetContactBalanceAsync(businessId, ba),
            "get_profit_estimate" => await HandleGetProfitEstimateAsync(businessId),
            "get_top_products" => await HandleGetTopProductsAsync(businessId, ba),
            "get_today_sales_detail" => await HandleGetTodaySalesDetailAsync(businessId),
            "get_product_sales_today" => await HandleGetProductSalesTodayAsync(businessId, ba),
            "get_specific_stock" => await HandleGetSpecificStockAsync(businessId, ba),
            "get_staff_sales" => await HandleGetStaffSalesAsync(businessId, ba),
            "get_product_staff" => await HandleGetProductStaffAsync(businessId, ba),
            "get_product_buyers" => await HandleGetProductBuyersAsync(businessId, ba),
            "get_transaction_history" => await HandleGetTransactionHistoryAsync(businessId),
            "get_dead_stock" => await HandleGetDeadStockAsync(businessId),
            "get_profit_by_product" => await HandleGetProfitByProductAsync(businessId),
            "get_stockout_prediction" => await HandleGetStockoutPredictionAsync(businessId),
            "get_today_expenses" => await HandleGetTodayExpensesAsync(businessId),
            "get_recent_expenses" => await HandleGetRecentExpensesAsync(businessId),
            "correct_last_expense" => await HandleCorrectLastExpenseAsync(businessId, ba, user),
            "update_low_stock_threshold" => await HandleUpdateLowStockThresholdAsync(businessId, ba),
            "delete_product" => await HandleDeleteProductAsync(businessId, ba),
            "hold_stock" => await HandleHoldStockAsync(businessId, ba),
            "release_hold" => await HandleReleaseHoldAsync(businessId, ba),
            "get_active_holds" => await HandleGetActiveHoldsAsync(businessId),
            "add_staff" => await HandleAddStaffAsync(businessId, ba),
            "get_staff_list" => await HandleGetStaffListAsync(businessId),
            "create_contact" => await HandleCreateContactAsync(businessId, ba),
            "correct_last_sale" => await HandleCorrectLastSaleAsync(businessId, ba, user),
            "update_last_sale" => await HandleUpdateLastSaleAsync(businessId, ba, user),
            "correct_debt" => await HandleCorrectDebtAsync(businessId, ba, user),
            "undo_last_action" => await HandleUndoLastActionAsync(businessId, user),
            "return_product" => await HandleReturnProductAsync(businessId, ba, user),
            "stocktake" => await HandleStocktakeAsync(businessId, ba, user),
            "get_week_comparison" => await HandleGetWeekComparisonAsync(businessId),
            "get_product_profit" => await HandleGetProductProfitAsync(businessId, ba),
            "get_stock_value" => await HandleGetStockValueAsync(businessId),
            "repeat_last_sale" => await HandleRepeatLastSaleAsync(businessId, user),
            "add_to_last_sale" => await HandleAddToLastSaleAsync(businessId, ba, user),
            "batch_action" => await HandleBatchActionAsync(user, ba),
            "get_my_account" => HandleGetMyAccount(user),
            "get_my_plan" => await HandleGetMyPlanAsync(businessId),
            "get_plans" => HandleGetPlans(),
            "subscribe" => await HandleSubscribeAsync(businessId, ba, user),
            "cancel_subscription" => await HandleCancelSubscriptionAsync(businessId, ba),
            "cancel_plan_change" => await HandleCancelPlanChangeAsync(businessId),
            "greet" => HandleGreet(user.Business.Name),
            "help" => HandleHelp(),
            "show_roles" => HandleShowRoles(),
            "show_reports" => HandleShowReports(),
            _ => HandleUnknown()
        };
    }

    private async Task<string> HandleCreateSaleAsync(Guid businessId, JsonElement ba, User? recordedBy = null)
    {
        var sellAll = ba.GetStringOrNull("sellAll") == "true";
        var sellHalf = ba.GetStringOrNull("sellHalf") == "true";
        var sellEachQty = ba.GetDecimalOrNull("sellEachQty");
        var sellAllProduct = ba.GetStringOrNull("sellAllProduct");
        var sellHalfProduct = ba.GetStringOrNull("sellHalfProduct");
        var sellCategory = ba.GetStringOrNull("sellCategory");
        var discountPercent = ba.GetDecimalOrNull("discountPercent");
        var topLevelUnitPrice = ba.GetDecimalOrNull("unitPrice");
        var excludeProducts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ba.TryGetProperty("excludeProducts", out var exEl) && exEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var ex in exEl.EnumerateArray())
            {
                var name = ex.GetString();
                if (!string.IsNullOrEmpty(name)) excludeProducts.Add(name);
            }
        }

        var saleItems = new List<SaleItemRequest>();
        var skipped = new List<string>();

        if (!string.IsNullOrEmpty(sellAllProduct))
        {
            var (product, err) = await FindProductAsync(businessId, sellAllProduct);
            if (product == null) return err!;
            if (product.CurrentStock <= 0) return $"{product.Name} has no stock to sell.";
            var price = topLevelUnitPrice ?? product.SellingPrice ?? 0;
            if (price <= 0) return $"No selling price set for {product.Name}. Set a price first.";
            if (discountPercent.HasValue) price = price * (1 - discountPercent.Value / 100);
            saleItems.Add(new SaleItemRequest { ProductId = product.Id, Quantity = product.CurrentStock, UnitPrice = price });
        }
        else if (!string.IsNullOrEmpty(sellHalfProduct))
        {
            // "Sell half my stock of shampoo" — server computes half from live DB stock instead of
            // trusting Claude to do the arithmetic from context (which failed silently for edge cases
            // like low stock). Round up so "half of 3" = 2, never 0 or 1.5; users almost certainly
            // mean "most of them" rather than losing a unit to rounding.
            var (product, err) = await FindProductAsync(businessId, sellHalfProduct);
            if (product == null) return err!;
            if (product.CurrentStock <= 0) return $"{product.Name} has no stock to sell.";
            var halfQty = Math.Max(1, Math.Ceiling(product.CurrentStock / 2m));
            var price = topLevelUnitPrice ?? product.SellingPrice ?? 0;
            if (price <= 0) return $"No selling price is set for {product.Name}. Please say the price, e.g. \"Sell half my stock of {product.Name} at 5000\"";
            if (discountPercent.HasValue) price = price * (1 - discountPercent.Value / 100);
            saleItems.Add(new SaleItemRequest { ProductId = product.Id, Quantity = halfQty, UnitPrice = price });
        }
        else if (sellAll || sellEachQty.HasValue)
        {
            var query = _db.Products
                .Where(p => p.BusinessId == businessId && p.IsActive && p.CurrentStock > 0);

            if (!string.IsNullOrEmpty(sellCategory))
                query = query.Where(p => p.Category != null && p.Category.ToLower().Contains(sellCategory.ToLower()));

            var products = await query.ToListAsync();

            foreach (var product in products)
            {
                if (excludeProducts.Any(ex => product.Name.ToLower().Contains(ex.ToLower()))) continue;

                var qty = sellAll ? product.CurrentStock : sellEachQty!.Value;
                if (sellHalf) qty = Math.Floor(product.CurrentStock / 2);
                if (qty <= 0) continue;
                if (product.CurrentStock < qty) { skipped.Add($"{product.Name} (only {product.CurrentStock:0.##} {product.Unit})"); continue; }
                var price = product.SellingPrice ?? 0;
                if (price <= 0) { skipped.Add($"{product.Name} (no selling price)"); continue; }
                if (discountPercent.HasValue) price = price * (1 - discountPercent.Value / 100);

                saleItems.Add(new SaleItemRequest { ProductId = product.Id, Quantity = qty, UnitPrice = price });
            }
        }
        else
        {
            if (!ba.TryGetProperty("items", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Array || itemsEl.GetArrayLength() == 0)
                return "I couldn't identify the items. Please list what you sold and how many.";

            var isBatch = itemsEl.GetArrayLength() > 1;

            foreach (var item in itemsEl.EnumerateArray())
            {
                var productName = item.GetStringOrNull("productName");
                if (string.IsNullOrEmpty(productName)) continue;

                var qty = item.GetDecimalOrNull("quantity") ?? 0;
                var unitPrice = item.GetDecimalOrNull("unitPrice");
                var itemTotalAmount = item.GetDecimalOrNull("totalAmount");
                var (product, error) = await FindProductAsync(businessId, productName);

                if (product == null) { if (isBatch) { skipped.Add($"{productName} (not found)"); continue; } return $"*{productName}* isn't in your inventory yet. Add it first:\n\"Bought {qty:0.##} {item.GetStringOrNull("unit") ?? UnitInferrer.Infer(productName)} of {productName} at [cost]\"\n\nThen record the sale."; }

                // Reverse calculation: "sell rice worth 5k" — derive qty from total amount
                if (qty <= 0 && itemTotalAmount.HasValue && itemTotalAmount.Value > 0)
                {
                    var price = product.SellingPrice ?? 0;
                    if (price <= 0) { if (isBatch) { skipped.Add($"{product.Name} (no price for calc)"); continue; } return $"Can't calculate quantity — no selling price set for {product.Name}."; }
                    qty = Math.Floor(itemTotalAmount.Value / price);
                    unitPrice = price;
                    if (qty <= 0) { if (isBatch) { skipped.Add($"{product.Name} (amount too low)"); continue; } return $"{_cs}{itemTotalAmount.Value:N0} isn't enough for 1 {product.Unit} of {product.Name} ({_cs}{price:N0} each)."; }
                }

                if (qty <= 0) { if (isBatch) { skipped.Add($"{productName} (invalid qty)"); continue; } return $"Please provide a valid quantity for {productName}."; }
                if (product.CurrentStock < qty) { if (isBatch) { skipped.Add($"{product.Name} (only {product.CurrentStock:0.##} {product.Unit})"); continue; } return $"❌ Not enough stock for {product.Name}. You have {product.CurrentStock} {product.Unit} available, but need {qty}."; }

                var finalPrice = unitPrice ?? product.SellingPrice ?? 0;
                if (finalPrice <= 0) { if (isBatch) { skipped.Add($"{product.Name} (no price)"); continue; } return $"No selling price is set for {product.Name}. Please say the price, e.g. \"I sold {qty} {product.Unit} of {product.Name} at 5000\""; }
                if (discountPercent.HasValue && unitPrice == null) finalPrice = finalPrice * (1 - discountPercent.Value / 100);

                saleItems.Add(new SaleItemRequest { ProductId = product.Id, Quantity = qty, UnitPrice = finalPrice });
            }
        }

        if (saleItems.Count == 0)
        {
            var reason = skipped.Count > 0 ? $"All products were skipped:\n{string.Join("\n", skipped.Select(s => $"• {s}"))}" : "I couldn't match any products. Check the product name and try again.";
            return $"❌ No items could be sold. {reason}";
        }

        Guid? contactId = null;
        var contactName = ba.GetStringOrNull("contactName");
        if (!string.IsNullOrEmpty(contactName))
        {
            var contact = await FindOrCreateContactAsync(businessId, contactName, ContactType.Customer);
            contactId = contact.Id;
        }

        var paymentStatusStr = ba.GetStringOrNull("paymentStatus") ?? "Paid";
        var paymentStatus = Enum.TryParse<PaymentStatus>(paymentStatusStr, true, out var ps) ? ps : PaymentStatus.Paid;

        // SalesService.CreateAsync opens its own transaction and handles atomicity internally
        // (sale items, stock decrements, inventory transactions all commit together or roll back together).
        // An outer transaction here would nest on the same connection — Npgsql doesn't support nested
        // transactions, which surfaces as "The connection is already in a transaction" to end users.
        var sale = await _sales.CreateAsync(businessId, new CreateSaleRequest
        {
            Items = saleItems,
            ContactId = contactId,
            PaymentStatus = paymentStatus,
            PaymentMethod = ba.GetStringOrNull("paymentMethod")
        }, EntrySource.WhatsApp, recordedBy?.Id, recordedBy?.FullName);

        var lines = sale.Items.Select(i => $"• {i.Quantity} {i.Unit} of {i.ProductName} @ {_cs}{i.UnitPrice:N0} = {_cs}{i.TotalPrice:N0}");
        var debtNote = "";

        // Auto-create receivable for credit sales
        if (paymentStatus != PaymentStatus.Paid && contactId.HasValue && sale.TotalAmount > 0)
        {
            await _ledger.CreateReceivableAsync(businessId, new DTOs.Ledger.CreateReceivableRequest
            {
                ContactId = contactId.Value,
                Amount = sale.TotalAmount,
                Notes = $"Credit sale"
            }, EntrySource.WhatsApp, recordedBy?.Id, recordedBy?.FullName);
            var cName = ba.GetStringOrNull("contactName") ?? "Customer";
            debtNote = $"\n💰 {cName} now owes you {_cs}{sale.TotalAmount:N0}";
        }

        var skippedNote = skipped.Count > 0
            ? $"\n\n⚠️ Skipped {skipped.Count} item{(skipped.Count != 1 ? "s" : "")}:\n{string.Join("\n", skipped.Select(s => $"• {s}"))}"
            : "";

        return $"✅ Sale recorded!\n{string.Join("\n", lines)}\n\n*Total: {_cs}{sale.TotalAmount:N0}* ({sale.PaymentStatus}){debtNote}{skippedNote}";
    }

    private async Task<string> HandleCreateExpenseAsync(Guid businessId, JsonElement ba, User? recordedBy = null)
    {
        var amount = ba.GetDecimalOrNull("amount");
        if (!amount.HasValue || amount.Value <= 0)
            return "I couldn't identify the amount. Please include how much was spent.";

        var expense = await _expenses.CreateAsync(businessId, new DTOs.Expenses.CreateExpenseRequest
        {
            Category = ba.GetStringOrNull("category") ?? "General",
            Amount = amount.Value,
            Notes = ba.GetStringOrNull("notes"),
            PaidTo = ba.GetStringOrNull("paidTo")
        }, EntrySource.WhatsApp, recordedBy?.Id, recordedBy?.FullName);

        return $"✅ Expense recorded: {_cs}{expense.Amount:N0} for {expense.Category}" +
               (expense.PaidTo != null ? $" (paid to {expense.PaidTo})" : "");
    }

    private async Task<string> HandleAddInventoryAsync(Guid businessId, JsonElement ba, User? recordedBy = null)
    {
        // Check for multi-product items[] array first
        if (ba.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array && itemsEl.GetArrayLength() > 0)
        {
            return await HandleMultiAddInventoryAsync(businessId, itemsEl, ba, recordedBy);
        }

        // Single product flow (backward compatible)
        var productName = ba.GetStringOrNull("productName");
        var qty = ba.GetDecimalOrNull("quantity");
        if (string.IsNullOrEmpty(productName) || !qty.HasValue)
            return "Please specify the product name and quantity to add.";
        if (qty.Value <= 0) return "Quantity must be greater than zero.";

        return await AddSingleInventoryItemAsync(businessId, productName, qty.Value,
            ba.GetStringOrNull("unit"), ba.GetDecimalOrNull("unitCost"), ba.GetDecimalOrNull("sellingPrice"),
            ba.GetStringOrNull("category"), ba.GetStringOrNull("subcategory"), ba.GetStringOrNull("notes"), ba.GetStringOrNull("paidTo"),
            ba.GetStringOrNull("payLater"), ba.GetStringOrNull("supplierName"), recordedBy);
    }

    private async Task<string> HandleMultiAddInventoryAsync(Guid businessId, JsonElement itemsEl, JsonElement ba, User? recordedBy = null)
    {
        var results = new List<string>();
        var created = new List<string>();
        var failed = new List<string>();
        decimal totalExpense = 0;

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            foreach (var item in itemsEl.EnumerateArray())
            {
                var productName = item.GetStringOrNull("productName");
                var qty = item.GetDecimalOrNull("quantity");

                if (string.IsNullOrEmpty(productName) || !qty.HasValue || qty.Value <= 0)
                {
                    if (!string.IsNullOrEmpty(productName))
                        failed.Add($"{productName} (missing quantity)");
                    continue;
                }

                try
                {
                    var unit = item.GetStringOrNull("unit") ?? UnitInferrer.Infer(productName);
                    var unitCost = item.GetDecimalOrNull("unitCost");
                    var sellPrice = item.GetDecimalOrNull("sellingPrice");
                    var category = item.GetStringOrNull("category") ?? ba.GetStringOrNull("category");
                    var subcategory = item.GetStringOrNull("subcategory") ?? ba.GetStringOrNull("subcategory");

                    var (product, _) = await FindProductAsync(businessId, productName);
                    var isNew = false;

                    if (product == null)
                    {
                        product = new Product
                        {
                            BusinessId = businessId,
                            Name = productName,
                            Unit = unit,
                            CostPrice = unitCost,
                            SellingPrice = sellPrice,
                            CurrentStock = 0,
                            LowStockThreshold = 5,
                            Category = category,
                            Subcategory = subcategory,
                            Source = EntrySource.WhatsApp,
                            RecordedByUserId = recordedBy?.Id,
                            RecordedByName = recordedBy?.FullName
                        };
                        _db.Products.Add(product);
                        await _db.SaveChangesAsync();
                        isNew = true;
                        created.Add(product.Name);
                    }
                    else if (sellPrice.HasValue && sellPrice.Value > 0 && product.SellingPrice != sellPrice.Value)
                    {
                        product.SellingPrice = sellPrice.Value;
                    }

                    var effectiveCost = unitCost ?? product.CostPrice;

                    var stockBefore = product.CurrentStock;
                    await _inventory.StockInAsync(businessId, new StockInRequest
                    {
                        ProductId = product.Id,
                        Quantity = qty.Value,
                        UnitCost = effectiveCost,
                        Notes = $"Bulk restock"
                    }, recordedBy?.Id, recordedBy?.FullName);

                    if (effectiveCost.HasValue && effectiveCost.Value > 0)
                    {
                        var purchaseTotal = qty.Value * effectiveCost.Value;
                        totalExpense += purchaseTotal;
                        await _expenses.CreateAsync(businessId, new DTOs.Expenses.CreateExpenseRequest
                        {
                            Category = "Inventory",
                            Amount = purchaseTotal,
                            Notes = $"Bought {qty.Value:0.##} {product.Unit} of {product.Name} @ {_cs}{effectiveCost.Value:N0}",
                        }, EntrySource.WhatsApp, recordedBy?.Id, recordedBy?.FullName);
                    }

                    var newTag = isNew ? " 🆕" : "";
                    results.Add($"• {product.Name}: +{qty.Value:0.##} {product.Unit} (stock: {(stockBefore + qty.Value):0.##}){newTag}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Multi-add inventory failed for {Product}", productName);
                    failed.Add($"{productName} ({FriendlyErrorMessage(ex)})");
                }
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        if (results.Count == 0)
            return "Couldn't process any items. Please list each product with a quantity, e.g. \"30 bags rice, 40 boxes juice\"";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"✅ Restocked {results.Count} product{(results.Count != 1 ? "s" : "")}:");
        foreach (var r in results) sb.AppendLine(r);

        if (created.Count > 0)
            sb.AppendLine($"\n🆕 New: {string.Join(", ", created)}");
        if (totalExpense > 0)
            sb.AppendLine($"💸 Total expense: {_cs}{totalExpense:N0}");
        if (failed.Count > 0)
            sb.AppendLine($"\n⚠️ Skipped: {string.Join(", ", failed)}");

        return sb.ToString().TrimEnd();
    }

    private async Task<string> AddSingleInventoryItemAsync(Guid businessId, string productName, decimal qty,
        string? unit, decimal? unitCost, decimal? sellPrice, string? category, string? subcategory, string? notes, string? paidTo,
        string? payLater = null, string? supplierName = null, User? recordedBy = null)
    {
        var (product, error) = await FindProductAsync(businessId, productName);
        var autoCreated = false;

        if (product == null)
        {
            product = new Product
            {
                BusinessId = businessId,
                Name = productName,
                Unit = unit ?? UnitInferrer.Infer(productName),
                CostPrice = unitCost,
                SellingPrice = sellPrice,
                CurrentStock = 0,
                LowStockThreshold = 5,
                Category = category,
                Subcategory = subcategory,
                Source = EntrySource.WhatsApp,
                RecordedByUserId = recordedBy?.Id,
                RecordedByName = recordedBy?.FullName
            };
            _db.Products.Add(product);
            await _db.SaveChangesAsync();
            autoCreated = true;
        }

        if (sellPrice.HasValue && sellPrice.Value > 0 && product.SellingPrice != sellPrice.Value)
        {
            product.SellingPrice = sellPrice.Value;
            await _db.SaveChangesAsync();
        }

        var effectiveCost = unitCost ?? product.CostPrice;

        var stockBefore = product.CurrentStock;
        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            await _inventory.StockInAsync(businessId, new StockInRequest
            {
                ProductId = product.Id,
                Quantity = qty,
                UnitCost = effectiveCost,
                Notes = notes
            }, recordedBy?.Id, recordedBy?.FullName);

            if (effectiveCost.HasValue && effectiveCost.Value > 0)
            {
                var purchaseTotal = qty * effectiveCost.Value;
                await _expenses.CreateAsync(businessId, new DTOs.Expenses.CreateExpenseRequest
                {
                    Category = "Inventory",
                    Amount = purchaseTotal,
                    Notes = $"Bought {qty:0.##} {product.Unit} of {product.Name} @ {_cs}{effectiveCost.Value:N0}/{product.Unit}",
                    PaidTo = paidTo
                }, EntrySource.WhatsApp, recordedBy?.Id, recordedBy?.FullName);
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        var createdNote = autoCreated ? $"\n🆕 Created new product: {product.Name} (unit: {product.Unit})" : "";
        var costNote = effectiveCost.HasValue && effectiveCost.Value > 0
            ? $"\n💸 Recorded as expense: {_cs}{(qty * effectiveCost.Value):N0}" + (unitCost == null ? " (using stored cost price)" : "")
            : "";

        // Auto-create payable if "pay later"
        var debtNote = "";
        if (payLater == "true")
        {
            var payLaterCost = unitCost ?? product.CostPrice;
            var totalOwed = payLaterCost.HasValue ? qty * payLaterCost.Value : 0;
            var sName = supplierName ?? "Supplier";
            var contact = await FindOrCreateContactAsync(businessId, sName, ContactType.Supplier);

            await _ledger.CreatePayableAsync(businessId, new DTOs.Ledger.CreatePayableRequest
            {
                ContactId = contact.Id,
                Amount = totalOwed,
                Notes = totalOwed > 0 ? $"Credit purchase: {qty:0.##} {product.Unit} of {product.Name} @ {_cs}{payLaterCost!.Value:N0}" : $"PAY_LATER:{product.Name}:{qty:0.##}"
            }, EntrySource.WhatsApp, recordedBy?.Id, recordedBy?.FullName);

            if (totalOwed > 0)
                debtNote = $"\n💳 You owe *{sName}* {_cs}{totalOwed:N0}" + (unitCost == null ? " (using stored cost price)" : "");
            else
                debtNote = $"\n💳 Pay-later from *{sName}* — set a cost price to update the amount owed";
        }

        return $"✅ Added {qty:0.##} {product.Unit} of {product.Name}.{createdNote}\nNew stock: {(stockBefore + qty):0.##} {product.Unit}{costNote}{debtNote}";
    }

    private async Task<string> HandleRemoveInventoryAsync(Guid businessId, JsonElement ba, User? recordedBy = null)
    {
        // Batch zero-all
        var zeroAll = ba.GetStringOrNull("zeroAll");
        if (zeroAll == "true")
        {
            var allProducts = await _db.Products
                .Where(p => p.BusinessId == businessId && p.IsActive && p.CurrentStock > 0)
                .ToListAsync();
            if (allProducts.Count == 0) return "All products already have 0 stock.";

            foreach (var p in allProducts)
            {
                await _inventory.StockOutAsync(businessId, new StockOutRequest
                {
                    ProductId = p.Id, Quantity = p.CurrentStock, Notes = "Batch zero-out"
                }, recordedBy?.Id, recordedBy?.FullName);
            }
            return $"✅ Cleared stock for {allProducts.Count} products: {string.Join(", ", allProducts.Select(p => p.Name))}.";
        }

        // Multi-product zero-out via items[]
        if (ba.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array && itemsEl.GetArrayLength() > 0)
        {
            var results = new List<string>();
            var skipped = new List<string>();

            foreach (var item in itemsEl.EnumerateArray())
            {
                var name = item.GetStringOrNull("productName");
                if (string.IsNullOrEmpty(name)) continue;

                var (product, _) = await FindProductAsync(businessId, name);
                if (product == null) { skipped.Add($"{name} (not found)"); continue; }
                if (product.CurrentStock <= 0) { skipped.Add($"{name} (already 0)"); continue; }

                var removed = product.CurrentStock;
                await _inventory.StockOutAsync(businessId, new StockOutRequest
                {
                    ProductId = product.Id, Quantity = removed, Notes = "Batch zero-out"
                }, recordedBy?.Id, recordedBy?.FullName);
                results.Add($"• {product.Name}: {removed:0.##} {product.Unit} removed → 0");
            }

            if (results.Count == 0 && skipped.Count > 0)
                return $"Nothing to remove: {string.Join(", ", skipped)}";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"✅ Cleared stock for {results.Count} product{(results.Count != 1 ? "s" : "")}:");
            foreach (var r in results) sb.AppendLine(r);
            if (skipped.Count > 0) sb.AppendLine($"\n⚠️ Skipped: {string.Join(", ", skipped)}");
            return sb.ToString().TrimEnd();
        }

        // Single product flow
        var productName = ba.GetStringOrNull("productName");
        var qty = ba.GetDecimalOrNull("quantity");
        var zeroOut = ba.GetStringOrNull("zeroOut");

        if (string.IsNullOrEmpty(productName))
            return "Please specify the product name.";

        var (prod, error) = await FindProductAsync(businessId, productName);
        if (prod == null) return error!;

        if (zeroOut == "true" || (qty.HasValue && qty.Value >= 999999))
        {
            qty = prod.CurrentStock;
            if (qty <= 0) return $"{prod.Name} already has 0 stock.";
        }

        if (!qty.HasValue || qty.Value <= 0)
            return "Please specify the quantity to remove.";

        if (prod.CurrentStock < qty.Value)
            return $"❌ Can't remove {qty.Value} {prod.Unit} of {prod.Name} — only {prod.CurrentStock:0.##} in stock.";

        var stockBefore = prod.CurrentStock;
        await _inventory.StockOutAsync(businessId, new StockOutRequest
        {
            ProductId = prod.Id, Quantity = qty.Value, Notes = ba.GetStringOrNull("notes")
        }, recordedBy?.Id, recordedBy?.FullName);

        return $"✅ Removed {qty.Value:0.##} {prod.Unit} of {prod.Name}.\nRemaining: {(stockBefore - qty.Value):0.##} {prod.Unit}";
    }

    private async Task<string> HandleMarkDamagedAsync(Guid businessId, JsonElement ba, User? recordedBy = null)
    {
        var productName = ba.GetStringOrNull("productName");
        var qty = ba.GetDecimalOrNull("quantity");
        if (string.IsNullOrEmpty(productName) || !qty.HasValue)
            return "Please specify the product and damaged quantity.";
        if (qty.Value <= 0) return "Quantity must be greater than zero.";

        var (product, error) = await FindProductAsync(businessId, productName);
        if (product == null) return error!;

        if (product.CurrentStock < qty.Value)
            return $"❌ Can't mark {qty.Value} {product.Unit} as damaged — only {product.CurrentStock} {product.Unit} of {product.Name} in stock.";

        var stockBefore = product.CurrentStock;
        await _inventory.MarkDamagedAsync(businessId, new DamagedRequest
        {
            ProductId = product.Id, Quantity = qty.Value, Notes = ba.GetStringOrNull("notes")
        }, recordedBy?.Id, recordedBy?.FullName);

        return $"✅ {qty.Value:0.##} {product.Unit} of {product.Name} marked as damaged.\nRemaining: {(stockBefore - qty.Value):0.##} {product.Unit}";
    }

    private async Task<string> HandleCreateReceivableAsync(Guid businessId, JsonElement ba, User? recordedBy = null)
    {
        // Plan gate — ledger is not available on Starter tier
        var (allowed, planErr) = await _planGuard.CheckFeatureAsync(businessId, "ledger");
        if (!allowed) return $"⚠️ {planErr}";

        var contactName = ba.GetStringOrNull("contactName");
        var amount = ba.GetDecimalOrNull("amount");
        if (string.IsNullOrEmpty(contactName) || !amount.HasValue)
            return "Please specify who owes you and how much.";

        var contact = await FindOrCreateContactAsync(businessId, contactName, ContactType.Customer);
        await _ledger.CreateReceivableAsync(businessId, new CreateReceivableRequest
        {
            ContactId = contact.Id, Amount = amount.Value, Notes = ba.GetStringOrNull("notes")
        }, EntrySource.WhatsApp, recordedBy?.Id, recordedBy?.FullName);

        return $"✅ Recorded: {contactName} owes you {_cs}{amount.Value:N0}";
    }

    private async Task<string> HandleCreatePayableAsync(Guid businessId, JsonElement ba, User? recordedBy = null)
    {
        // Plan gate — ledger is not available on Starter tier
        var (allowed, planErr) = await _planGuard.CheckFeatureAsync(businessId, "ledger");
        if (!allowed) return $"⚠️ {planErr}";

        var contactName = ba.GetStringOrNull("contactName");
        var amount = ba.GetDecimalOrNull("amount");
        if (string.IsNullOrEmpty(contactName) || !amount.HasValue)
            return "Please specify who you owe and how much.";

        var contact = await FindOrCreateContactAsync(businessId, contactName, ContactType.Supplier);
        await _ledger.CreatePayableAsync(businessId, new CreatePayableRequest
        {
            ContactId = contact.Id, Amount = amount.Value, Notes = ba.GetStringOrNull("notes")
        }, EntrySource.WhatsApp, recordedBy?.Id, recordedBy?.FullName);

        return $"✅ Recorded: You owe {contactName} {_cs}{amount.Value:N0}";
    }

    private async Task<string> HandleRecordPaymentAsync(Guid businessId, JsonElement ba, string type, User? recordedBy = null)
    {
        // Plan gate — ledger payments require the ledger feature
        var (allowed, planErr) = await _planGuard.CheckFeatureAsync(businessId, "ledger");
        if (!allowed) return $"⚠️ {planErr}";

        var contactName = ba.GetStringOrNull("contactName");
        var amount = ba.GetDecimalOrNull("amount");
        var clearAll = ba.GetStringOrNull("clearAll");
        var clearAllDebts = ba.GetStringOrNull("clearAllDebts");

        var receivableType = type == "receivable" ? LedgerEntryType.Receivable : LedgerEntryType.Payable;
        var paymentType = type == "receivable" ? LedgerEntryType.ReceivablePayment : LedgerEntryType.PayablePayment;

        // Batch clear ALL debts
        if (clearAllDebts == "true")
        {
            var allEntries = await _db.LedgerEntries
                .Include(e => e.Contact)
                .Where(e => e.BusinessId == businessId)
                .ToListAsync();

            var byContact = allEntries
                .GroupBy(e => e.Contact)
                .Select(g => new
                {
                    Contact = g.Key,
                    Outstanding = g.Where(e => e.EntryType == receivableType).Sum(e => e.Amount)
                                - g.Where(e => e.EntryType == paymentType).Sum(e => e.Amount)
                })
                .Where(c => c.Outstanding > 0)
                .ToList();

            if (byContact.Count == 0) return $"No outstanding {type} balances to clear.";

            var cleared = new List<string>();
            foreach (var c in byContact)
            {
                await _ledger.RecordPaymentAsync(businessId, new RecordPaymentRequest
                {
                    ContactId = c.Contact.Id, Amount = c.Outstanding, PaymentType = type
                }, EntrySource.WhatsApp, recordedBy?.Id, recordedBy?.FullName);
                cleared.Add($"• {c.Contact.Name}: {_cs}{c.Outstanding:N0}");
            }

            return $"✅ Cleared {cleared.Count} {type} balance{(cleared.Count != 1 ? "s" : "")}:\n{string.Join("\n", cleared)}\n\nAll balances now {_cs}0.";
        }

        // Single contact — auto-lookup balance if clearAll or no amount
        if (!string.IsNullOrEmpty(contactName))
        {
            var contact = await _db.Contacts.FirstOrDefaultAsync(c =>
                c.BusinessId == businessId && c.Name.ToLower().Contains(contactName.ToLower()));
            if (contact == null) return $"Contact '{contactName}' not found. Check the name and try again.";

            var entries = await _db.LedgerEntries.Where(e => e.ContactId == contact.Id && e.BusinessId == businessId).ToListAsync();
            var outstanding = entries.Where(e => e.EntryType == receivableType).Sum(e => e.Amount)
                            - entries.Where(e => e.EntryType == paymentType).Sum(e => e.Amount);

            if (outstanding <= 0) return $"{contact.Name} has no outstanding {type} balance.";

            // Auto-clear full balance
            if (clearAll == "true" || !amount.HasValue)
            {
                amount = outstanding;
            }

            if (amount.Value <= 0) return "Payment amount must be greater than zero.";
            if (amount.Value > outstanding)
                return $"⚠️ Payment of {_cs}{amount.Value:N0} exceeds the outstanding balance of {_cs}{outstanding:N0} for {contact.Name}. Please confirm the correct amount.";

            var direction = type == "receivable" ? $"{contact.Name} paid you" : $"You paid {contact.Name}";

            await _ledger.RecordPaymentAsync(businessId, new RecordPaymentRequest
            {
                ContactId = contact.Id, Amount = amount.Value, PaymentType = type
            }, EntrySource.WhatsApp, recordedBy?.Id, recordedBy?.FullName);

            var remaining = outstanding - amount.Value;
            var suffix = remaining > 0 ? $"\nRemaining balance: {_cs}{remaining:N0}" : "\nBalance fully cleared ✅";
            return $"✅ Payment recorded: {direction} {_cs}{amount.Value:N0}{suffix}";
        }

        return "Please specify who paid or whose debt to clear. E.g. \"Sara paid 20k\" or \"Clear Sara's debt\".";
    }

    private async Task<string> HandleCreateProductAsync(Guid businessId, JsonElement ba, User? recordedBy = null)
    {
        var name = ba.GetStringOrNull("name");
        var unit = ba.GetStringOrNull("unit");
        if (string.IsNullOrEmpty(name))
            return "Please provide the product name.";
        if (string.IsNullOrEmpty(unit))
            unit = UnitInferrer.Infer(name);

        var exists = await _db.Products.AnyAsync(p => p.BusinessId == businessId && p.IsActive && p.Name.ToLower() == name.ToLower());
        if (exists) return $"'{name}' already exists in your products.";

        var sellingPrice = ba.GetDecimalOrNull("sellingPrice");
        var costPrice = ba.GetDecimalOrNull("costPrice");
        var initialStock = ba.GetDecimalOrNull("initialStock") ?? 0;

        var product = new Product
        {
            BusinessId = businessId,
            Name = name,
            Unit = unit,
            SellingPrice = sellingPrice,
            CostPrice = costPrice,
            CurrentStock = initialStock,
            LowStockThreshold = 5,
            Category = ba.GetStringOrNull("category"),
            Subcategory = ba.GetStringOrNull("subcategory"),
            Source = EntrySource.WhatsApp,
            RecordedByUserId = recordedBy?.Id,
            RecordedByName = recordedBy?.FullName
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        var priceInfo = sellingPrice.HasValue ? $" at {_cs}{sellingPrice.Value:N0}/{unit}" : "";
        var stockInfo = initialStock > 0 ? $"\nOpening stock: {initialStock} {unit}" : "";
        return $"✅ Product created: *{name}*{priceInfo}{stockInfo}\n\nYou can now record sales and restock via WhatsApp.";
    }

    private async Task<string> HandleUpdateProductPriceAsync(Guid businessId, JsonElement ba, User? recordedBy = null)
    {
        var productName = ba.GetStringOrNull("productName");
        if (string.IsNullOrEmpty(productName)) return "Please specify the product name.";

        var (product, error) = await FindProductAsync(businessId, productName);
        if (product == null) return error!;

        var sellingPrice = ba.GetDecimalOrNull("sellingPrice");
        var costPrice = ba.GetDecimalOrNull("costPrice");
        var sellingPriceChange = ba.GetDecimalOrNull("sellingPriceChange");
        var costPriceChange = ba.GetDecimalOrNull("costPriceChange");

        if (sellingPriceChange.HasValue && product.SellingPrice.HasValue)
            sellingPrice = product.SellingPrice.Value + sellingPriceChange.Value;
        if (costPriceChange.HasValue && product.CostPrice.HasValue)
            costPrice = product.CostPrice.Value + costPriceChange.Value;

        if (!sellingPrice.HasValue && !costPrice.HasValue)
            return "Please specify the new selling price or cost price.";

        if (sellingPrice.HasValue) product.SellingPrice = Math.Max(0, sellingPrice.Value);
        if (costPrice.HasValue) product.CostPrice = Math.Max(0, costPrice.Value);
        await _db.SaveChangesAsync();

        var parts = new List<string>();
        if (sellingPrice.HasValue) parts.Add($"selling price → {_cs}{sellingPrice.Value:N0}");
        if (costPrice.HasValue) parts.Add($"cost price → {_cs}{costPrice.Value:N0}");

        // Check for {_cs}0 pay-later payables that need updating
        var payLaterNote = "";
        if (costPrice.HasValue && costPrice.Value > 0)
        {
            var zeroPaylaters = await _db.LedgerEntries
                .Where(e => e.BusinessId == businessId
                    && e.EntryType == LedgerEntryType.Payable
                    && e.Amount == 0
                    && e.Notes != null && e.Notes.StartsWith($"PAY_LATER:{product.Name}:"))
                .ToListAsync();

            foreach (var entry in zeroPaylaters)
            {
                // Parse qty from notes: "PAY_LATER:beans:10"
                var noteParts = entry.Notes!.Split(':');
                if (noteParts.Length >= 3 && decimal.TryParse(noteParts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var qty))
                {
                    entry.Amount = qty * costPrice.Value;
                    entry.Notes = $"Credit purchase: {qty:0.##} {product.Unit} of {product.Name} @ {_cs}{costPrice.Value:N0}";
                    payLaterNote += $"\n💳 Updated payable: {_cs}{entry.Amount:N0} owed";
                }
            }
            if (zeroPaylaters.Count > 0) await _db.SaveChangesAsync();
        }

        return $"✅ {product.Name} updated: {string.Join(", ", parts)}{payLaterNote}";
    }

    private async Task<string> HandleGetDailySummaryAsync(Guid businessId)
    {
        var summary = await _reports.GetDailySummaryAsync(businessId, null);
        var net = summary.TotalSales - summary.TotalExpenses;
        var netEmoji = net >= 0 ? "📈" : "📉";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"📊 *Daily Summary — {TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz):MMM d, yyyy}*");
        sb.AppendLine($"🛒 Sales: {_cs}{summary.TotalSales:N0} ({summary.SaleCount} transactions)");
        sb.AppendLine($"💸 Expenses: {_cs}{summary.TotalExpenses:N0}");
        sb.AppendLine($"{netEmoji} Net: {_cs}{net:N0}");

        // Low stock
        if (summary.LowStockCount > 0)
            sb.AppendLine($"⚠️ {summary.LowStockCount} product{(summary.LowStockCount != 1 ? "s" : "")} running low");

        // Outstanding receivables
        if (summary.OutstandingReceivables > 0)
            sb.AppendLine($"💰 Outstanding receivables: {_cs}{summary.OutstandingReceivables:N0}");

        // Outstanding payables
        if (summary.OutstandingPayables > 0)
            sb.AppendLine($"💳 Outstanding payables: {_cs}{summary.OutstandingPayables:N0}");

        // No sales warning
        if (summary.SaleCount == 0)
            sb.AppendLine("🔕 No sales recorded today.");

        // Top sold product today
        var todayUtc = DateTime.UtcNow.Date;
        var topProduct = await _db.SaleItems
            .Include(i => i.Sale).Include(i => i.Product)
            .Where(i => i.Sale.BusinessId == businessId && i.Sale.CreatedAtUtc >= todayUtc)
            .GroupBy(i => i.Product.Name)
            .Select(g => new { Name = g.Key, Rev = g.Sum(i => i.TotalPrice) })
            .OrderByDescending(p => p.Rev)
            .FirstOrDefaultAsync();

        if (topProduct != null)
            sb.AppendLine($"🏆 Top today: {topProduct.Name} ({_cs}{topProduct.Rev:N0})");

        return sb.ToString().TrimEnd();
    }

    private async Task<string> HandleGetTodaySalesAsync(Guid businessId)
    {
        var s = await _reports.GetDailySummaryAsync(businessId, null);
        return $"📊 *Today's Sales*\nRevenue: {_cs}{s.TotalSales:N0} ({s.SaleCount} transactions)\nExpenses: {_cs}{s.TotalExpenses:N0}\nNet: {_cs}{s.NetCashIn:N0}";
    }

    private async Task<string> HandleGetWeekSalesAsync(Guid businessId)
    {
        var s = await _reports.GetWeeklySummaryAsync(businessId, null);
        var top = s.TopProducts.Count > 0 ? $"\n🏆 Top: {s.TopProducts[0].ProductName} ({_cs}{s.TopProducts[0].TotalRevenue:N0})" : "";
        return $"📊 *This Week ({s.WeekStart} – {s.WeekEnd})*\nSales: {_cs}{s.TotalSales:N0}\nExpenses: {_cs}{s.TotalExpenses:N0}\nEst. Profit: {_cs}{s.EstimatedProfit:N0}" + top;
    }

    private async Task<string> HandleGetAllStockAsync(Guid businessId, JsonElement ba)
    {
        var showPrices = ba.GetStringOrNull("showPrices") == "true";

        var items = await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive)
            .OrderBy(p => p.Name).ToListAsync();

        if (items.Count == 0) return "You have no products set up yet.";

        // Get held quantities for all products in one query
        var activeHolds = await _db.StockHolds
            .Where(h => h.BusinessId == businessId && h.Status == HoldStatus.Active)
            .GroupBy(h => h.ProductId)
            .Select(g => new { ProductId = g.Key, HeldQty = g.Sum(h => h.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.HeldQty);

        var lines = items.Select(p =>
        {
            var held = activeHolds.GetValueOrDefault(p.Id, 0);
            var flag = p.CurrentStock <= p.LowStockThreshold ? " ⚠️" : "";
            var holdStr = held > 0 ? $" ({held:0.##} on hold, {(p.CurrentStock - held):0.##} avail)" : "";
            var priceStr = "";
            if (showPrices)
            {
                var prices = new List<string>();
                if (p.SellingPrice.HasValue) prices.Add($"Sell: {_cs}{p.SellingPrice.Value:N0}");
                if (p.CostPrice.HasValue) prices.Add($"Cost: {_cs}{p.CostPrice.Value:N0}");
                if (prices.Count > 0) priceStr = $" — {string.Join(" | ", prices)}";
            }
            return $"• {p.Name}: {p.CurrentStock:0.##} {p.Unit}{holdStr}{flag}{priceStr}";
        });
        return $"📦 *Stock Levels*\n{string.Join("\n", lines)}";
    }

    private async Task<string> HandleGetLowStockAsync(Guid businessId)
    {
        var items = await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive && p.CurrentStock <= p.LowStockThreshold)
            .OrderBy(p => p.CurrentStock).ToListAsync();

        if (items.Count == 0) return "✅ All products have sufficient stock.";
        var lines = items.Select(p =>
        {
            var priceStr = p.SellingPrice.HasValue ? $" — {_cs}{p.SellingPrice.Value:N0}" : "";
            return $"• {p.Name}: {p.CurrentStock:0.##} {p.Unit} (min: {p.LowStockThreshold:0.##}){priceStr}";
        });
        return $"⚠️ *Low Stock* ({items.Count} items)\n{string.Join("\n", lines)}";
    }

    private async Task<string> HandleGetOutstandingAsync(Guid businessId, string type)
    {
        var balances = await _ledger.GetOutstandingBalancesAsync(businessId, type);
        if (balances.Count == 0)
            return type == "receivable" ? "No outstanding receivables." : "No outstanding payables.";

        var title = type == "receivable" ? "💰 Outstanding Receivables" : "💸 Outstanding Payables";
        var total = type == "receivable" ? balances.Sum(b => b.TotalReceivable) : balances.Sum(b => b.TotalPayable);

        // Show all contacts, not just 10. The message-truncation logic in SendMessageAsync
        // handles the WhatsApp character limit if the list is very long.
        var lines = balances
            .OrderByDescending(b => type == "receivable" ? b.TotalReceivable : b.TotalPayable)
            .Select(b => type == "receivable"
                ? $"• {b.ContactName}: {_cs}{b.TotalReceivable:N0}"
                : $"• {b.ContactName}: {_cs}{b.TotalPayable:N0}");

        var countNote = balances.Count > 1 ? $" ({balances.Count} contacts)" : "";
        return $"{title}{countNote}\n{string.Join("\n", lines)}\n\n*Total: {_cs}{total:N0}*";
    }

    private async Task<string> HandleGetContactBalanceAsync(Guid businessId, JsonElement ba)
    {
        var contactName = ba.GetStringOrNull("contactName");
        if (string.IsNullOrEmpty(contactName))
            return await HandleGetOutstandingAsync(businessId, "receivable");

        var contact = await _db.Contacts.FirstOrDefaultAsync(c =>
            c.BusinessId == businessId && c.Name.ToLower() == contactName.ToLower());
        if (contact == null) return $"Contact '{contactName}' not found.";

        var entries = await _db.LedgerEntries
            .Where(e => e.ContactId == contact.Id && e.BusinessId == businessId).ToListAsync();

        var receivable = entries.Where(e => e.EntryType == LedgerEntryType.Receivable).Sum(e => e.Amount)
                       - entries.Where(e => e.EntryType == LedgerEntryType.ReceivablePayment).Sum(e => e.Amount);
        var payable = entries.Where(e => e.EntryType == LedgerEntryType.Payable).Sum(e => e.Amount)
                   - entries.Where(e => e.EntryType == LedgerEntryType.PayablePayment).Sum(e => e.Amount);

        if (receivable <= 0 && payable <= 0) return $"{contactName} has no outstanding balance.";

        var parts = new List<string>();
        if (receivable > 0) parts.Add($"owes you {_cs}{receivable:N0}");
        if (payable > 0) parts.Add($"you owe {_cs}{payable:N0}");
        return $"💼 {contactName}: {string.Join(", ", parts)}";
    }

    private async Task<string> HandleGetProfitEstimateAsync(Guid businessId)
    {
        var pos = await _reports.GetCashPositionAsync(businessId);
        return $"📈 *This Month*\nSales: {_cs}{pos.TotalSalesThisMonth:N0}\nExpenses: {_cs}{pos.TotalExpensesThisMonth:N0}\nEst. Cash In: {_cs}{pos.EstimatedCashIn:N0}\nReceivables: {_cs}{pos.OutstandingReceivables:N0}\nPayables: {_cs}{pos.OutstandingPayables:N0}\n*Net: {_cs}{pos.NetPosition:N0}*";
    }

    private async Task<string> HandleGetProductSalesTodayAsync(Guid businessId, JsonElement ba)
    {
        var productName = ba.GetStringOrNull("productName");
        if (string.IsNullOrEmpty(productName)) return "Which product do you want to check?";

        var todayUtc = DateTime.UtcNow.Date;
        var saleItems = await _db.SaleItems
            .Include(i => i.Sale)
            .Include(i => i.Product)
            .Where(i => i.Sale.BusinessId == businessId && i.Sale.CreatedAtUtc >= todayUtc
                        && i.Product.Name.ToLower().Contains(productName.ToLower()))
            .ToListAsync();

        if (saleItems.Count == 0)
            return $"No {productName} sold today.";

        var totalQty = saleItems.Sum(i => i.Quantity);
        var totalRev = saleItems.Sum(i => i.TotalPrice);
        var unit = saleItems.First().Product.Unit;

        return $"📊 *{productName}* today: {totalQty:0.##} {unit} sold — {_cs}{totalRev:N0}";
    }

    private async Task<string> HandleAddStaffAsync(Guid businessId, JsonElement ba)
    {
        var staffLimitErr = await _planGuard.CheckStaffLimitAsync(businessId);
        if (staffLimitErr != null) return $"⚠️ {staffLimitErr}";

        var fullName = ba.GetStringOrNull("fullName");
        var phoneNumber = ba.GetStringOrNull("phoneNumber");
        if (string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(phoneNumber))
            return "Please provide the staff member's name and phone number.\nExample: \"Add staff Mary +2348012345678\" or \"Add staff Ama +233241234567\"";

        var normalizedPhone = NormalizePhone(phoneNumber);
        if (string.IsNullOrEmpty(normalizedPhone))
            return "Invalid phone number. Please use a valid phone number in international format (e.g. +233...)";

        var existingUser = await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == normalizedPhone);
        if (existingUser != null && existingUser.IsActive)
            return $"The number {normalizedPhone} is already registered in BizPilot.";

        var roleStr = ba.GetStringOrNull("role") ?? "Sales";
        if (!Enum.TryParse<UserRole>(roleStr, true, out var role) || role == UserRole.Owner)
            role = UserRole.Sales;

        var setupCode = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        var business = await _db.Businesses.FindAsync(businessId);
        User user;

        if (existingUser != null && !existingUser.IsActive && existingUser.BusinessId == businessId)
        {
            // Reactivating a previously-removed staff member. Bump TokenVersion so any stale JWT they might
            // still possess from before deactivation is invalidated — they must go through the setup flow to regain access.
            existingUser.IsActive = true;
            existingUser.FullName = fullName.Trim();
            existingUser.Role = role;
            existingUser.MustChangePassword = true;
            existingUser.PasswordResetCode = BCrypt.Net.BCrypt.HashPassword(setupCode);
            existingUser.PasswordResetCodeExpiresAtUtc = DateTime.UtcNow.AddHours(24);
            existingUser.TokenVersion++;
            existingUser.FailedLoginAttempts = 0;
            existingUser.LockoutEndsAtUtc = null;
            user = existingUser;
        }
        else
        {
            // If the phone belongs to deactivated staff at a DIFFERENT business, free it up by
            // swapping their phone to a placeholder before creating the new user. The old row stays
            // intact for audit history at the original business.
            if (existingUser != null && !existingUser.IsActive)
            {
                existingUser.PhoneNumber = $"x{existingUser.Id.ToString("N")[..18]}";
                await _db.SaveChangesAsync();
            }

            var tempPassword = Guid.NewGuid().ToString("N")[..16];
            user = new User
            {
                BusinessId = businessId,
                FullName = fullName.Trim(),
                PhoneNumber = normalizedPhone,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword),
                Role = role,
                IsActive = true,
                MustChangePassword = true,
                PasswordResetCode = BCrypt.Net.BCrypt.HashPassword(setupCode),
                PasswordResetCodeExpiresAtUtc = DateTime.UtcNow.AddHours(24)
            };
            _db.Users.Add(user);
        }

        await _db.SaveChangesAsync();

        // Send welcome message to the new staff member
        _ = Task.Run(async () =>
        {
            try
            {
                await SendMessageAsync($"whatsapp:{normalizedPhone}",
                    $"👋 Welcome to *BizPilot*!\n\n" +
                    $"You've been added as *{role}* at *{business?.Name ?? "a business"}*.\n\n" +
                    $"*Set up your dashboard account:*\n" +
                    $"1. Go to app.bizpilot-ai.com/forgot-password\n" +
                    $"2. Enter your phone number: {normalizedPhone}\n" +
                    $"3. Use this setup code: *{setupCode}*\n" +
                    $"4. Set your new password\n\n" +
                    $"⏰ This code expires in 24 hours.\n\n" +
                    $"You can also start using BizPilot right here on WhatsApp! Try:\n" +
                    $"• \"Sold 5 bags of rice at 3000\"\n" +
                    $"• \"Check stock\"\n" +
                    $"• \"Today's sales\"");
            }
            catch { /* best effort */ }
        });

        return $"✅ *Staff added!*\n\n" +
            $"👤 {user.FullName}\n" +
            $"📞 {user.PhoneNumber}\n" +
            $"🏷️ Role: {user.Role}\n\n" +
            $"A welcome message with a setup link has been sent to their WhatsApp. They can set their dashboard password using the forgot-password flow.";
    }

    private async Task<string> HandleGetStaffListAsync(Guid businessId)
    {
        var staff = await _db.Users
            .Where(u => u.BusinessId == businessId && u.IsActive)
            .OrderBy(u => u.Role).ThenBy(u => u.FullName)
            .ToListAsync();

        if (staff.Count == 0) return "No staff members found.";

        var lines = staff.Select(s => $"• {s.FullName} — {s.Role}{(s.PhoneNumber != null ? $" ({s.PhoneNumber})" : "")}");
        return $"👥 *Team Members* ({staff.Count})\n{string.Join("\n", lines)}";
    }

    private async Task<string> HandleGetProductStaffAsync(Guid businessId, JsonElement ba)
    {
        var productName = ba.GetStringOrNull("productName");
        if (string.IsNullOrEmpty(productName)) return "Which product? E.g. \"Who sold rice today?\"";

        var todayUtc = DateTime.UtcNow.Date;
        var saleItems = await _db.SaleItems
            .Include(i => i.Sale)
            .Include(i => i.Product)
            .Where(i => i.Sale.BusinessId == businessId && i.Sale.CreatedAtUtc >= todayUtc
                        && i.Product.Name.ToLower().Contains(productName.ToLower())
                        && i.Sale.RecordedByName != null)
            .ToListAsync();

        if (saleItems.Count == 0)
            return $"No recorded sales of {productName} by any staff member today.";

        var grouped = saleItems
            .GroupBy(i => i.Sale.RecordedByName!)
            .Select(g => new { Staff = g.Key, Qty = g.Sum(i => i.Quantity), Rev = g.Sum(i => i.TotalPrice) })
            .OrderByDescending(s => s.Qty)
            .ToList();

        var unit = saleItems.First().Product.Unit;
        var lines = grouped.Select(s => $"• {s.Staff}: {s.Qty:0.##} {unit} — {_cs}{s.Rev:N0}");

        return $"👥 *Who sold {productName} today*\n{string.Join("\n", lines)}";
    }

    private async Task<string> HandleGetProductBuyersAsync(Guid businessId, JsonElement ba)
    {
        var productName = ba.GetStringOrNull("productName");
        if (string.IsNullOrEmpty(productName)) return "Which product? E.g. \"Who bought rice today?\"";

        var todayUtc = DateTime.UtcNow.Date;
        var saleItems = await _db.SaleItems
            .Include(i => i.Sale).ThenInclude(s => s.Contact)
            .Include(i => i.Product)
            .Where(i => i.Sale.BusinessId == businessId && i.Sale.CreatedAtUtc >= todayUtc
                        && i.Product.Name.ToLower().Contains(productName.ToLower()))
            .OrderByDescending(i => i.Sale.CreatedAtUtc)
            .ToListAsync();

        if (saleItems.Count == 0)
            return $"No sales of {productName} today.";

        var unit = saleItems.First().Product.Unit;
        var actualName = saleItems.First().Product.Name;
        var totalQty = saleItems.Sum(i => i.Quantity);
        var totalRev = saleItems.Sum(i => i.TotalPrice);

        var lines = saleItems.Select(i =>
        {
            var customer = i.Sale.Contact?.Name ?? "Walk-in customer";
            var staff = i.Sale.RecordedByName ?? "Unknown";
            var status = i.Sale.PaymentStatus != PaymentStatus.Paid ? $" ({i.Sale.PaymentStatus})" : "";
            var time = TimeZoneInfo.ConvertTimeFromUtc(i.Sale.CreatedAtUtc, _tz).ToString("HH:mm");
            return $"• {customer}: {i.Quantity:0.##} {unit} @ {_cs}{i.UnitPrice:N0} = {_cs}{i.TotalPrice:N0}{status} — by {staff} at {time}";
        });

        return $"🛒 *Who bought {actualName} today* ({totalQty:0.##} {unit} total — {_cs}{totalRev:N0})\n{string.Join("\n", lines)}";
    }

    private async Task<string> HandleGetSpecificStockAsync(Guid businessId, JsonElement ba)
    {
        var names = new List<string>();
        if (ba.TryGetProperty("productNames", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var n in arr.EnumerateArray())
            {
                var v = n.GetString();
                if (!string.IsNullOrEmpty(v)) names.Add(v.ToLower());
            }
        }

        if (names.Count == 0) return "Which products do you want to check? List them by name.";

        var products = await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive)
            .ToListAsync();

        var matched = products.Where(p => names.Any(n => p.Name.ToLower().Contains(n))).ToList();

        if (matched.Count == 0)
            return $"No products found matching: {string.Join(", ", names)}";

        var lines = matched.Select(p =>
        {
            var prices = new List<string>();
            if (p.SellingPrice.HasValue) prices.Add($"Sell: {_cs}{p.SellingPrice.Value:N0}");
            if (p.CostPrice.HasValue) prices.Add($"Cost: {_cs}{p.CostPrice.Value:N0}");
            var priceStr = prices.Count > 0 ? $" — {string.Join(" | ", prices)}" : "";
            var flag = p.CurrentStock <= p.LowStockThreshold ? " ⚠️" : "";
            return $"• {p.Name}: {p.CurrentStock:0.##} {p.Unit}{flag}{priceStr}";
        });

        return $"📦 *Stock*\n{string.Join("\n", lines)}";
    }

    private async Task<string> HandleGetStaffSalesAsync(Guid businessId, JsonElement ba)
    {
        var staffName = ba.GetStringOrNull("staffName");
        if (string.IsNullOrEmpty(staffName)) return "Which staff member? E.g. \"What did Mary sell today?\"";

        var todayUtc = DateTime.UtcNow.Date;
        var saleItems = await _db.SaleItems
            .Include(i => i.Sale)
            .Include(i => i.Product)
            .Where(i => i.Sale.BusinessId == businessId && i.Sale.CreatedAtUtc >= todayUtc
                        && i.Sale.RecordedByName != null && i.Sale.RecordedByName.ToLower().Contains(staffName.ToLower()))
            .ToListAsync();

        if (saleItems.Count == 0) return $"No sales recorded by {staffName} today.";

        var grouped = saleItems
            .GroupBy(i => new { i.Product.Name, i.Product.Unit })
            .Select(g => new { g.Key.Name, g.Key.Unit, Qty = g.Sum(i => i.Quantity), Rev = g.Sum(i => i.TotalPrice) })
            .OrderByDescending(p => p.Rev).ToList();

        var total = grouped.Sum(p => p.Rev);
        var lines = grouped.Select(p => $"• {p.Name}: {p.Qty:0.##} {p.Unit} — {_cs}{p.Rev:N0}");

        return $"📊 *{staffName}'s sales today*\n{string.Join("\n", lines)}\n\n*Total: {_cs}{total:N0}*";
    }

    // ─── Corrections + Returns ─────────────────────────────────────────────────

    private async Task<string> HandleCorrectLastSaleAsync(Guid businessId, JsonElement ba, User? recordedBy = null)
    {
        var newQty = ba.GetDecimalOrNull("quantity");
        if (!newQty.HasValue || newQty.Value < 0)
            return "What should the correct quantity be? E.g. \"Actually I sold 2\"";

        var lastSale = await _db.Sales
            .Include(s => s.Items).ThenInclude(i => i.Product)
            .Where(s => s.BusinessId == businessId && s.RecordedByUserId == recordedBy!.Id
                && s.CreatedAtUtc >= DateTime.UtcNow.AddMinutes(-30))
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (lastSale == null) return "No recent sale found to correct. You can only correct your own sales within 30 minutes.";

        if (lastSale.Items.Count != 1)
            return "The last sale had multiple items. Please void it on the dashboard and re-record.";

        var saleItem = lastSale.Items.First();
        var product = saleItem.Product;
        var oldQty = saleItem.Quantity;

        if (newQty.Value == oldQty)
            return $"The last sale was already {oldQty:0.##} {product.Unit} of {product.Name}. No change needed.";

        await _sales.VoidAsync(businessId, lastSale.Id, recordedBy?.Id, recordedBy?.FullName);

        if (newQty.Value == 0)
            return $"✅ Last sale voided. {oldQty:0.##} {product.Unit} of {product.Name} returned to stock.";

        // Create new sale with corrected qty
        var unitPrice = saleItem.UnitPrice;
        var newSale = await _sales.CreateAsync(businessId, new CreateSaleRequest
        {
            Items = new List<SaleItemRequest>
            {
                new() { ProductId = product.Id, Quantity = newQty.Value, UnitPrice = unitPrice }
            },
            ContactId = lastSale.ContactId,
            PaymentStatus = lastSale.PaymentStatus
        }, lastSale.Source, lastSale.RecordedByUserId, lastSale.RecordedByName);

        var diff = oldQty - newQty.Value;
        return $"✅ Corrected! Previous sale of {oldQty:0.##} voided.\n" +
               $"• {newQty.Value:0.##} {product.Unit} of {product.Name} @ {_cs}{unitPrice:N0} = {_cs}{newSale.TotalAmount:N0}\n" +
               (diff > 0 ? $"Stock restored: {diff:0.##} {product.Unit} returned to inventory." : $"Stock adjusted: {Math.Abs(diff):0.##} {product.Unit} additional removed.");
    }

    private async Task<string> HandleReturnProductAsync(Guid businessId, JsonElement ba, User? recordedBy = null)
    {
        var productName = ba.GetStringOrNull("productName");
        var qty = ba.GetDecimalOrNull("quantity");
        var contactName = ba.GetStringOrNull("contactName");

        if (string.IsNullOrEmpty(productName) || !qty.HasValue || qty.Value <= 0)
            return "Which product was returned and how many? E.g. \"Customer returned 3 lip gloss\"";

        var (product, error) = await FindProductAsync(businessId, productName);
        if (product == null) return error!;

        // Add stock back
        await _inventory.StockInAsync(businessId, new StockInRequest
        {
            ProductId = product.Id,
            Quantity = qty.Value,
            Notes = $"Return{(contactName != null ? $" from {contactName}" : "")}"
        }, recordedBy?.Id, recordedBy?.FullName);

        var refundAmount = product.SellingPrice.HasValue ? qty.Value * product.SellingPrice.Value : 0;
        var refundNote = refundAmount > 0 ? $"\nRefund amount: {_cs}{refundAmount:N0}" : "";

        return $"✅ Return processed:\n• {qty.Value:0.##} {product.Unit} of {product.Name} returned to stock\nNew stock: {(product.CurrentStock + qty.Value):0.##} {product.Unit}{refundNote}";
    }

    // ─── Batch action handler ──────────────────────────────────────────────────

    private async Task<string> HandleBatchActionAsync(User user, JsonElement ba)
    {
        if (!ba.TryGetProperty("complete", out var completeEl) || completeEl.ValueKind != JsonValueKind.Array)
            return "I couldn't parse the actions. Please try again, one action at a time.";

        var results = new List<string>();
        var failed = new List<string>();
        var idx = 0;

        foreach (var action in completeEl.EnumerateArray())
        {
            idx++;
            var intent = action.GetStringOrNull("intent");
            if (string.IsNullOrEmpty(intent)) continue;

            try
            {
                // Build a mini ParsedMessage for each action
                var subParsed = new DTOs.Parsing.ParsedMessage
                {
                    Intent = intent,
                    Confidence = 0.95,
                    BusinessAction = action
                };

                var result = await ExecuteIntentAsync(user, subParsed);
                // Extract first line of the result for the summary
                var firstLine = result.Split('\n')[0];
                results.Add($"{idx}. {firstLine}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Batch action sub-intent {Intent} failed", intent);
                failed.Add($"{idx}. {intent}: {FriendlyErrorMessage(ex)}");
            }
        }

        var sb = new System.Text.StringBuilder();

        if (results.Count > 0)
        {
            sb.AppendLine($"✅ Processed {results.Count} action{(results.Count != 1 ? "s" : "")}:");
            foreach (var r in results) sb.AppendLine(r);
        }

        if (failed.Count > 0)
        {
            sb.AppendLine($"\n⚠️ Failed:");
            foreach (var f in failed) sb.AppendLine(f);
        }

        // Check for pending items
        if (ba.TryGetProperty("pending", out var pendingEl) && pendingEl.ValueKind == JsonValueKind.Array && pendingEl.GetArrayLength() > 0)
        {
            sb.AppendLine("\n⏳ *Still need info:*");
            foreach (var p in pendingEl.EnumerateArray())
            {
                var question = p.GetStringOrNull("question") ?? "More details needed";
                var pIntent = p.GetStringOrNull("intent") ?? "action";
                sb.AppendLine($"• {pIntent}: {question}");
            }
        }

        if (results.Count == 0 && failed.Count == 0)
            return "I couldn't process any actions. Please try again.";

        return sb.ToString().TrimEnd();
    }

    // ─── Tier 2+3 handlers ──────────────────────────────────────────────────────

    private async Task<string> HandleGetTransactionHistoryAsync(Guid businessId)
    {
        var todayUtc = DateTime.UtcNow.Date;
        var sales = await _db.Sales
            .Include(s => s.Items).ThenInclude(i => i.Product)
            .Include(s => s.Contact)
            .Where(s => s.BusinessId == businessId && s.CreatedAtUtc >= todayUtc)
            .OrderByDescending(s => s.CreatedAtUtc)
            .Take(10)
            .ToListAsync();

        if (sales.Count == 0) return "No transactions today.";

        var lines = sales.Select(s =>
        {
            var time = TimeZoneInfo.ConvertTimeFromUtc(s.CreatedAtUtc, _tz).ToString("h:mm tt");
            var items = string.Join(", ", s.Items.Select(i => $"{i.Quantity:0.##} {i.Product.Unit} {i.Product.Name}"));
            var buyer = s.Contact?.Name ?? "Walk-in";
            var staff = s.RecordedByName ?? "—";
            return $"• {time} — {items} → {buyer} — {_cs}{s.TotalAmount:N0} ({s.PaymentStatus}) [by {staff}]";
        });

        return $"📋 *Today's Transactions*\n{string.Join("\n", lines)}";
    }

    private async Task<string> HandleGetDeadStockAsync(Guid businessId)
    {
        var twoWeeksAgo = DateTime.UtcNow.AddDays(-14);
        var allProducts = await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive && p.CurrentStock > 0)
            .ToListAsync();

        var productIds = allProducts.Select(p => p.Id).ToList();
        var recentSoldIds = await _db.SaleItems
            .Include(i => i.Sale)
            .Where(i => i.Sale.BusinessId == businessId && i.Sale.CreatedAtUtc >= twoWeeksAgo && productIds.Contains(i.ProductId))
            .Select(i => i.ProductId)
            .Distinct()
            .ToListAsync();

        var deadStock = allProducts.Where(p => !recentSoldIds.Contains(p.Id)).ToList();

        if (deadStock.Count == 0) return "All your products have sold in the last 2 weeks. No dead stock.";

        var lines = deadStock.Select(p => $"• {p.Name}: {p.CurrentStock:0.##} {p.Unit} in stock — no sales in 14 days");
        return $"💤 *Dead Stock* ({deadStock.Count} items)\n{string.Join("\n", lines)}\n\nConsider discounting or returning these.";
    }

    private async Task<string> HandleGetProfitByProductAsync(Guid businessId)
    {
        var thirtyDaysAgo = DateTime.UtcNow.Date.AddDays(-29);
        var saleItems = await _db.SaleItems
            .Include(i => i.Sale)
            .Include(i => i.Product)
            .Where(i => i.Sale.BusinessId == businessId && i.Sale.CreatedAtUtc >= thirtyDaysAgo && i.Product.CostPrice.HasValue)
            .ToListAsync();

        if (saleItems.Count == 0) return "Not enough data to calculate profit by product. Make sure products have cost prices set.";

        var grouped = saleItems
            .GroupBy(i => new { i.Product.Name, i.Product.Unit, CostPrice = i.Product.CostPrice!.Value })
            .Select(g => new
            {
                g.Key.Name,
                Revenue = g.Sum(i => i.TotalPrice),
                Cost = g.Sum(i => i.Quantity * g.Key.CostPrice),
                Profit = g.Sum(i => i.TotalPrice) - g.Sum(i => i.Quantity * g.Key.CostPrice),
                Margin = g.Sum(i => i.TotalPrice) > 0
                    ? (g.Sum(i => i.TotalPrice) - g.Sum(i => i.Quantity * g.Key.CostPrice)) / g.Sum(i => i.TotalPrice) * 100
                    : 0
            })
            .OrderByDescending(p => p.Profit)
            .ToList();

        var profitable = grouped.Where(p => p.Profit > 0).Take(5).ToList();
        var losing = grouped.Where(p => p.Profit < 0).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("💰 *Profit by Product (Last 30 Days)*");

        if (profitable.Count > 0)
        {
            sb.AppendLine("\n📈 Most profitable:");
            foreach (var p in profitable)
                sb.AppendLine($"• {p.Name}: {_cs}{p.Profit:N0} profit ({p.Margin:0.#}% margin)");
        }

        if (losing.Count > 0)
        {
            sb.AppendLine("\n📉 Losing money:");
            foreach (var p in losing)
                sb.AppendLine($"• {p.Name}: {_cs}{Math.Abs(p.Profit):N0} loss");
        }

        return sb.ToString().TrimEnd();
    }

    private async Task<string> HandleGetStockoutPredictionAsync(Guid businessId)
    {
        var sevenDaysAgo = DateTime.UtcNow.Date.AddDays(-6);
        var products = await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive && p.CurrentStock > 0)
            .ToListAsync();

        var productIds = products.Select(p => p.Id).ToList();
        var salesByProduct = await _db.SaleItems
            .Include(i => i.Sale)
            .Where(i => i.Sale.BusinessId == businessId && i.Sale.CreatedAtUtc >= sevenDaysAgo && productIds.Contains(i.ProductId))
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, TotalSold = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.TotalSold);

        var predictions = products
            .Select(p =>
            {
                var sold7d = salesByProduct.GetValueOrDefault(p.Id, 0);
                var dailyRate = sold7d / 7m;
                var daysLeft = dailyRate > 0 ? p.CurrentStock / dailyRate : 999;
                var restock = dailyRate > 0 ? Math.Max(0, (dailyRate * 7) - p.CurrentStock) : 0;
                return new { p.Name, p.Unit, p.CurrentStock, DailyRate = dailyRate, DaysLeft = daysLeft, Restock = restock };
            })
            .Where(p => p.DaysLeft < 14 && p.DailyRate > 0)
            .OrderBy(p => p.DaysLeft)
            .Take(10)
            .ToList();

        if (predictions.Count == 0) return "All products have 2+ weeks of stock based on current sales rates.";

        var lines = predictions.Select(p =>
        {
            var urgency = p.DaysLeft <= 3 ? "🔴" : p.DaysLeft <= 7 ? "🟡" : "🟢";
            var restockNote = p.Restock > 0 ? $" — restock {p.Restock:0.##} {p.Unit} for 7 days" : "";
            return $"{urgency} {p.Name}: ~{p.DaysLeft:0.#} days left ({p.CurrentStock:0.##} {p.Unit}){restockNote}";
        });

        return $"🔮 *Stockout Predictions*\n{string.Join("\n", lines)}";
    }

    private async Task<string> HandleGetTopProductsAsync(Guid businessId, JsonElement ba)
    {
        var direction = ba.GetStringOrNull("direction");
        var isBottom = direction == "bottom";
        var count = (int)(ba.GetDecimalOrNull("count") ?? 10);
        if (count < 1) count = 10;
        if (count > 20) count = 20;

        var thirtyDaysAgo = DateTime.UtcNow.Date.AddDays(-29);
        var allSold = await _db.SaleItems
            .Include(i => i.Sale)
            .Include(i => i.Product)
            .Where(i => i.Sale.BusinessId == businessId && i.Sale.CreatedAtUtc >= thirtyDaysAgo)
            .GroupBy(i => new { i.Product.Name, i.Product.Unit })
            .Select(g => new
            {
                g.Key.Name,
                g.Key.Unit,
                TotalQty = g.Sum(i => i.Quantity),
                TotalRevenue = g.Sum(i => i.TotalPrice)
            })
            .ToListAsync();

        if (allSold.Count == 0) return "No sales in the last 30 days to rank products.";

        var sorted = isBottom
            ? allSold.OrderBy(p => p.TotalRevenue).Take(count).ToList()
            : allSold.OrderByDescending(p => p.TotalRevenue).Take(count).ToList();

        var lines = sorted.Select((p, i) =>
            $"{i + 1}. {p.Name}: {p.TotalQty:0.##} {p.Unit} sold — {_cs}{p.TotalRevenue:N0}");

        var title = isBottom ? "📉 *Least Selling Products (Last 30 Days)*" : "🏆 *Top Products (Last 30 Days)*";
        return $"{title}\n{string.Join("\n", lines)}";
    }

    private async Task<string> HandleGetTodaySalesDetailAsync(Guid businessId)
    {
        var todayUtc = DateTime.UtcNow.Date;
        var saleItems = await _db.SaleItems
            .Include(i => i.Sale)
            .Include(i => i.Product)
            .Where(i => i.Sale.BusinessId == businessId && i.Sale.CreatedAtUtc >= todayUtc)
            .ToListAsync();

        if (saleItems.Count == 0) return "No sales recorded today.";

        var grouped = saleItems
            .GroupBy(i => new { i.Product.Name, i.Product.Unit })
            .Select(g => new
            {
                g.Key.Name,
                g.Key.Unit,
                TotalQty = g.Sum(i => i.Quantity),
                TotalRevenue = g.Sum(i => i.TotalPrice)
            })
            .OrderByDescending(p => p.TotalRevenue)
            .ToList();

        var grandTotal = grouped.Sum(p => p.TotalRevenue);
        var lines = grouped.Select(p =>
            $"• {p.Name}: {p.TotalQty:0.##} {p.Unit} sold — {_cs}{p.TotalRevenue:N0}");

        return $"🛒 *Products Sold Today*\n{string.Join("\n", lines)}\n\n*Total: {_cs}{grandTotal:N0}*";
    }

    private async Task<string> HandleHoldStockAsync(Guid businessId, JsonElement ba)
    {
        var productName = ba.GetStringOrNull("productName");
        var qty = ba.GetDecimalOrNull("quantity");
        var contactName = ba.GetStringOrNull("contactName");

        if (string.IsNullOrEmpty(productName) || !qty.HasValue || qty.Value <= 0)
            return "Please specify the product and quantity to hold. E.g. \"Hold 5 bags of rice for Ada\"";

        var (product, error) = await FindProductAsync(businessId, productName);
        if (product == null) return error!;

        try
        {
            var hold = await _holds.CreateHoldAsync(businessId, product.Id, contactName ?? "Customer", qty.Value, ba.GetStringOrNull("notes"), Common.EntrySource.WhatsApp);
            return $"✅ Held {qty.Value:0.##} {product.Unit} of {product.Name} for *{hold.ContactName}*.\n\nWhen they arrive, say \"{hold.ContactName} came for her {product.Name.ToLower()}\" to convert to a sale, or \"Release {hold.ContactName}'s hold\" to cancel.";
        }
        catch (InvalidOperationException ex)
        {
            // InvalidOperationException here is always a business-logic error from StockHoldService
            // (insufficient stock, duplicate hold). Safe to surface the message directly.
            return $"❌ {ex.Message}";
        }
    }

    private async Task<string> HandleReleaseHoldAsync(Guid businessId, JsonElement ba)
    {
        var productName = ba.GetStringOrNull("productName");
        var contactName = ba.GetStringOrNull("contactName");
        var convertToSale = ba.GetStringOrNull("convertToSale");

        // Find matching active hold
        var holds = await _holds.GetActiveHoldsAsync(businessId);
        StockHoldDto? match = null;

        if (!string.IsNullOrEmpty(contactName) && !string.IsNullOrEmpty(productName))
            match = holds.FirstOrDefault(h => h.ContactName.ToLower().Contains(contactName.ToLower()) && h.ProductName.ToLower().Contains(productName.ToLower()));
        else if (!string.IsNullOrEmpty(contactName))
            match = holds.FirstOrDefault(h => h.ContactName.ToLower().Contains(contactName.ToLower()));
        else if (!string.IsNullOrEmpty(productName))
            match = holds.FirstOrDefault(h => h.ProductName.ToLower().Contains(productName.ToLower()));

        if (match == null)
            return "No matching active hold found. Say \"What's on hold?\" to see all holds.";

        // "Ada came for her rice" → convert to sale
        if (convertToSale == "true")
        {
            try
            {
                var sale = await _holds.ConvertToSaleAsync(businessId, match.Id);
                return $"✅ Hold converted to sale!\n• {match.Quantity:0.##} {match.Unit} of {match.ProductName} sold to {match.ContactName}\n*Total: {_cs}{sale.TotalAmount:N0}*";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hold-to-sale conversion failed for hold {HoldId}", match.Id);
                return $"❌ Could not convert hold: {FriendlyErrorMessage(ex)}";
            }
        }

        // Just release
        await _holds.ReleaseHoldAsync(businessId, match.Id);
        return $"✅ Released hold: {match.Quantity:0.##} {match.Unit} of {match.ProductName} for {match.ContactName}. Stock is now available again.";
    }

    private async Task<string> HandleGetActiveHoldsAsync(Guid businessId)
    {
        var holds = await _holds.GetActiveHoldsAsync(businessId);
        if (holds.Count == 0) return "No active holds right now.";

        var lines = holds.Select(h =>
            $"• {h.Quantity:0.##} {h.Unit} of {h.ProductName} — for *{h.ContactName}* ({h.CreatedAtUtc:MMM d})");
        return $"📋 *Active Holds* ({holds.Count})\n{string.Join("\n", lines)}\n\nSay \"release [name]'s hold\" or \"[name] came for [product]\" to resolve.";
    }

    private async Task<string> HandleCorrectLastExpenseAsync(Guid businessId, JsonElement ba, User? recordedBy = null)
    {
        // Find the most recent non-deleted expense by this user (within 2 hours)
        var cutoff = DateTime.UtcNow.AddHours(-2);
        var lastExpense = await _db.Expenses
            .Where(e => e.BusinessId == businessId && !e.IsDeleted
                        && e.RecordedByUserId == recordedBy!.Id
                        && e.CreatedAtUtc >= cutoff)
            .OrderByDescending(e => e.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (lastExpense == null)
            return "No recent expense found to modify. You can only correct your own expenses within 2 hours.";

        // Check for replacement items (batch correction: "paid 20k to Mary and 40k to Ken")
        var replacements = new List<(string Category, decimal Amount, string? PaidTo, string? Notes)>();

        if (ba.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in itemsEl.EnumerateArray())
            {
                var cat = item.GetStringOrNull("category") ?? lastExpense.Category;
                var amt = item.GetDecimalOrNull("amount");
                if (!amt.HasValue || amt.Value <= 0) continue;
                replacements.Add((cat, amt.Value, item.GetStringOrNull("paidTo"), item.GetStringOrNull("notes")));
            }
        }

        // Single correction: just update amount/category/notes
        var newAmount = ba.GetDecimalOrNull("amount");
        var newCategory = ba.GetStringOrNull("category");
        var newPaidTo = ba.GetStringOrNull("paidTo");
        var newNotes = ba.GetStringOrNull("notes");

        if (replacements.Count == 0 && newAmount.HasValue)
        {
            replacements.Add((newCategory ?? lastExpense.Category, newAmount.Value, newPaidTo ?? lastExpense.PaidTo, newNotes));
        }

        if (replacements.Count == 0)
            return $"What would you like to change about the last expense ({_cs}{lastExpense.Amount:N0} {lastExpense.Category})? " +
                   "For example: \"Change amount to 25,000\" or \"Change category to Transport\".";

        // Void the original expense
        var oldRef = $"EX-{lastExpense.Id.ToString("N")[..8].ToUpper()}";
        lastExpense.IsDeleted = true;
        lastExpense.DeletedAtUtc = DateTime.UtcNow;
        lastExpense.Notes = $"{lastExpense.Notes ?? lastExpense.Category} [Voided — replaced by correction]";

        // Create replacement(s)
        var results = new List<string>();
        foreach (var (cat, amt, paidTo, notes) in replacements)
        {
            await _expenses.CreateAsync(businessId, new DTOs.Expenses.CreateExpenseRequest
            {
                Category = cat,
                Amount = amt,
                PaidTo = paidTo,
                Notes = $"{notes ?? cat} (corrected from {oldRef}: {_cs}{lastExpense.Amount:N0} {lastExpense.Category})"
            }, EntrySource.WhatsApp, recordedBy?.Id, recordedBy?.FullName);

            var paidToNote = !string.IsNullOrEmpty(paidTo) ? $" to {paidTo}" : "";
            results.Add($"{_cs}{amt:N0} for {cat}{paidToNote}");
        }

        await _db.SaveChangesAsync();

        return $"✅ Corrected! Original expense voided ({_cs}{lastExpense.Amount:N0} {lastExpense.Category}).\n" +
               $"Replacement{(results.Count > 1 ? "s" : "")}:\n" +
               string.Join("\n", results.Select((r, i) => $"• {r}"));
    }

    private async Task<string> HandleGetTodayExpensesAsync(Guid businessId)
    {
        var todayUtc = DateTime.UtcNow.Date;
        var expenses = await _db.Expenses
            .Where(e => e.BusinessId == businessId && e.CreatedAtUtc >= todayUtc)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(10)
            .ToListAsync();

        if (expenses.Count == 0) return "No expenses recorded today.";

        var lines = expenses.Select(e =>
            $"• {e.Category} — {_cs}{e.Amount:N0}" + (e.Notes != null ? $" ({e.Notes})" : ""));
        var total = expenses.Sum(e => e.Amount);
        return $"💸 *Today's Expenses* ({expenses.Count} items)\n{string.Join("\n", lines)}\n\n*Total: {_cs}{total:N0}*";
    }

    private async Task<string> HandleGetRecentExpensesAsync(Guid businessId)
    {
        var sevenDaysAgo = DateTime.UtcNow.Date.AddDays(-6);
        var expenses = await _db.Expenses
            .Where(e => e.BusinessId == businessId && e.CreatedAtUtc >= sevenDaysAgo)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(10)
            .ToListAsync();

        if (expenses.Count == 0) return "No expenses in the last 7 days.";

        var lines = expenses.Select(e =>
        {
            var date = TimeZoneInfo.ConvertTimeFromUtc(e.CreatedAtUtc, _tz).ToString("MMM d");
            return $"• {date} — {e.Category} — {_cs}{e.Amount:N0}" + (e.PaidTo != null ? $" (to {e.PaidTo})" : "");
        });
        var total = expenses.Sum(e => e.Amount);
        return $"💸 *Recent Expenses* (last 7 days)\n{string.Join("\n", lines)}\n\n*Total: {_cs}{total:N0}*";
    }

    private async Task<string> HandleUpdateLowStockThresholdAsync(Guid businessId, JsonElement ba)
    {
        var productName = ba.GetStringOrNull("productName");
        var threshold = ba.GetDecimalOrNull("threshold");

        if (string.IsNullOrEmpty(productName) || !threshold.HasValue)
            return "Which product, and what stock level should I alert you at? E.g. \"Alert me when rice drops to 5\"";

        var (product, error) = await FindProductAsync(businessId, productName);
        if (product == null) return error!;

        product.LowStockThreshold = threshold.Value;
        await _db.SaveChangesAsync();

        return $"✅ Low stock alert for *{product.Name}* set to {threshold.Value:0.##} {product.Unit}. You'll be notified when stock drops below this.";
    }

    private async Task<string> HandleDeleteProductAsync(Guid businessId, JsonElement ba)
    {
        var productName = ba.GetStringOrNull("productName");
        var deleteAll = ba.GetStringOrNull("deleteAll");
        var deleteCategory = ba.GetStringOrNull("deleteCategory");

        // Delete by category
        if (!string.IsNullOrEmpty(deleteCategory))
        {
            var catProducts = await _db.Products
                .Where(p => p.BusinessId == businessId && p.IsActive && p.Category != null && p.Category.ToLower() == deleteCategory.ToLower())
                .ToListAsync();

            if (catProducts.Count == 0) return $"No active products found in the \"{deleteCategory}\" category.";

            foreach (var p in catProducts) p.IsActive = false;
            await _db.SaveChangesAsync();

            return $"✅ Deleted {catProducts.Count} products in *{deleteCategory}*: {string.Join(", ", catProducts.Select(p => p.Name))}.\n\nPast sales are still in reports.";
        }

        // Batch delete all products
        if (deleteAll == "true")
        {
            var allProducts = await _db.Products
                .Where(p => p.BusinessId == businessId && p.IsActive)
                .ToListAsync();

            if (allProducts.Count == 0) return "You have no active products to delete.";

            foreach (var p in allProducts) p.IsActive = false;
            await _db.SaveChangesAsync();

            return $"✅ Deleted all {allProducts.Count} products: {string.Join(", ", allProducts.Select(p => p.Name))}.\n\nPast sales are still in reports. To undo, edit products on the dashboard.";
        }

        if (string.IsNullOrEmpty(productName)) return "Which product should I delete?";

        var (product, error) = await FindProductAsync(businessId, productName);
        if (product == null) return error!;

        product.IsActive = false;
        await _db.SaveChangesAsync();

        return $"✅ *{product.Name}* has been removed from your products. Past sales are still in reports.\n\nTo undo, edit the product on the dashboard.";
    }

    private static string HandleGreet(string businessName) =>
        $"👋 Hi! I'm your BizPilot assistant for *{businessName}*.\n\n" +
        "🛒 \"Sold 3 bags of rice\"\n" +
        "📥 \"Bought 10 bags of rice\"\n" +
        "💸 \"Spent 5k on transport\"\n" +
        "📦 \"Check inventory\"\n" +
        "💰 \"Kofi owes me 20k\"\n" +
        "📊 \"What did I sell today?\"\n\n" +
        "Type *help* for more commands (holds, staff, insights, bulk actions).\n\n" +
        "_I'm an AI assistant. Please review what I record — say *undo* if anything looks wrong._";

    private static string HandleHelp() =>
        "📖 *More commands:*\n\n" +
        "🔒 *Holds:* \"Hold 5 rice for Ama\" / \"What's on hold?\"\n" +
        "👥 *Staff:* \"Add staff Mary +2348012345678 as Admin\" / \"Who are my staff?\" / \"What did Mary sell?\"\n" +
        "    _Say *roles* to see what each role can do_\n" +
        "🔮 *Insights:* \"When will I run out?\" / \"Most profitable product?\" / \"Stock value\"\n" +
        "💰 *Debts:* \"Clear Ama's debt\" / \"Clear all debts\"\n" +
        "📊 *Reports:* \"Top 3 products\" / \"Least selling item\" / \"Dead stock\" / \"Compare weeks\"\n" +
        "📥 *Bulk restock:* \"Bought 10 rice, 5 juice, 3 shampoo\"\n" +
        "🛒 *Multi-sale:* \"Sold 3 rice and 2 beans at 5k each\"\n" +
        "✏️ *Corrections:* \"Cancel that\" / \"That was on credit\" / \"Add Ama to that\"\n" +
        "📋 *Plans:* \"What plan am I on?\" / \"Plans\"\n\n" +
        "💡 *Tip:* Do multiple things at once!\n" +
        "\"Bought 3 yam at 2k, sold 2 toothpaste at 5k, NEPA bill 10k\"";

    private static string HandleShowRoles() =>
        "👥 *Staff Roles*\n\n" +
        "👑 *Owner* — Full access. Manage staff, settings, billing, all reports.\n\n" +
        "🔑 *Admin* — Everything except billing & settings. Can manage staff, void sales, view all reports, record debts.\n\n" +
        "🛒 *Sales* — Record sales, view stock, view own sales reports. Cannot manage staff or see full reports.\n\n" +
        "📒 *Bookkeeper* — Record expenses, manage debts, view all reports & stock. Cannot record sales.\n\n" +
        "👁️ *Viewer* — View-only. Can see reports and stock levels but cannot record anything.\n\n" +
        "To add staff with a role:\n\"Add staff Mary +2348012345678 as Bookkeeper\"";

    private static string HandleShowReports() =>
        "📊 *Available Reports*\n\n" +
        "🛒 *Sales:*\n" +
        "• \"Daily summary\" — full overview with debts, stock, top product\n" +
        "• \"Today's sales\" — quick revenue + expenses + net\n" +
        "• \"What did I sell today\" — product-by-product breakdown\n" +
        "• \"This week's sales\" — weekly summary with profit\n" +
        "• \"Top 5 products\" — best sellers (last 30 days)\n" +
        "• \"Least selling items\" — worst sellers\n" +
        "• \"Most profitable product\" — profit margins by product\n\n" +
        "📦 *Inventory:*\n" +
        "• \"Check inventory\" — all stock levels\n" +
        "• \"Inventory with prices\" — stock + selling/cost prices\n" +
        "• \"Show me rice and beans\" — specific products only\n" +
        "• \"What's running low\" — low stock items\n" +
        "• \"Dead stock\" — products not sold in 2+ weeks\n" +
        "• \"When will I run out\" — stockout predictions\n" +
        "• \"What's on hold\" — reserved items\n\n" +
        "💰 *Money:*\n" +
        "• \"Today's expenses\" — spending breakdown\n" +
        "• \"Recent expenses\" — last 7 days\n" +
        "• \"Who owes me\" — outstanding receivables\n" +
        "• \"Who I owe\" — outstanding payables\n" +
        "• \"Kofi's balance\" — specific contact\n" +
        "• \"Profit this month\" — monthly cash position\n\n" +
        "👥 *Staff:*\n" +
        "• \"Who are my staff\" — team list\n" +
        "• \"What did Mary sell\" — staff sales\n" +
        "• \"Who sold rice\" — which staff sold a product\n\n" +
        "📋 *Activity:*\n" +
        "• \"Today's transactions\" — full log with times + buyers";

    private static string HandleUnknown() =>
        "I didn't quite get that. Try:\n\n" +
        "• \"Sold 3 bags of rice\"\n" +
        "• \"Spent 5k on transport\"\n" +
        "• \"Check inventory\"\n" +
        "• \"Who owes me?\"\n" +
        "• \"What did I sell today?\"\n\n" +
        "Type *help* for more commands.";

    private async Task<(Product? Product, string? Error)> FindProductAsync(Guid businessId, string name)
    {
        // 1. Exact match (case-insensitive)
        var product = await _db.Products.FirstOrDefaultAsync(p =>
            p.BusinessId == businessId && p.IsActive && p.Name.ToLower() == name.ToLower());
        if (product != null) return (product, null);

        // 2. Substring match — catches "rice" → "white rice"
        var matches = await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive &&
                        p.Name.ToLower().Contains(name.ToLower()))
            .ToListAsync();

        if (matches.Count == 1) return (matches[0], null);
        if (matches.Count > 1)
        {
            var names = string.Join(", ", matches.Select(p => p.Name));
            return (null, $"Multiple products match '{name}': {names}. Please be more specific.");
        }

        // 3. Fuzzy match — catches typos like "rics" → "rice" or "face primr" → "face primer".
        // Uses Levenshtein distance scaled to length, then ranks candidates. Only suggests when the
        // closest match is within a reasonable distance so we don't auto-correct wildly different words.
        var allProducts = await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive)
            .ToListAsync();

        if (allProducts.Count == 0)
            return (null, "You have no products set up yet. Add some first with, e.g. \"Bought 10 bags of rice at 3000\".");

        var lowerName = name.ToLowerInvariant();
        var scored = allProducts
            .Select(p => new { Product = p, Distance = LevenshteinDistance(lowerName, p.Name.ToLowerInvariant()) })
            // Scale threshold by search-term length: short typos tolerate 1-2 edits, longer words tolerate more
            .Where(x => x.Distance <= Math.Max(2, lowerName.Length / 3))
            .OrderBy(x => x.Distance)
            .ToList();

        if (scored.Count == 0)
        {
            // No fuzzy candidates within reasonable distance — fall back to showing available products,
            // but cap the list so huge inventories don't send a 5KB message.
            var available = string.Join(", ", allProducts.Take(15).Select(p => p.Name));
            var more = allProducts.Count > 15 ? $" (and {allProducts.Count - 15} more)" : "";
            return (null, $"Product '{name}' not found. Did you mean one of your existing products?\n\n{available}{more}");
        }

        // If there's a clear best match (single candidate or tight score), suggest it politely.
        if (scored.Count == 1 || scored[0].Distance < scored[Math.Min(1, scored.Count - 1)].Distance)
        {
            var top = scored[0].Product;
            return (null, $"Product '{name}' not found. Did you mean *{top.Name}*? If yes, please try again with that exact name.");
        }

        // Multiple near-matches — show them and let the user pick.
        var suggestions = string.Join(", ", scored.Take(3).Select(s => s.Product.Name));
        return (null, $"Product '{name}' not found. Did you mean one of: {suggestions}?");
    }

    /// <summary>
    /// Classic Levenshtein edit distance — number of insertions, deletions, or substitutions to transform
    /// one string into another. Used for typo-tolerant product matching. Implementation is standard
    /// dynamic-programming; kept here as a static helper to avoid a library dependency for one function.
    /// </summary>
    private static int LevenshteinDistance(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a.Length;

        var matrix = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) matrix[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) matrix[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }
        return matrix[a.Length, b.Length];
    }

    private async Task<string> HandleCorrectDebtAsync(Guid businessId, JsonElement ba, User? recordedBy = null)
    {
        var contactName = ba.GetStringOrNull("contactName");
        var newAmount = ba.GetDecimalOrNull("amount");
        if (string.IsNullOrEmpty(contactName))
            return "Which contact's debt do you want to adjust?";
        if (!newAmount.HasValue || newAmount.Value < 0)
            return "What should the new amount be?";

        var contact = await _db.Contacts.FirstOrDefaultAsync(c =>
            c.BusinessId == businessId && EF.Functions.ILike(c.Name, $"%{contactName}%"));
        if (contact == null) return $"Contact '{contactName}' not found.";

        // Find the most recent receivable or payable entry for this contact
        var latestEntry = await _db.LedgerEntries
            .Where(e => e.BusinessId == businessId && e.ContactId == contact.Id
                        && (e.EntryType == LedgerEntryType.Receivable || e.EntryType == LedgerEntryType.Payable))
            .OrderByDescending(e => e.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (latestEntry == null)
            return $"No existing debt found for {contact.Name}. To create one, say \"{contact.Name} owes me [amount]\" or \"I owe {contact.Name} [amount]\".";

        var oldAmount = latestEntry.Amount;
        var typeLabel = latestEntry.EntryType == LedgerEntryType.Receivable ? "receivable" : "payable";

        if (newAmount.Value == 0)
        {
            // Zero means clear the debt — create a reversal payment
            var paymentType = latestEntry.EntryType == LedgerEntryType.Receivable
                ? LedgerEntryType.ReceivablePayment : LedgerEntryType.PayablePayment;

            // Calculate total outstanding, not just the latest entry
            var entries = await _db.LedgerEntries
                .Where(e => e.BusinessId == businessId && e.ContactId == contact.Id)
                .ToListAsync();

            var dt = latestEntry.EntryType;
            var pt = latestEntry.EntryType == LedgerEntryType.Receivable
                ? LedgerEntryType.ReceivablePayment : LedgerEntryType.PayablePayment;

            var outstanding = entries.Where(e => e.EntryType == dt).Sum(e => e.Amount)
                            - entries.Where(e => e.EntryType == pt).Sum(e => e.Amount);

            if (outstanding <= 0) return $"{contact.Name} has no outstanding {typeLabel} balance.";

            _db.LedgerEntries.Add(new LedgerEntry
            {
                BusinessId = businessId,
                ContactId = contact.Id,
                EntryType = paymentType,
                Amount = outstanding,
                Notes = $"Debt cleared (adjusted from {_cs}{outstanding:N0} to {_cs}0)",
                Source = EntrySource.WhatsApp,
                RecordedByUserId = recordedBy?.Id,
                RecordedByName = recordedBy?.FullName
            });
            await _db.SaveChangesAsync();
            return $"✅ Cleared {contact.Name}'s {typeLabel} balance (was {_cs}{outstanding:N0}).";
        }

        // Compute CURRENT outstanding balance (sum of all entries), not just the latest entry's amount.
        // Without this, repeated adjustments (100k→200k→300k) stack incorrectly because each delta
        // is calculated from the original entry instead of the running balance.
        var debtType = latestEntry.EntryType;
        var payType = latestEntry.EntryType == LedgerEntryType.Receivable
            ? LedgerEntryType.ReceivablePayment : LedgerEntryType.PayablePayment;

        var allEntries = await _db.LedgerEntries
            .Where(e => e.BusinessId == businessId && e.ContactId == contact.Id
                        && (e.EntryType == debtType || e.EntryType == payType))
            .ToListAsync();

        var currentBalance = allEntries.Where(e => e.EntryType == debtType).Sum(e => e.Amount)
                           - allEntries.Where(e => e.EntryType == payType).Sum(e => e.Amount);

        var delta = newAmount.Value - currentBalance;
        if (delta == 0) return $"{contact.Name}'s {typeLabel} is already {_cs}{currentBalance:N0}.";

        var adjustmentType = delta > 0 ? debtType : payType;

        _db.LedgerEntries.Add(new LedgerEntry
        {
            BusinessId = businessId,
            ContactId = contact.Id,
            EntryType = adjustmentType,
            Amount = Math.Abs(delta),
            Notes = $"Adjusted: {_cs}{currentBalance:N0} → {_cs}{newAmount.Value:N0}",
            Source = "Adjustment",
            RecordedByUserId = recordedBy?.Id,
            RecordedByName = recordedBy?.FullName
        });
        await _db.SaveChangesAsync();

        var direction = latestEntry.EntryType == LedgerEntryType.Receivable
            ? $"{contact.Name} owes you" : $"You owe {contact.Name}";
        return $"✅ Adjusted: {direction} {_cs}{newAmount.Value:N0} (was {_cs}{currentBalance:N0})";
    }

    private async Task<string> HandleCreateContactAsync(Guid businessId, JsonElement ba)
    {
        var name = ba.GetStringOrNull("contactName");
        if (string.IsNullOrWhiteSpace(name))
            return "Please provide the contact's name. E.g. \"Add contact Ama Mensah\"";

        var phone = ba.GetStringOrNull("phoneNumber");
        var typeStr = ba.GetStringOrNull("contactType") ?? "Customer";
        var contactType = Enum.TryParse<ContactType>(typeStr, true, out var ct) ? ct : ContactType.Customer;

        var existing = await _db.Contacts.FirstOrDefaultAsync(c =>
            c.BusinessId == businessId && EF.Functions.ILike(c.Name, name));

        if (existing != null)
        {
            var updates = new List<string>();
            if (!string.IsNullOrEmpty(phone) && string.IsNullOrEmpty(existing.PhoneNumber))
            {
                existing.PhoneNumber = phone;
                updates.Add($"phone: {phone}");
            }
            if (existing.Type != contactType && contactType != ContactType.Customer)
            {
                existing.Type = contactType;
                updates.Add($"type: {contactType}");
            }

            if (updates.Count > 0)
            {
                await _db.SaveChangesAsync();
                return $"Contact *{existing.Name}* already exists — updated {string.Join(", ", updates)}.";
            }
            return $"Contact *{existing.Name}* already exists ({existing.Type}).";
        }

        var contact = new Contact
        {
            BusinessId = businessId,
            Name = name.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            Type = contactType,
            Source = EntrySource.WhatsApp
        };
        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync();

        var phoneNote = !string.IsNullOrEmpty(phone) ? $"\n📞 {phone}" : "";
        return $"✅ Contact added: *{contact.Name}* ({contact.Type}){phoneNote}";
    }

    private async Task<Contact> FindOrCreateContactAsync(Guid businessId, string name, ContactType type)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c =>
            c.BusinessId == businessId && c.Name.ToLower() == name.ToLower());

        if (contact != null) return contact;

        contact = new Contact { BusinessId = businessId, Name = name, Type = type, Source = EntrySource.WhatsApp };
        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync();
        return contact;
    }

    private static DateTime _lastRateLimitCleanup = DateTime.UtcNow;

    private static bool IsRateLimited(string phone)
    {
        lock (_rateLimitLock)
        {
            var now = DateTime.UtcNow;

            // Periodic cleanup: remove stale entries every 5 minutes to prevent unbounded growth
            if (now - _lastRateLimitCleanup > TimeSpan.FromMinutes(5))
            {
                var staleThreshold = now - RateLimitWindow;
                var staleKeys = _rateLimits
                    .Where(kv => kv.Value.Count == 0 || kv.Value.All(t => t < staleThreshold))
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (var key in staleKeys) _rateLimits.Remove(key);
                _lastRateLimitCleanup = now;
            }

            if (!_rateLimits.TryGetValue(phone, out var timestamps))
            {
                timestamps = new List<DateTime>();
                _rateLimits[phone] = timestamps;
            }
            // Drop old entries outside the window
            timestamps.RemoveAll(t => now - t > RateLimitWindow);
            if (timestamps.Count == 0)
            {
                _rateLimits.Remove(phone);
                timestamps = new List<DateTime>();
                _rateLimits[phone] = timestamps;
            }
            if (timestamps.Count >= RateLimitMaxMessages) return true;
            timestamps.Add(now);
            return false;
        }
    }

    // Normalize phone numbers to E.164 (+countrycode...) format.
    // Handles: "08012345678" (NG local), "2348012345678", "+2348012345678", "+1 (613) 712-8154"
    // Helper: returns the last 4 digits of a phone for safe logging (PII-friendly).
    private static string RedactPhone(string? phone)
        => string.IsNullOrEmpty(phone) || phone.Length < 4 ? "****" : phone[^4..];

    public static string NormalizePhone(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        // Strip everything except digits and leading +
        var hasPlus = raw.TrimStart().StartsWith("+");
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return string.Empty;

        if (hasPlus)
            return "+" + digits; // Already international — keep as-is

        // Local format: starts with 0 — try to match against known African country codes by digit count.
        // Nigerian local (0xxx): 11 digits → +234. Ghanaian local (0xxx): 10 digits → +233.
        // Most African countries: 10-11 digits local with leading 0.
        if (digits.StartsWith("0") && digits.Length >= 9 && digits.Length <= 11)
        {
            // Try known prefixes — match by total local digit count
            if (digits.Length == 11) return "+234" + digits[1..]; // Nigeria
            if (digits.Length == 10) return "+233" + digits[1..]; // Ghana (or Kenya +254, SA +27 — ambiguous, default Ghana)
            if (digits.Length == 9) return "+254" + digits[1..];  // Kenya
        }

        // International without +: starts with a known country code
        if (digits.StartsWith("234") && digits.Length == 13) return "+" + digits; // Nigeria
        if (digits.StartsWith("233") && digits.Length == 12) return "+" + digits; // Ghana
        if (digits.StartsWith("254") && digits.Length == 12) return "+" + digits; // Kenya
        if (digits.StartsWith("27") && digits.Length == 11) return "+" + digits;  // South Africa
        if (digits.StartsWith("255") && digits.Length == 12) return "+" + digits; // Tanzania
        if (digits.StartsWith("256") && digits.Length == 12) return "+" + digits; // Uganda
        if (digits.StartsWith("237") && digits.Length == 12) return "+" + digits; // Cameroon

        // North American: 10 digits (no country code) → +1
        if (digits.Length == 10 && !digits.StartsWith("0"))
            return "+1" + digits;

        // North American: 11 digits starting with 1
        if (digits.Length == 11 && digits.StartsWith("1"))
            return "+" + digits;

        // Fallback: prepend +
        return "+" + digits;
    }

    public async Task SendMessageAsync(string to, string text, Guid? businessId = null, Guid? userId = null)
    {
        var accountSid = _config["Twilio:AccountSid"];
        var authToken = _config["Twilio:AuthToken"];
        var from = _config["Twilio:WhatsAppFrom"] ?? "whatsapp:+14155238886";

        if (string.IsNullOrEmpty(accountSid) || string.IsNullOrEmpty(authToken))
        {
            // Twilio not configured — message stored in MessageLogs only (no logging of recipient phone)
            _logger.LogInformation("[DEV] WhatsApp would be sent (Twilio not configured)");

            _db.MessageLogs.Add(new MessageLog
            {
                BusinessId = businessId,
                UserId = userId,
                Direction = MessageDirection.Outbound,
                RawMessage = text,
                ProcessingStatus = MessageProcessingStatus.Executed
            });
            await _db.SaveChangesAsync();
            return;
        }

        TwilioClient.Init(accountSid, authToken);

        var toNumber = to.StartsWith("whatsapp:") ? to : $"whatsapp:{to}";

        // WhatsApp messages have a 1600-character limit. Truncate long messages (e.g., bulk sales
        // listing 30+ products) rather than letting Twilio reject the send and crashing the flow.
        // The full text is preserved in MessageLogs for audit; the user just sees a trimmed version.
        const int MaxWhatsAppLength = 1500; // buffer under the 1600 hard limit
        var body = text;
        if (body.Length > MaxWhatsAppLength)
        {
            var truncated = body[..MaxWhatsAppLength];
            var lastNewline = truncated.LastIndexOf('\n');
            if (lastNewline > MaxWhatsAppLength / 2) truncated = truncated[..lastNewline];
            body = truncated + "\n\n... (message trimmed — full details on dashboard)";
        }

        var message = await MessageResource.CreateAsync(
            from: new PhoneNumber(from),
            to: new PhoneNumber(toNumber),
            body: body);

        _logger.LogInformation("Twilio message sent: {Sid}", message.Sid);

        _db.MessageLogs.Add(new MessageLog
        {
            BusinessId = businessId,
            UserId = userId,
            Direction = MessageDirection.Outbound,
            RawMessage = text,
            ProcessingStatus = MessageProcessingStatus.Executed
        });
        await _db.SaveChangesAsync();
    }

    // ─── Update Last Sale ──────────────────────────────────────────────────────
    private async Task<string> HandleUpdateLastSaleAsync(Guid businessId, JsonElement ba, User? recordedBy = null)
    {
        var lastSale = await _db.Sales
            .Include(s => s.Contact)
            .Where(s => s.BusinessId == businessId && s.RecordedByUserId == recordedBy!.Id
                && s.CreatedAtUtc >= DateTime.UtcNow.AddMinutes(-30))
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (lastSale == null) return "No recent sale found. You can only update your own sales within 30 minutes.";

        var changes = new List<string>();

        var newStatus = ba.GetStringOrNull("paymentStatus");
        if (!string.IsNullOrEmpty(newStatus) && Enum.TryParse<PaymentStatus>(newStatus, true, out var ps))
        {
            lastSale.PaymentStatus = ps;
            changes.Add($"status → {ps}");

            if (ps != PaymentStatus.Paid)
            {
                var contactName = ba.GetStringOrNull("contactName");
                if (!string.IsNullOrEmpty(contactName) && lastSale.ContactId == null)
                {
                    var contact = await FindOrCreateContactAsync(businessId, contactName, ContactType.Customer);
                    lastSale.ContactId = contact.Id;
                    changes.Add($"customer → {contact.Name}");
                }
                var cId = lastSale.ContactId;
                if (cId.HasValue && lastSale.TotalAmount > 0)
                {
                    await _ledger.CreateReceivableAsync(businessId, new DTOs.Ledger.CreateReceivableRequest
                    {
                        ContactId = cId.Value, Amount = lastSale.TotalAmount, Notes = "Credit sale (updated)"
                    }, EntrySource.WhatsApp, recordedBy?.Id, recordedBy?.FullName);
                    var cName = lastSale.Contact?.Name ?? ba.GetStringOrNull("contactName") ?? "Customer";
                    changes.Add($"{cName} now owes {_cs}{lastSale.TotalAmount:N0}");
                }
            }
        }

        var contactOnly = ba.GetStringOrNull("contactName");
        if (!string.IsNullOrEmpty(contactOnly) && string.IsNullOrEmpty(newStatus))
        {
            var contact = await FindOrCreateContactAsync(businessId, contactOnly, ContactType.Customer);
            lastSale.ContactId = contact.Id;
            changes.Add($"customer → {contact.Name}");
        }

        var newMethod = ba.GetStringOrNull("paymentMethod");
        if (!string.IsNullOrEmpty(newMethod))
        {
            lastSale.PaymentMethod = newMethod;
            changes.Add($"payment method → {newMethod}");
        }

        if (changes.Count == 0) return "Nothing to update. Specify what to change (e.g. 'that was on credit' or 'add Ama to that').";

        await _db.SaveChangesAsync();
        return $"✅ Last sale updated: {string.Join(", ", changes)}";
    }

    // ─── Undo Last Action ──────────────────────────────────────────────────────
    private async Task<string> HandleUndoLastActionAsync(Guid businessId, User? recordedBy = null)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-30);

        var lastSale = await _db.Sales
            .Where(s => s.BusinessId == businessId && s.RecordedByUserId == recordedBy!.Id && s.CreatedAtUtc >= cutoff)
            .OrderByDescending(s => s.CreatedAtUtc).FirstOrDefaultAsync();

        var lastExpense = await _db.Expenses
            .Where(e => e.BusinessId == businessId && e.RecordedByUserId == recordedBy!.Id && e.CreatedAtUtc >= cutoff)
            .OrderByDescending(e => e.CreatedAtUtc).FirstOrDefaultAsync();

        var lastInvTx = await _db.InventoryTransactions
            .Include(t => t.Product)
            .Where(t => t.BusinessId == businessId && t.RecordedByUserId == recordedBy!.Id && t.CreatedAtUtc >= cutoff)
            .OrderByDescending(t => t.CreatedAtUtc).FirstOrDefaultAsync();

        var candidates = new List<(DateTime Time, string Type, object Entity)>();
        if (lastSale != null) candidates.Add((lastSale.CreatedAtUtc, "sale", lastSale));
        if (lastExpense != null) candidates.Add((lastExpense.CreatedAtUtc, "expense", lastExpense));
        if (lastInvTx != null) candidates.Add((lastInvTx.CreatedAtUtc, "inventory", lastInvTx));

        if (candidates.Count == 0) return "No recent action found in the last 30 minutes to undo.";

        var most = candidates.OrderByDescending(c => c.Time).First();

        if (most.Type == "sale")
        {
            var sale = (Sale)most.Entity;
            await _sales.VoidAsync(businessId, sale.Id, recordedBy?.Id, recordedBy?.FullName);
            return $"✅ Last sale ({_cs}{sale.TotalAmount:N0}) has been voided. Stock restored.";
        }

        if (most.Type == "expense")
        {
            var expense = (Expense)most.Entity;
            await _expenses.DeleteAsync(businessId, expense.Id);
            return $"✅ Last expense ({_cs}{expense.Amount:N0} — {expense.Category}) has been deleted.";
        }

        if (most.Type == "inventory")
        {
            var tx = (InventoryTransaction)most.Entity;
            var product = tx.Product;
            if (tx.Type == InventoryTransactionType.StockIn)
            {
                if (product.CurrentStock >= tx.Quantity)
                {
                    product.CurrentStock -= tx.Quantity;
                    _db.InventoryTransactions.Remove(tx);
                    await _db.SaveChangesAsync();
                    return $"✅ Undone: {tx.Quantity:0.##} {product.Unit} of {product.Name} removed. Stock: {product.CurrentStock:0.##}";
                }
                return $"Can't undo — some of the {tx.Quantity:0.##} {product.Unit} has already been sold.";
            }
            else
            {
                product.CurrentStock += tx.Quantity;
                _db.InventoryTransactions.Remove(tx);
                await _db.SaveChangesAsync();
                return $"✅ Undone: {tx.Quantity:0.##} {product.Unit} of {product.Name} restored. Stock: {product.CurrentStock:0.##}";
            }
        }

        return "Couldn't determine what to undo.";
    }

    // ─── Stocktake ─────────────────────────────────────────────────────────────
    private async Task<string> HandleStocktakeAsync(Guid businessId, JsonElement ba, User? recordedBy = null)
    {
        if (!ba.TryGetProperty("items", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Array || itemsEl.GetArrayLength() == 0)
            return "List what you counted, e.g. 'I counted 15 rice, 8 beans, 20 oil'";

        var results = new List<string>();
        var errors = new List<string>();

        foreach (var item in itemsEl.EnumerateArray())
        {
            var name = item.GetStringOrNull("productName");
            var actualCount = item.GetDecimalOrNull("actualCount");
            if (string.IsNullOrEmpty(name) || !actualCount.HasValue) continue;

            var (product, err) = await FindProductAsync(businessId, name);
            if (product == null) { errors.Add($"{name} (not found)"); continue; }

            var diff = actualCount.Value - product.CurrentStock;
            if (diff == 0) { results.Add($"• {product.Name}: ✓ matches ({product.CurrentStock:0.##} {product.Unit})"); continue; }

            await _inventory.AdjustAsync(businessId, new DTOs.Inventory.AdjustmentRequest
            {
                ProductId = product.Id,
                NewQuantity = actualCount.Value,
                Notes = $"Stocktake: was {product.CurrentStock:0.##}, counted {actualCount.Value:0.##}"
            }, recordedBy?.Id, recordedBy?.FullName);

            var arrow = diff > 0 ? "↑" : "↓";
            results.Add($"• {product.Name}: {product.CurrentStock:0.##} → {actualCount.Value:0.##} {product.Unit} ({arrow}{Math.Abs(diff):0.##})");
        }

        if (results.Count == 0 && errors.Count > 0) return $"❌ Couldn't match any products: {string.Join(", ", errors)}";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"✅ *Stocktake Complete* ({results.Count} products adjusted)");
        sb.AppendLine(string.Join("\n", results));
        if (errors.Count > 0) sb.AppendLine($"\n⚠️ Skipped: {string.Join(", ", errors)}");
        return sb.ToString().TrimEnd();
    }

    // ─── Week Comparison ───────────────────────────────────────────────────────
    private async Task<string> HandleGetWeekComparisonAsync(Guid businessId)
    {
        var today = DateTime.UtcNow.Date;
        var thisWeekStart = today.AddDays(-(int)today.DayOfWeek + 1);
        if (today.DayOfWeek == DayOfWeek.Sunday) thisWeekStart = thisWeekStart.AddDays(-7);
        var lastWeekStart = thisWeekStart.AddDays(-7);

        var thisWeekSales = await _db.Sales
            .Where(s => s.BusinessId == businessId && s.CreatedAtUtc >= thisWeekStart)
            .SumAsync(s => s.TotalAmount);
        var lastWeekSales = await _db.Sales
            .Where(s => s.BusinessId == businessId && s.CreatedAtUtc >= lastWeekStart && s.CreatedAtUtc < thisWeekStart)
            .SumAsync(s => s.TotalAmount);

        var thisWeekExpenses = await _db.Expenses
            .Where(e => e.BusinessId == businessId && e.CreatedAtUtc >= thisWeekStart)
            .SumAsync(e => e.Amount);
        var lastWeekExpenses = await _db.Expenses
            .Where(e => e.BusinessId == businessId && e.CreatedAtUtc >= lastWeekStart && e.CreatedAtUtc < thisWeekStart)
            .SumAsync(e => e.Amount);

        var thisNet = thisWeekSales - thisWeekExpenses;
        var lastNet = lastWeekSales - lastWeekExpenses;

        var salesChange = lastWeekSales > 0 ? ((thisWeekSales - lastWeekSales) / lastWeekSales * 100) : 0;
        var salesArrow = salesChange > 0 ? "📈" : salesChange < 0 ? "📉" : "➡️";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("📊 *This Week vs Last Week*\n");
        sb.AppendLine($"*Sales*");
        sb.AppendLine($"This week: {_cs}{thisWeekSales:N0}");
        sb.AppendLine($"Last week: {_cs}{lastWeekSales:N0}");
        sb.AppendLine($"{salesArrow} {(salesChange >= 0 ? "+" : "")}{salesChange:0.#}%\n");
        sb.AppendLine($"*Expenses*");
        sb.AppendLine($"This week: {_cs}{thisWeekExpenses:N0}");
        sb.AppendLine($"Last week: {_cs}{lastWeekExpenses:N0}\n");
        sb.AppendLine($"*Net*");
        sb.AppendLine($"This week: {_cs}{thisNet:N0}");
        sb.AppendLine($"Last week: {_cs}{lastNet:N0}");

        return sb.ToString().TrimEnd();
    }

    // ─── Product Profit ────────────────────────────────────────────────────────
    private async Task<string> HandleGetProductProfitAsync(Guid businessId, JsonElement ba)
    {
        var productName = ba.GetStringOrNull("productName");
        if (string.IsNullOrEmpty(productName)) return "Which product? E.g. 'Am I making money on rice?'";

        var (product, err) = await FindProductAsync(businessId, productName);
        if (product == null) return err!;

        if (!product.CostPrice.HasValue) return $"No cost price set for {product.Name}. Add a cost price to see profitability.";

        var thirtyDaysAgo = DateTime.UtcNow.Date.AddDays(-29);
        var saleItems = await _db.SaleItems
            .Include(i => i.Sale)
            .Where(i => i.Sale.BusinessId == businessId && i.ProductId == product.Id && i.Sale.CreatedAtUtc >= thirtyDaysAgo)
            .ToListAsync();

        if (saleItems.Count == 0) return $"No sales of {product.Name} in the last 30 days.";

        var revenue = saleItems.Sum(i => i.TotalPrice);
        var cost = saleItems.Sum(i => i.Quantity) * product.CostPrice.Value;
        var profit = revenue - cost;
        var margin = revenue > 0 ? profit / revenue * 100 : 0;
        var totalQty = saleItems.Sum(i => i.Quantity);

        var emoji = profit > 0 ? "✅" : "❌";
        return $"{emoji} *{product.Name} — 30 Day Profit*\n\nSold: {totalQty:0.##} {product.Unit}\nRevenue: {_cs}{revenue:N0}\nCost: {_cs}{cost:N0}\nProfit: {_cs}{profit:N0}\nMargin: {margin:0.#}%";
    }

    // ─── Plan Queries ───────────────────────────────────────────────────────────
    private static string HandleGetMyAccount(User user) =>
        $"👤 *Your Account*\n\n" +
        $"🏪 Business: *{user.Business.Name}*\n" +
        $"👤 Name: *{user.FullName}*\n" +
        $"📞 Phone: {user.PhoneNumber}\n" +
        $"🏷️ Role: {user.Role}\n" +
        (!string.IsNullOrEmpty(user.Email) ? $"📧 Email: {user.Email}\n" : "") +
        $"\nManage your account at app.bizpilot-ai.com/settings";

    private async Task<string> HandleGetMyPlanAsync(Guid businessId)
    {
        var biz = await _db.Businesses.FindAsync(businessId);
        if (biz == null) return "Business not found.";

        var plan = Common.PlanLimits.Get(biz.Plan);
        var planLabel = biz.Plan[0..1].ToUpper() + biz.Plan[1..];
        var trial = Common.PlanGuard.GetTrialStatus(biz);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"📋 *Your Plan: {planLabel}*\n");

        if (trial == Common.TrialStatus.Active && biz.TrialEndsAt.HasValue)
        {
            var days = Math.Max(0, (int)Math.Ceiling((biz.TrialEndsAt.Value - DateTime.UtcNow).TotalDays));
            sb.AppendLine($"🆓 Free trial — {days} day{(days != 1 ? "s" : "")} left");
        }
        else if (trial == Common.TrialStatus.GracePeriod)
            sb.AppendLine("⚠️ Trial ended — grace period active");
        else if (trial == Common.TrialStatus.Expired)
            sb.AppendLine("🔒 Trial expired — subscribe to keep access");
        else if (Common.PlanGuard.IsSubscriber(biz))
            sb.AppendLine("✅ Active subscription");

        if (plan.PricePerMonth > 0) sb.AppendLine($"💰 {_cs}{plan.PricePerMonth:N0}/month");
        sb.AppendLine($"\n*Features:*");
        sb.AppendLine($"• {(plan.MaxProducts < 0 ? "Unlimited" : plan.MaxProducts.ToString())} products");
        sb.AppendLine($"• {(plan.MaxMessagesPerMonth < 0 ? "Unlimited" : plan.MaxMessagesPerMonth.ToString())} messages/month");
        sb.AppendLine($"• {(plan.MaxStaff < 0 ? "Unlimited" : plan.MaxStaff.ToString())} user{(plan.MaxStaff != 1 ? "s" : "")}");
        if (plan.HasLedger) sb.AppendLine("• Ledger (credits & debts)");
        if (plan.HasStockHolds) sb.AppendLine("• Stock holds");
        if (plan.HasCsvImport) sb.AppendLine("• CSV import");
        if (plan.HasAdvancedReports) sb.AppendLine("• Advanced reports & charts");

        if (!string.IsNullOrEmpty(biz.PendingPlanChange))
        {
            var pendingLabel = biz.PendingPlanChange[0..1].ToUpper() + biz.PendingPlanChange[1..];
            var switchDate = biz.SubscriptionEndsAt?.ToString("dd MMM yyyy") ?? "end of billing period";
            sb.AppendLine($"\n⏳ Switching to *{pendingLabel}* on {switchDate}. Say *cancel plan change* to stay on {planLabel}.");
        }

        if (biz.SubscriptionEndsAt.HasValue && !string.IsNullOrEmpty(biz.PaystackSubscriptionCode))
            sb.AppendLine($"\n📅 Renews: {biz.SubscriptionEndsAt.Value:dd MMM yyyy}");

        sb.AppendLine("\nSay *plans* to compare all available plans.");
        return sb.ToString().TrimEnd();
    }

    private static string HandleGetPlans()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("📊 *BizPilot Plans*\n");

        sb.AppendLine("*Starter* — {_cs}3,500/month");
        sb.AppendLine("30 products, 150 messages, 1 user");
        sb.AppendLine("WhatsApp bot, daily summaries, basic dashboard\n");

        sb.AppendLine("*Shop* — {_cs}7,500/month");
        sb.AppendLine("Unlimited products, 850 messages, 4 users");
        sb.AppendLine("Everything in Starter + ledger, stock holds\n");

        sb.AppendLine("*Pro* — {_cs}12,500/month");
        sb.AppendLine("Unlimited products & messages, 11 users");
        sb.AppendLine("Everything in Shop + CSV import, advanced reports\n");

        sb.AppendLine("*Business* — {_cs}30,000/month");
        sb.AppendLine("Unlimited everything + multi-branch, API access\n");

        sb.AppendLine("Say *subscribe to [plan]* to get started, or manage at app.bizpilot-ai.com/settings");
        return sb.ToString().TrimEnd();
    }

    private async Task<string> HandleSubscribeAsync(Guid businessId, JsonElement ba, User? user)
    {
        var plan = ba.GetStringOrNull("plan");
        if (string.IsNullOrEmpty(plan))
            return "Which plan would you like? Say *subscribe to starter*, *shop*, *pro*, or *business*.";

        var confirmed = ba.GetStringOrNull("confirmed") == "true";

        var business = await _db.Businesses.FindAsync(businessId);
        if (business == null) return "Business not found.";

        if (!business.IsBillable)
            return "Your account doesn't require a subscription.";

        var planConfig = Common.PlanLimits.Get(plan);
        if (planConfig.PricePerMonth <= 0)
            return "Invalid plan name. Available plans: Starter, Shop, Pro, Business.";

        if (business.SubscribedPlan?.ToLower() == plan.ToLower() && string.IsNullOrEmpty(business.PendingPlanChange))
            return $"You're already subscribed to the {plan[0..1].ToUpper() + plan[1..]} plan.";

        var planLabel = plan[0..1].ToUpper() + plan[1..];
        var isDowngrade = Common.PlanGuard.PlanRank(plan) < Common.PlanGuard.PlanRank(business.SubscribedPlan);
        var hasActiveSub = !string.IsNullOrEmpty(business.PaystackSubscriptionCode);

        // Downgrade with active subscription — schedule for end of cycle
        if (isDowngrade && hasActiveSub)
        {
            if (!confirmed)
            {
                var endsAt = business.SubscriptionEndsAt?.ToString("dd MMM yyyy") ?? "end of billing period";
                return $"You want to switch from *{business.SubscribedPlan![0..1].ToUpper() + business.SubscribedPlan[1..]}* to *{planLabel}* ({_cs}{planConfig.PricePerMonth:N0}/month).\n\n" +
                       $"Your current plan will stay active until *{endsAt}*, then you'll be switched to {planLabel}.\n\n" +
                       $"Reply *yes* to confirm, or *no* to keep your current plan.";
            }

            business.PendingPlanChange = plan.ToLower();
            await _db.SaveChangesAsync();

            var switchDate = business.SubscriptionEndsAt?.ToString("dd MMM yyyy") ?? "end of your billing period";
            return $"✅ Plan change scheduled.\n\nYou'll keep your current features until *{switchDate}*, then switch to *{planLabel}* ({_cs}{planConfig.PricePerMonth:N0}/month).\n\nTo cancel this change, say *cancel plan change*.";
        }

        // Upgrade or new subscription — go to payment
        if (!confirmed)
        {
            var action = hasActiveSub ? "upgrade" : "subscribe";
            return $"You're about to {action} to *{planLabel}* at *{_cs}{planConfig.PricePerMonth:N0}/month*.\n\n" +
                   $"Reply *yes* to proceed with payment, or *no* to cancel.";
        }

        try
        {
            // If upgrading from an active subscription, cancel the old one first
            if (hasActiveSub)
            {
                try
                {
                    var paystack2 = _serviceProvider.GetRequiredService<PaystackService>();
                    await paystack2.CancelSubscriptionAsync(businessId);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to cancel old subscription during upgrade"); }
            }

            var email = user?.Email ?? $"{user?.PhoneNumber}@bizpilot-ai.com";
            var paystack = _serviceProvider.GetRequiredService<PaystackService>();
            var url = await paystack.InitializeSubscriptionAsync(businessId, plan, email);

            business.PendingPlanChange = null;
            await _db.SaveChangesAsync();

            return $"💳 *Subscribe to {planLabel}* — {_cs}{planConfig.PricePerMonth:N0}/month\n\n" +
                   $"Complete your payment here:\n{url}\n\n" +
                   $"Your plan will activate immediately after payment.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize subscription for {Business}", business.Name);
            return "Sorry, couldn't set up payment right now. Please try again or visit app.bizpilot-ai.com/settings to subscribe.";
        }
    }

    private async Task<string> HandleCancelSubscriptionAsync(Guid businessId, JsonElement ba)
    {
        var confirmed = ba.GetStringOrNull("confirmed") == "true";

        var business = await _db.Businesses.FindAsync(businessId);
        if (business == null) return "Business not found.";

        if (string.IsNullOrEmpty(business.PaystackSubscriptionCode))
            return "You don't have an active subscription to cancel.";

        if (!confirmed)
        {
            var planLabel = business.Plan[0..1].ToUpper() + business.Plan[1..];
            var endsAt = business.SubscriptionEndsAt?.ToString("dd MMM yyyy") ?? "end of your billing period";
            return $"⚠️ Are you sure you want to cancel your *{planLabel}* subscription?\n\n" +
                   $"You'll keep access until *{endsAt}*, then your plan will be downgraded.\n\n" +
                   $"Reply *yes* to confirm cancellation, or *no* to keep your subscription.";
        }

        try
        {
            var paystack = _serviceProvider.GetRequiredService<PaystackService>();
            await paystack.CancelSubscriptionAsync(businessId);

            var endsAt = business.SubscriptionEndsAt?.ToString("dd MMM yyyy") ?? "the end of your billing period";
            return $"✅ Subscription cancelled.\n\nYou'll keep access to your {business.Plan[0..1].ToUpper() + business.Plan[1..]} features until *{endsAt}*.\n\nYou can resubscribe anytime by saying *subscribe to {business.Plan}*.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel subscription for {Business}", business.Name);
            return "Sorry, couldn't cancel right now. Please try again or visit app.bizpilot-ai.com/settings.";
        }
    }

    private async Task<string> HandleCancelPlanChangeAsync(Guid businessId)
    {
        var business = await _db.Businesses.FindAsync(businessId);
        if (business == null) return "Business not found.";

        if (string.IsNullOrEmpty(business.PendingPlanChange))
            return "You don't have a pending plan change to cancel.";

        var was = business.PendingPlanChange[0..1].ToUpper() + business.PendingPlanChange[1..];
        business.PendingPlanChange = null;
        await _db.SaveChangesAsync();

        return $"✅ Plan change cancelled. You'll stay on your current *{business.Plan[0..1].ToUpper() + business.Plan[1..]}* plan. The scheduled switch to {was} has been removed.";
    }

    // ─── Stock Value ───────────────────────────────────────────────────────────
    private async Task<string> HandleGetStockValueAsync(Guid businessId)
    {
        var products = await _db.Products
            .Where(p => p.BusinessId == businessId && p.IsActive && p.CurrentStock > 0)
            .ToListAsync();

        if (products.Count == 0) return "No products in stock.";

        var totalCostValue = 0m;
        var totalSellValue = 0m;
        var noCost = 0;
        var noSell = 0;

        foreach (var p in products)
        {
            if (p.CostPrice.HasValue) totalCostValue += p.CurrentStock * p.CostPrice.Value;
            else noCost++;
            if (p.SellingPrice.HasValue) totalSellValue += p.CurrentStock * p.SellingPrice.Value;
            else noSell++;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"📦 *Inventory Value* ({products.Count} products in stock)\n");
        if (totalCostValue > 0) sb.AppendLine($"Cost value: {_cs}{totalCostValue:N0}");
        if (totalSellValue > 0) sb.AppendLine($"Selling value: {_cs}{totalSellValue:N0}");
        if (totalCostValue > 0 && totalSellValue > 0)
            sb.AppendLine($"Potential profit: {_cs}{(totalSellValue - totalCostValue):N0}");
        if (noCost > 0) sb.AppendLine($"\n⚠️ {noCost} product{(noCost != 1 ? "s" : "")} missing cost price");
        if (noSell > 0) sb.AppendLine($"⚠️ {noSell} product{(noSell != 1 ? "s" : "")} missing selling price");

        return sb.ToString().TrimEnd();
    }

    // ─── Repeat Last Sale ──────────────────────────────────────────────────────
    private async Task<string> HandleRepeatLastSaleAsync(Guid businessId, User? recordedBy = null)
    {
        var lastSale = await _db.Sales
            .Include(s => s.Items).ThenInclude(i => i.Product)
            .Include(s => s.Contact)
            .Where(s => s.BusinessId == businessId && s.RecordedByUserId == recordedBy!.Id)
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (lastSale == null) return "No previous sale to repeat.";

        var saleItems = new List<SaleItemRequest>();
        var skipped = new List<string>();

        foreach (var item in lastSale.Items)
        {
            var product = await _db.Products.FindAsync(item.ProductId);
            if (product == null || !product.IsActive) { skipped.Add($"{item.Product.Name} (deleted)"); continue; }
            if (product.CurrentStock < item.Quantity) { skipped.Add($"{product.Name} (only {product.CurrentStock:0.##} {product.Unit})"); continue; }

            saleItems.Add(new SaleItemRequest { ProductId = product.Id, Quantity = item.Quantity, UnitPrice = item.UnitPrice });
        }

        if (saleItems.Count == 0) return $"Can't repeat — not enough stock for any items from the last sale.";

        var sale = await _sales.CreateAsync(businessId, new CreateSaleRequest
        {
            Items = saleItems,
            ContactId = lastSale.ContactId,
            PaymentStatus = lastSale.PaymentStatus,
            PaymentMethod = lastSale.PaymentMethod
        }, EntrySource.WhatsApp, recordedBy?.Id, recordedBy?.FullName);

        var lines = sale.Items.Select(i => $"• {i.Quantity} {i.Unit} of {i.ProductName} @ {_cs}{i.UnitPrice:N0} = {_cs}{i.TotalPrice:N0}");
        var skippedNote = skipped.Count > 0 ? $"\n\n⚠️ Skipped: {string.Join(", ", skipped)}" : "";
        return $"✅ Sale repeated!\n{string.Join("\n", lines)}\n\n*Total: {_cs}{sale.TotalAmount:N0}* ({sale.PaymentStatus}){skippedNote}";
    }

    // ─── Add to Last Sale ──────────────────────────────────────────────────────
    private async Task<string> HandleAddToLastSaleAsync(Guid businessId, JsonElement ba, User? recordedBy = null)
    {
        var productName = ba.GetStringOrNull("productName");
        var qty = ba.GetDecimalOrNull("quantity");
        if (string.IsNullOrEmpty(productName) || !qty.HasValue || qty.Value <= 0)
            return "What product and how many should I add? E.g. 'Add 3 rice to that'";

        var lastSale = await _db.Sales
            .Include(s => s.Items).ThenInclude(i => i.Product)
            .Where(s => s.BusinessId == businessId && s.RecordedByUserId == recordedBy!.Id
                && s.CreatedAtUtc >= DateTime.UtcNow.AddMinutes(-30))
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (lastSale == null) return "No recent sale found. You can only add to your own sales within 30 minutes.";

        var (product, err) = await FindProductAsync(businessId, productName);
        if (product == null) return err!;
        if (product.CurrentStock < qty.Value) return $"❌ Not enough stock for {product.Name}. You have {product.CurrentStock} {product.Unit} available.";

        var unitPrice = ba.GetDecimalOrNull("unitPrice") ?? product.SellingPrice ?? 0;
        if (unitPrice <= 0) return $"No selling price set for {product.Name}. Please specify a price.";

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var saleItem = new SaleItem
            {
                SaleId = lastSale.Id,
                ProductId = product.Id,
                Quantity = qty.Value,
                UnitPrice = unitPrice,
                TotalPrice = qty.Value * unitPrice
            };
            _db.SaleItems.Add(saleItem);

            product.CurrentStock -= qty.Value;

            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                BusinessId = businessId,
                ProductId = product.Id,
                Type = InventoryTransactionType.StockOut,
                Quantity = qty.Value,
                Notes = $"Added to sale {lastSale.Id}",
                RecordedByUserId = recordedBy?.Id,
                RecordedByName = recordedBy?.FullName
            });

            await _db.SaveChangesAsync();

            // Recalculate total from all items (avoids lost updates from concurrent adds)
            var newTotal = await _db.SaleItems
                .Where(i => i.SaleId == lastSale.Id)
                .SumAsync(i => i.TotalPrice);
            lastSale.TotalAmount = newTotal;
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return $"✅ Added {qty.Value:0.##} {product.Unit} of {product.Name} @ {_cs}{unitPrice:N0} to last sale.\nNew total: {_cs}{lastSale.TotalAmount:N0}";
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync();
            return "Couldn't add — the sale or stock was modified by another action. Please try again.";
        }
    }
}

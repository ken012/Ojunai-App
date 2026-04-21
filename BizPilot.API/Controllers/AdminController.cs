using BizPilot.API.Data;
using BizPilot.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BizPilot.API.Controllers;

[AllowAnonymous]
[Route("api/admin")]
[ApiController]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public AdminController(AppDbContext db, IConfiguration config) { _db = db; _config = config; }

    [HttpGet("onboarding-analytics")]
    public async Task<IActionResult> GetOnboardingAnalytics([FromQuery] string key)
    {
        var secret = _config["Admin:AnalyticsKey"];
        if (string.IsNullOrEmpty(secret) || secret.Length < 32) return StatusCode(503, "Admin endpoint not configured.");
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes((key ?? "").ToLowerInvariant()),
            System.Text.Encoding.UTF8.GetBytes(secret.ToLowerInvariant()))) return Unauthorized();

        var logs = await _db.MessageLogs
            .Where(m => m.ParsedIntent != null && m.ParsedIntent.StartsWith("onboarding:"))
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(500)
            .Select(m => new { m.ParsedIntent, m.RawMessage, m.CreatedAtUtc })
            .ToListAsync();

        var funnel = logs
            .GroupBy(l => l.ParsedIntent)
            .Select(g => new { Step = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToList();

        var activeFlowsRaw = await _db.OnboardingStates
            .OrderByDescending(s => s.LastActivityUtc)
            .Select(s => new
            {
                s.PhoneNumber,
                Step = s.Step.ToString(),
                s.BusinessName,
                s.BusinessType,
                s.City,
                s.OwnerName,
                s.CreatedAtUtc,
                s.LastActivityUtc
            })
            .ToListAsync();

        var activeFlows = activeFlowsRaw.Select(s => new
        {
            PhoneNumber = RedactPhone(s.PhoneNumber),
            s.Step,
            s.BusinessName,
            s.BusinessType,
            s.City,
            s.OwnerName,
            s.CreatedAtUtc,
            s.LastActivityUtc
        }).ToList();

        var recentSignupsRaw = await _db.Businesses
            .Include(b => b.Users)
            .Where(b => b.CreatedAtUtc >= DateTime.UtcNow.AddDays(-30))
            .OrderByDescending(b => b.CreatedAtUtc)
            .Take(50)
            .Select(b => new
            {
                b.Name,
                b.BusinessType,
                b.City,
                Owner = b.Users.Where(u => u.Role == UserRole.Owner).Select(u => u.FullName).FirstOrDefault(),
                Phone = b.Users.Where(u => u.Role == UserRole.Owner).Select(u => u.PhoneNumber).FirstOrDefault(),
                b.Plan,
                b.TrialEndsAt,
                b.CreatedAtUtc
            })
            .ToListAsync();

        var recentSignups = recentSignupsRaw.Select(s => new
        {
            s.Name,
            s.BusinessType,
            s.City,
            s.Owner,
            Phone = RedactPhone(s.Phone),
            s.Plan,
            s.TrialEndsAt,
            s.CreatedAtUtc
        }).ToList();

        return Ok(new { funnel, activeFlows, recentSignups });
    }

    private static string RedactPhone(string? phone)
    {
        if (string.IsNullOrEmpty(phone)) return "—";
        if (phone.Length <= 4) return phone;
        return new string('*', phone.Length - 4) + phone[^4..];
    }

    /// <summary>
    /// Shared admin-key validation. Returns null on success, or the action result to short-circuit with.
    /// </summary>
    private IActionResult? ValidateAdminKey(string? key)
    {
        var secret = _config["Admin:AnalyticsKey"];
        if (string.IsNullOrEmpty(secret) || secret.Length < 32)
            return StatusCode(503, "Admin endpoint not configured.");
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes((key ?? "").ToLowerInvariant()),
            System.Text.Encoding.UTF8.GetBytes(secret.ToLowerInvariant()))) return Unauthorized();
        return null;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // DATA WIPE
    // ═════════════════════════════════════════════════════════════════════════════

    [HttpGet("wipe-inventory-expenses")]
    public async Task<IActionResult> WipeInventoryExpenses([FromQuery] string key, [FromQuery] Guid businessId)
    {
        var auth = ValidateAdminKey(key);
        if (auth != null) return auth;

        var biz = await _db.Businesses.FindAsync(businessId);
        if (biz == null) return NotFound(new { error = "Business not found" });

        var saleItemCount = await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"SaleItems\" WHERE \"SaleId\" IN (SELECT \"Id\" FROM \"Sales\" WHERE \"BusinessId\" = {0})", businessId);
        var saleCount = await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"Sales\" WHERE \"BusinessId\" = {0}", businessId);
        var expenseCount = await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"Expenses\" WHERE \"BusinessId\" = {0}", businessId);
        var invTxCount = await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"InventoryTransactions\" WHERE \"BusinessId\" = {0}", businessId);
        var holdCount = await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"StockHolds\" WHERE \"BusinessId\" = {0}", businessId);
        var ledgerCount = await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"LedgerEntries\" WHERE \"BusinessId\" = {0}", businessId);
        var productCount = await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"Products\" WHERE \"BusinessId\" = {0}", businessId);
        var contactCount = await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"Contacts\" WHERE \"BusinessId\" = {0}", businessId);

        return Ok(new
        {
            business = biz.Name,
            wiped = new
            {
                saleItems = saleItemCount,
                sales = saleCount,
                expenses = expenseCount,
                inventoryTransactions = invTxCount,
                stockHolds = holdCount,
                ledgerEntries = ledgerCount,
                products = productCount,
                contacts = contactCount,
            }
        });
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // PRODUCT RECATEGORIZATION
    // ═════════════════════════════════════════════════════════════════════════════

    [HttpGet("recategorize-products")]
    public async Task<IActionResult> RecategorizeProducts(
        [FromQuery] string key,
        [FromQuery] Guid? businessId = null,
        [FromQuery] bool aggressive = false)
    {
        var auth = ValidateAdminKey(key);
        if (auth != null) return auth;

        var query = _db.Products.Where(p => p.IsActive);
        if (businessId.HasValue)
            query = query.Where(p => p.BusinessId == businessId.Value);

        var products = await query.ToListAsync();
        var changes = new List<object>();
        var recategorized = 0;

        foreach (var product in products)
        {
            // Safe mode: only touch uncategorized or obviously wrong categories
            if (!aggressive && product.Category != null
                && product.Category != "Uncategorized"
                && product.Category != "General / Other")
                continue;

            var (newCat, newSub) = Common.CategoryInferrer.Infer(product.Name);
            if (newCat == null) continue;

            var oldCat = product.Category ?? "Uncategorized";
            var oldSub = product.Subcategory;

            if (newCat == oldCat && newSub == oldSub) continue;

            changes.Add(new
            {
                product = product.Name,
                from = oldSub != null ? $"{oldCat} / {oldSub}" : oldCat,
                to = newSub != null ? $"{newCat} / {newSub}" : newCat,
            });

            product.Category = newCat;
            product.Subcategory = newSub;
            recategorized++;
        }

        if (recategorized > 0)
            await _db.SaveChangesAsync();

        return Ok(new
        {
            mode = aggressive ? "aggressive" : "safe",
            total = products.Count,
            recategorized,
            unchanged = products.Count - recategorized,
            changes,
        });
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // BILLING ADMIN — subscription overview and event audit log
    // ═════════════════════════════════════════════════════════════════════════════

    [HttpGet("billing-overview")]
    public async Task<IActionResult> BillingOverview([FromQuery] string key)
    {
        var auth = ValidateAdminKey(key);
        if (auth != null) return auth;

        var now = DateTime.UtcNow;
        var businesses = await _db.Businesses
            .Where(b => b.IsActive && b.SubscribedPlan != null && b.SubscribedPlan != "starter")
            .ToListAsync();

        var result = new
        {
            totalActiveSubscribers = businesses.Count(b => b.SubscriptionEndsAt > now || !string.IsNullOrEmpty(b.PaystackSubscriptionCode) || !string.IsNullOrEmpty(b.FlutterwaveSubscriptionId)),
            byPlan = businesses.GroupBy(b => b.Plan).Select(g => new { plan = g.Key, count = g.Count() }),
            byProvider = businesses.GroupBy(b => b.BillingProvider).Select(g => new { provider = g.Key, count = g.Count() }),
            byCurrency = businesses.GroupBy(b => b.BillingCurrency).Select(g => new { currency = g.Key, count = g.Count() }),
            byCycle = businesses.GroupBy(b => b.BillingCycle).Select(g => new { cycle = g.Key, count = g.Count() }),
            autoRenew = businesses.Count(b => b.IsAutoRenew),
            manualRenew = businesses.Count(b => !b.IsAutoRenew),
            expiringIn7Days = businesses.Count(b => b.SubscriptionEndsAt.HasValue && b.SubscriptionEndsAt > now && b.SubscriptionEndsAt < now.AddDays(7)),
            inGrace = businesses.Count(b => b.SubscriptionStatus == "grace" || (b.SubscriptionEndsAt.HasValue && b.SubscriptionEndsAt < now && b.SubscriptionEndsAt.Value.AddDays(3) > now)),
            pastDue = businesses.Count(b => b.SubscriptionStatus == "past_due"),
        };

        return Ok(result);
    }

    [HttpGet("billing-events")]
    public async Task<IActionResult> BillingEvents(
        [FromQuery] string key,
        [FromQuery] Guid? businessId = null,
        [FromQuery] string? eventType = null,
        [FromQuery] int days = 7,
        [FromQuery] int limit = 50)
    {
        var auth = ValidateAdminKey(key);
        if (auth != null) return auth;

        var since = DateTime.UtcNow.AddDays(-days);
        var query = _db.BillingEvents
            .Where(e => e.CreatedAtUtc >= since)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.Where(e => e.BusinessId == businessId.Value);
        if (!string.IsNullOrEmpty(eventType))
            query = query.Where(e => e.EventType == eventType);

        var events = await query
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(limit)
            .Select(e => new
            {
                e.Id, e.BusinessId, e.EventType, e.Provider, e.Plan,
                e.BillingCycle, e.Amount, e.Currency, e.TransactionRef,
                e.SubscriptionId, e.PaymentMethod, e.Status, e.ErrorDetails,
                e.CreatedAtUtc
            })
            .ToListAsync();

        return Ok(new { count = events.Count, data = events });
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // GROWTH & USAGE METRICS
    // ═════════════════════════════════════════════════════════════════════════════

    [HttpGet("metrics/overview")]
    public async Task<IActionResult> MetricsOverview([FromQuery] string key, [FromQuery] int days = 30)
    {
        var auth = ValidateAdminKey(key);
        if (auth != null) return auth;

        var now = DateTime.UtcNow;
        var since = now.AddDays(-Math.Clamp(days, 1, 365));

        var totalBusinesses = await _db.Businesses.CountAsync(b => b.IsActive);
        var totalUsers = await _db.Users.CountAsync(u => u.IsActive);

        // Daily active businesses (sent at least one WhatsApp message)
        var dauSince = now.AddDays(-1);
        var dailyActive = await _db.MessageLogs
            .Where(m => m.CreatedAtUtc >= dauSince && m.Direction == MessageDirection.Inbound && m.BusinessId.HasValue)
            .Select(m => m.BusinessId).Distinct().CountAsync();

        // Weekly active
        var wauSince = now.AddDays(-7);
        var weeklyActive = await _db.MessageLogs
            .Where(m => m.CreatedAtUtc >= wauSince && m.Direction == MessageDirection.Inbound && m.BusinessId.HasValue)
            .Select(m => m.BusinessId).Distinct().CountAsync();

        // Monthly active
        var mauSince = now.AddDays(-30);
        var monthlyActive = await _db.MessageLogs
            .Where(m => m.CreatedAtUtc >= mauSince && m.Direction == MessageDirection.Inbound && m.BusinessId.HasValue)
            .Select(m => m.BusinessId).Distinct().CountAsync();

        // New signups in period
        var newSignups = await _db.Businesses.CountAsync(b => b.CreatedAtUtc >= since && b.IsActive);

        // Trial conversion: businesses that have SubscribedPlan != null (ever paid)
        var totalTrialStarted = await _db.Businesses.CountAsync(b => b.IsActive);
        var totalConverted = await _db.Businesses.CountAsync(b => b.IsActive && b.SubscribedPlan != null && b.SubscribedPlan != "starter");
        var conversionRate = totalTrialStarted > 0 ? Math.Round((double)totalConverted / totalTrialStarted * 100, 1) : 0;

        // Churn: businesses that downgraded or expired in the period
        var recentChurn = await _db.BillingEvents
            .CountAsync(e => e.CreatedAtUtc >= since && (e.EventType == "subscription.expired" || e.EventType == "subscription.cancelled"));

        return Ok(new
        {
            period = $"Last {days} days",
            totalBusinesses,
            totalUsers,
            dailyActiveBusinesses = dailyActive,
            weeklyActiveBusinesses = weeklyActive,
            monthlyActiveBusinesses = monthlyActive,
            newSignups,
            trialConversion = new { started = totalTrialStarted, converted = totalConverted, rate = $"{conversionRate}%" },
            recentChurnEvents = recentChurn,
        });
    }

    [HttpGet("metrics/revenue")]
    public async Task<IActionResult> RevenueMetrics([FromQuery] string key, [FromQuery] int months = 3)
    {
        var auth = ValidateAdminKey(key);
        if (auth != null) return auth;

        var since = DateTime.UtcNow.AddMonths(-Math.Clamp(months, 1, 12));
        var payments = await _db.BillingEvents
            .Where(e => e.CreatedAtUtc >= since && e.EventType == "payment.success" && e.Amount.HasValue)
            .Select(e => new { e.Amount, e.Currency, e.Plan, e.BillingCycle, e.Provider, e.CreatedAtUtc })
            .ToListAsync();

        // MRR estimate: sum of monthly payments + annual payments / 12
        var mrr = payments
            .Where(p => p.CreatedAtUtc >= DateTime.UtcNow.AddDays(-31))
            .Sum(p => p.BillingCycle == "annual" ? (p.Amount ?? 0) / 12 : (p.Amount ?? 0));

        var totalRevenue = payments.Sum(p => p.Amount ?? 0);

        var byPlan = payments.GroupBy(p => p.Plan)
            .Select(g => new { plan = g.Key, total = g.Sum(p => p.Amount ?? 0), count = g.Count() });

        var byCurrency = payments.GroupBy(p => p.Currency)
            .Select(g => new { currency = g.Key, total = g.Sum(p => p.Amount ?? 0), count = g.Count() });

        var byProvider = payments.GroupBy(p => p.Provider)
            .Select(g => new { provider = g.Key, total = g.Sum(p => p.Amount ?? 0), count = g.Count() });

        var byMonth = payments.GroupBy(p => p.CreatedAtUtc.ToString("yyyy-MM"))
            .OrderBy(g => g.Key)
            .Select(g => new { month = g.Key, total = g.Sum(p => p.Amount ?? 0), count = g.Count() });

        return Ok(new
        {
            period = $"Last {months} months",
            estimatedMrr = mrr,
            totalRevenue,
            totalPayments = payments.Count,
            byPlan,
            byCurrency,
            byProvider,
            byMonth,
        });
    }

    [HttpGet("metrics/churn")]
    public async Task<IActionResult> ChurnMetrics([FromQuery] string key, [FromQuery] int days = 30)
    {
        var auth = ValidateAdminKey(key);
        if (auth != null) return auth;

        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 365));

        var events = await _db.BillingEvents
            .Where(e => e.CreatedAtUtc >= since &&
                (e.EventType == "subscription.cancelled" || e.EventType == "subscription.expired" || e.EventType == "payment.refunded"))
            .OrderByDescending(e => e.CreatedAtUtc)
            .Select(e => new { e.BusinessId, e.EventType, e.Plan, e.Provider, e.Status, e.CreatedAtUtc })
            .ToListAsync();

        var businessIds = events.Select(e => e.BusinessId).Distinct().ToList();
        var businessNames = await _db.Businesses
            .Where(b => businessIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Name })
            .ToDictionaryAsync(b => b.Id, b => b.Name);

        var enriched = events.Select(e => new
        {
            businessName = businessNames.GetValueOrDefault(e.BusinessId, "Unknown"),
            e.BusinessId, e.EventType, e.Plan, e.Provider, e.Status, e.CreatedAtUtc
        });

        return Ok(new
        {
            period = $"Last {days} days",
            totalEvents = events.Count,
            uniqueBusinesses = businessIds.Count,
            byCancelled = events.Count(e => e.EventType == "subscription.cancelled"),
            byExpired = events.Count(e => e.EventType == "subscription.expired"),
            byRefunded = events.Count(e => e.EventType == "payment.refunded"),
            details = enriched,
        });
    }

    [HttpGet("metrics/top-businesses")]
    public async Task<IActionResult> TopBusinesses([FromQuery] string key, [FromQuery] int days = 30, [FromQuery] int limit = 20)
    {
        var auth = ValidateAdminKey(key);
        if (auth != null) return auth;

        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 365));

        var topByMessages = await _db.MessageLogs
            .Where(m => m.CreatedAtUtc >= since && m.Direction == MessageDirection.Inbound && m.BusinessId.HasValue)
            .GroupBy(m => m.BusinessId)
            .Select(g => new { businessId = g.Key, messageCount = g.Count() })
            .OrderByDescending(g => g.messageCount)
            .Take(limit)
            .ToListAsync();

        var topBySales = await _db.Sales
            .Where(s => s.CreatedAtUtc >= since)
            .GroupBy(s => s.BusinessId)
            .Select(g => new { businessId = g.Key, salesCount = g.Count(), salesTotal = g.Sum(s => s.TotalAmount) })
            .OrderByDescending(g => g.salesTotal)
            .Take(limit)
            .ToListAsync();

        var bizIds = topByMessages.Select(t => t.businessId!.Value)
            .Union(topBySales.Select(t => t.businessId)).Distinct().ToList();
        var names = await _db.Businesses
            .Where(b => bizIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Name, b.Plan, b.Currency, b.Country })
            .ToDictionaryAsync(b => b.Id);

        return Ok(new
        {
            period = $"Last {days} days",
            byMessages = topByMessages.Select(t => new
            {
                business = names.GetValueOrDefault(t.businessId!.Value),
                t.messageCount
            }),
            bySalesVolume = topBySales.Select(t => new
            {
                business = names.GetValueOrDefault(t.businessId),
                t.salesCount,
                t.salesTotal
            }),
        });
    }

    [HttpGet("metrics/message-volume")]
    public async Task<IActionResult> MessageVolume([FromQuery] string key, [FromQuery] int days = 30)
    {
        var auth = ValidateAdminKey(key);
        if (auth != null) return auth;

        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 365));

        var daily = await _db.MessageLogs
            .Where(m => m.CreatedAtUtc >= since && m.Direction == MessageDirection.Inbound)
            .GroupBy(m => m.CreatedAtUtc.Date)
            .Select(g => new { date = g.Key, count = g.Count() })
            .OrderBy(g => g.date)
            .ToListAsync();

        var totalInbound = daily.Sum(d => d.count);
        var avgPerDay = daily.Count > 0 ? Math.Round((double)totalInbound / daily.Count) : 0;

        return Ok(new
        {
            period = $"Last {days} days",
            totalInbound,
            averagePerDay = avgPerDay,
            peakDay = daily.OrderByDescending(d => d.count).FirstOrDefault(),
            daily,
        });
    }

    [HttpGet("metrics/failed-payments")]
    public async Task<IActionResult> FailedPayments([FromQuery] string key, [FromQuery] int days = 30)
    {
        var auth = ValidateAdminKey(key);
        if (auth != null) return auth;

        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 365));

        var events = await _db.BillingEvents
            .Where(e => e.CreatedAtUtc >= since &&
                (e.EventType == "payment.failed" || e.EventType == "payment.rejected" || e.EventType == "reconciliation.past_due"))
            .OrderByDescending(e => e.CreatedAtUtc)
            .Select(e => new { e.BusinessId, e.EventType, e.Plan, e.Provider, e.Amount, e.Currency, e.ErrorDetails, e.CreatedAtUtc })
            .ToListAsync();

        var bizIds = events.Select(e => e.BusinessId).Distinct().ToList();
        var names = await _db.Businesses
            .Where(b => bizIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Name })
            .ToDictionaryAsync(b => b.Id, b => b.Name);

        return Ok(new
        {
            period = $"Last {days} days",
            totalFailed = events.Count,
            byType = events.GroupBy(e => e.EventType).Select(g => new { type = g.Key, count = g.Count() }),
            details = events.Select(e => new
            {
                businessName = names.GetValueOrDefault(e.BusinessId, "Unknown"),
                e.EventType, e.Plan, e.Provider, e.Amount, e.Currency, e.ErrorDetails, e.CreatedAtUtc
            }),
        });
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // TELEMETRY — production observability over MessageLogs
    //
    // These endpoints surface the signals from the principles doc: misparse rate, retry patterns,
    // confidence distribution, and top failing phrasings. Check weekly; use the outputs to drive
    // new corpus entries and prompt improvements.
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Percentage of inbound messages per intent that ended in NeedsClarification or Failed in the
    /// last 7 days. High values for an intent flag where Claude is struggling to parse that category
    /// of messages. Threshold of concern: >5% for any single intent.
    /// </summary>
    [HttpGet("telemetry/misparse-rate")]
    public async Task<IActionResult> GetMisparseRate([FromQuery] string key, [FromQuery] int days = 7)
    {
        var auth = ValidateAdminKey(key);
        if (auth != null) return auth;

        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 90));

        var logs = await _db.MessageLogs
            .Where(m => m.Direction == MessageDirection.Inbound
                        && m.CreatedAtUtc >= since
                        && m.BusinessId != null)
            .Select(m => new { m.ParsedIntent, m.ProcessingStatus })
            .ToListAsync();

        var byIntent = logs
            .GroupBy(l => l.ParsedIntent ?? "unknown")
            .Select(g => new
            {
                Intent = g.Key,
                Total = g.Count(),
                Problems = g.Count(x => x.ProcessingStatus == MessageProcessingStatus.NeedsClarification
                                     || x.ProcessingStatus == MessageProcessingStatus.Failed),
                Rate = g.Count() == 0 ? 0 : Math.Round(
                    g.Count(x => x.ProcessingStatus == MessageProcessingStatus.NeedsClarification
                              || x.ProcessingStatus == MessageProcessingStatus.Failed) * 100.0 / g.Count(),
                    2)
            })
            .OrderByDescending(x => x.Rate)
            .ThenByDescending(x => x.Total)
            .ToList();

        var overall = new
        {
            Total = logs.Count,
            Problems = logs.Count(l => l.ProcessingStatus == MessageProcessingStatus.NeedsClarification
                                    || l.ProcessingStatus == MessageProcessingStatus.Failed),
            Rate = logs.Count == 0 ? 0 : Math.Round(
                logs.Count(l => l.ProcessingStatus == MessageProcessingStatus.NeedsClarification
                             || l.ProcessingStatus == MessageProcessingStatus.Failed) * 100.0 / logs.Count,
                2)
        };

        return Ok(new { windowDays = days, overall, byIntent });
    }

    /// <summary>
    /// Users who sent multiple messages in rapid succession after getting a clarification response.
    /// This is the "user is frustrated" signal — they tried to tell the bot something, it didn't
    /// understand, they rephrased. The original messages in each chain are our priority targets for
    /// corpus additions and prompt fixes.
    /// </summary>
    [HttpGet("telemetry/retry-patterns")]
    public async Task<IActionResult> GetRetryPatterns([FromQuery] string key, [FromQuery] int days = 7)
    {
        var auth = ValidateAdminKey(key);
        if (auth != null) return auth;

        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 30));

        // Pull all inbound logs with user context. We'll scan per-user for close-succession patterns.
        var logs = await _db.MessageLogs
            .Where(m => m.Direction == MessageDirection.Inbound
                        && m.CreatedAtUtc >= since
                        && m.UserId != null)
            .OrderBy(m => m.UserId).ThenBy(m => m.CreatedAtUtc)
            .Select(m => new { m.UserId, m.ParsedIntent, m.ProcessingStatus, m.RawMessage, m.CreatedAtUtc })
            .ToListAsync();

        var chains = new List<object>();
        var grouped = logs.GroupBy(l => l.UserId);
        foreach (var g in grouped)
        {
            var arr = g.ToList();
            for (int i = 0; i < arr.Count - 1; i++)
            {
                // Clarification + follow-up within 2 minutes is a chain seed
                if (arr[i].ProcessingStatus != MessageProcessingStatus.NeedsClarification) continue;

                var chain = new List<dynamic> { arr[i] };
                for (int j = i + 1; j < arr.Count; j++)
                {
                    if ((arr[j].CreatedAtUtc - chain[^1].CreatedAtUtc).TotalMinutes > 2) break;
                    chain.Add(arr[j]);
                }

                if (chain.Count >= 2)
                {
                    chains.Add(new
                    {
                        userId = g.Key,
                        messages = chain.Select(m => new
                        {
                            message = (string)m.RawMessage,
                            intent = (string?)m.ParsedIntent,
                            status = m.ProcessingStatus.ToString(),
                            at = (DateTime)m.CreatedAtUtc
                        }).ToList()
                    });
                    i += chain.Count - 1; // Skip past the chain we just consumed
                }
            }
        }

        return Ok(new { windowDays = days, chainCount = chains.Count, chains });
    }

    /// <summary>
    /// Histogram of Claude confidence scores for successfully-parsed inbound messages. Drift in this
    /// distribution is the earliest signal that prompt quality, model behavior, or user phrasings
    /// have shifted. Healthy steady state: >80% of messages at &gt;=0.90, most of the rest between
    /// 0.75 and 0.90, low single digits in the lower tiers.
    /// </summary>
    [HttpGet("telemetry/confidence-distribution")]
    public async Task<IActionResult> GetConfidenceDistribution([FromQuery] string key, [FromQuery] int days = 7)
    {
        var auth = ValidateAdminKey(key);
        if (auth != null) return auth;

        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 30));

        var confidences = await _db.MessageLogs
            .Where(m => m.Direction == MessageDirection.Inbound
                        && m.CreatedAtUtc >= since
                        && m.ConfidenceScore != null)
            .Select(m => m.ConfidenceScore!.Value)
            .ToListAsync();

        var buckets = new[]
        {
            new { label = "0.00-0.20", min = 0m,    max = 0.2m },
            new { label = "0.20-0.40", min = 0.2m,  max = 0.4m },
            new { label = "0.40-0.60", min = 0.4m,  max = 0.6m },
            new { label = "0.60-0.75", min = 0.6m,  max = 0.75m },
            new { label = "0.75-0.90", min = 0.75m, max = 0.9m },
            new { label = "0.90-1.00", min = 0.9m,  max = 1.0001m }
        };

        var distribution = buckets.Select(b => new
        {
            bucket = b.label,
            count = confidences.Count(c => c >= b.min && c < b.max),
            percent = confidences.Count == 0 ? 0 : Math.Round(confidences.Count(c => c >= b.min && c < b.max) * 100.0 / confidences.Count, 2)
        }).ToList();

        return Ok(new
        {
            windowDays = days,
            totalMessages = confidences.Count,
            mean = confidences.Count == 0 ? 0 : Math.Round((double)confidences.Average(), 3),
            distribution
        });
    }

    /// <summary>
    /// Clusters the raw text of messages that ended in NeedsClarification or unknown intent, grouped
    /// by a simple normalized form (lowercased, trimmed, alnum+space only). Top clusters are the
    /// phrasings most frequently failing to parse — each one should become a corpus entry.
    /// </summary>
    [HttpGet("telemetry/top-failures")]
    public async Task<IActionResult> GetTopFailures([FromQuery] string key, [FromQuery] int days = 7, [FromQuery] int limit = 20)
    {
        var auth = ValidateAdminKey(key);
        if (auth != null) return auth;

        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 30));

        var failures = await _db.MessageLogs
            .Where(m => m.Direction == MessageDirection.Inbound
                        && m.CreatedAtUtc >= since
                        && (m.ProcessingStatus == MessageProcessingStatus.NeedsClarification
                            || m.ProcessingStatus == MessageProcessingStatus.Failed
                            || m.ParsedIntent == "unknown"))
            .Select(m => new { m.RawMessage, m.ParsedIntent, m.ProcessingStatus, m.ConfidenceScore })
            .ToListAsync();

        var clusters = failures
            .GroupBy(f => Normalize(f.RawMessage))
            .Select(g => new
            {
                normalized = g.Key,
                count = g.Count(),
                sampleMessage = g.First().RawMessage,
                commonIntent = g.Where(x => x.ParsedIntent != null).GroupBy(x => x.ParsedIntent).OrderByDescending(x => x.Count()).FirstOrDefault()?.Key,
                avgConfidence = g.Where(x => x.ConfidenceScore.HasValue).Any()
                    ? Math.Round((double)g.Where(x => x.ConfidenceScore.HasValue).Average(x => x.ConfidenceScore!.Value), 3)
                    : (double?)null
            })
            .OrderByDescending(c => c.count)
            .Take(Math.Clamp(limit, 5, 100))
            .ToList();

        return Ok(new { windowDays = days, totalFailures = failures.Count, clusters });
    }

    /// <summary>
    /// Simple normalization for failure clustering: lowercase, strip non-alphanumeric (except space),
    /// collapse whitespace. Rough but good enough to group "Sold 3 rice" and "sold 3 rice!" together.
    /// </summary>
    private static string Normalize(string raw)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in raw.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == ' ') sb.Append(c);
        }
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString().Trim(), @"\s+", " ");
    }
}

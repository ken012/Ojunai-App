using System.Text;
using System.Text.Json;
using BizPilot.API.Common;
using BizPilot.API.Data;
using BizPilot.API.Models;
using BizPilot.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BizPilot.API.Services;

public class PaystackService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly HttpClient _http;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PaystackService> _logger;

    public PaystackService(AppDbContext db, IConfiguration config, IHttpClientFactory httpFactory, IServiceProvider serviceProvider, ILogger<PaystackService> logger)
    {
        _db = db;
        _config = config;
        _http = httpFactory.CreateClient("Paystack");
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<string> InitializeSubscriptionAsync(Guid businessId, string plan, string email)
    {
        var business = await _db.Businesses.FindAsync(businessId)
            ?? throw new KeyNotFoundException("Business not found.");

        if (!business.IsBillable)
            throw new InvalidOperationException("This account is not billable.");

        var planConfig = PlanLimits.Get(plan);
        if (planConfig.PricePerMonth <= 0)
            throw new InvalidOperationException("Invalid plan.");

        // Determine billing cycle and amount from BillingConfig
        var cycle = business.BillingCycle ?? "monthly";
        var isAnnual = cycle.Equals("annual", StringComparison.OrdinalIgnoreCase);
        var billingCycleEnum = isAnnual ? BillingConfig.BillingCycle.Annual : BillingConfig.BillingCycle.Monthly;
        var amount = BillingConfig.GetPrice(plan, billingCycleEnum, "NGN") ?? planConfig.PricePerMonth;
        var interval = isAnnual ? "annually" : "monthly";

        // Create or get Paystack plan
        var paystackPlanCode = await GetOrCreatePlanAsync($"{plan}-{cycle}", amount, interval);

        // Create or get Paystack customer
        var customerCode = business.PaystackCustomerCode;
        if (string.IsNullOrEmpty(customerCode))
        {
            try
            {
                customerCode = await CreateCustomerAsync(email, business.Name, businessId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create Paystack customer for {Email}, attempting to fetch existing", email);
                // Try to find existing customer by email lookup
                var existing = await GetAsync($"/customer/{Uri.EscapeDataString(email)}");
                if (existing.TryGetProperty("data", out var custData) && custData.TryGetProperty("customer_code", out var cc))
                    customerCode = cc.GetString();
                if (string.IsNullOrEmpty(customerCode))
                    throw;
            }
            business.PaystackCustomerCode = customerCode;
            await _db.SaveChangesAsync();
        }

        // Initialize transaction for subscription
        var body = new
        {
            email,
            amount = (int)(amount * 100), // kobo
            plan = paystackPlanCode,
            callback_url = $"{_config["App:DashboardUrl"] ?? "https://app.bizpilot-ai.com"}/settings?subscribed=true",
            metadata = new { businessId = businessId.ToString(), plan }
        };

        var response = await PostAsync("/transaction/initialize", body);
        return response.GetProperty("data").GetProperty("authorization_url").GetString()!;
    }

    public async Task<string> InitializeVoiceAIAsync(Guid businessId, string email, decimal amount, string currency, string cycle)
    {
        var business = await _db.Businesses.FindAsync(businessId)
            ?? throw new KeyNotFoundException("Business not found.");

        var isAnnual = cycle.Equals("annual", StringComparison.OrdinalIgnoreCase);
        var interval = isAnnual ? "annually" : "monthly";
        var paystackPlanCode = await GetOrCreatePlanAsync($"voice-ai-{cycle}", amount, interval);

        var customerCode = business.PaystackCustomerCode;
        if (string.IsNullOrEmpty(customerCode))
        {
            customerCode = await CreateCustomerAsync(email, business.Name, businessId);
            business.PaystackCustomerCode = customerCode;
            await _db.SaveChangesAsync();
        }

        var body = new
        {
            email,
            amount = (int)(amount * 100),
            plan = paystackPlanCode,
            callback_url = $"{_config["App:DashboardUrl"] ?? "https://app.bizpilot-ai.com"}/settings?voiceai=true",
            metadata = new { businessId = businessId.ToString(), product = "voice_ai" }
        };

        var response = await PostAsync("/transaction/initialize", body);
        return response.GetProperty("data").GetProperty("authorization_url").GetString()!;
    }

    /// <summary>
    /// Processes a Paystack webhook event after signature validation (done in SubscriptionController).
    /// Enforces idempotency (Paystack retries on failure) and dispatches to the right handler.
    /// </summary>
    /// <remarks>
    /// Security properties:
    ///   - Signature verified before this method is called (prevents forgery)
    ///   - Idempotency via PaystackEventLog table (prevents replay attacks — same event twice does nothing)
    ///   - Amount verified against plan price inside HandleChargeSuccess (prevents amount manipulation)
    /// </remarks>
    public async Task HandleWebhookAsync(JsonElement payload)
    {
        var eventType = payload.GetProperty("event").GetString();

        // Build an idempotency key from the event data. Paystack uses either a numeric `id` or a string `reference`
        // depending on the event type. We key on both type+id so the same id under different event types can't collide.
        var data = payload.GetProperty("data");
        string? eventId = null;
        if (data.TryGetProperty("id", out var idEl))
            eventId = idEl.ToString();
        else if (data.TryGetProperty("reference", out var refEl))
            eventId = refEl.GetString();

        if (!string.IsNullOrEmpty(eventId))
        {
            var fullEventId = $"{eventType}:{eventId}";
            // If we've already processed this exact event, silently succeed. Paystack considers any 2xx response as OK.
            var seen = await _db.PaystackEventLogs.AnyAsync(e => e.EventId == fullEventId);
            if (seen)
            {
                _logger.LogInformation("Paystack webhook duplicate ignored: {Event} {Id}", eventType, eventId);
                return;
            }
            // Record the event BEFORE processing so if the handler crashes and Paystack retries,
            // we still recognize the duplicate. The unique index on PaystackEventLog.EventId guarantees this.
            _db.PaystackEventLogs.Add(new Models.PaystackEventLog
            {
                EventId = fullEventId,
                EventType = eventType ?? "unknown"
            });
            await _db.SaveChangesAsync();
        }

        _logger.LogInformation("Paystack webhook: {Event}", eventType);

        // Dispatch to the specific handler. Unknown event types are silently ignored (Paystack may add new events).
        switch (eventType)
        {
            case "subscription.create":
                // A new subscription was created (first successful charge OR user subscribed).
                await HandleSubscriptionCreated(data);
                break;
            case "charge.success":
                // A one-time or recurring charge succeeded. Verifies amount matches the plan price.
                await HandleChargeSuccess(data);
                break;
            case "subscription.not_renew":
            case "subscription.disable":
                // User cancelled (via our dashboard or Paystack directly). Access continues until SubscriptionEndsAt.
                await HandleSubscriptionCancelled(data);
                break;
            case "invoice.payment_failed":
                // A recurring charge failed. Paystack retries automatically. We just log for visibility.
                await HandlePaymentFailed(data);
                break;
            case "charge.dispute.create":
            case "charge.dispute.remind":
                await HandleDisputeAsync(data);
                break;
            case "refund.processed":
                await HandleRefundAsync(data);
                break;
        }
    }

    public async Task CancelSubscriptionAsync(Guid businessId)
    {
        var business = await _db.Businesses.FindAsync(businessId)
            ?? throw new KeyNotFoundException("Business not found.");

        // Cancel via Paystack if there's an active subscription to disable
        if (!string.IsNullOrEmpty(business.PaystackSubscriptionCode))
        {
            try
            {
                var subResponse = await GetAsync($"/subscription/{business.PaystackSubscriptionCode}");
                var emailToken = subResponse.GetProperty("data").GetProperty("email_token").GetString();
                await PostAsync("/subscription/disable", new
                {
                    code = business.PaystackSubscriptionCode,
                    token = emailToken
                });
                _logger.LogInformation("Paystack subscription cancelled for {Business}, access until {EndsAt}",
                    business.Name, business.SubscriptionEndsAt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Paystack cancel API call failed for {Business} — clearing locally", business.Name);
            }
        }

        // Clear Paystack references regardless — even if the API call failed, we don't want the
        // dashboard to keep showing "active subscription" for a sub the user wants gone.
        business.PaystackSubscriptionCode = null;
        business.PaystackPlanCode = null;
        business.SubscriptionStatus = "cancelled";

        _db.BillingEvents.Add(new BillingEvent
        {
            BusinessId = business.Id,
            EventType = "subscription.cancelled",
            Provider = "paystack",
            Plan = business.Plan,
            Status = "cancelled",
            CreatedAtUtc = DateTime.UtcNow
        });

        // If there's no future billing end date, revert to starter immediately.
        // If there IS a future end date, keep access until then (the TrialRevertJobService
        // handles the eventual downgrade when SubscriptionEndsAt passes).
        if (business.SubscriptionEndsAt == null || business.SubscriptionEndsAt <= DateTime.UtcNow)
        {
            // Revert to Starter but keep SubscribedPlan = "starter" so the user is recognized as
            // a former subscriber. Setting it to null would make them look like a new user, showing
            // free-trial UI and "Subscribe to Starter" buttons instead of upgrade options.
            business.Plan = "starter";
            business.SubscribedPlan = "starter";
            business.PendingPlanChange = null;
            business.SubscriptionEndsAt = null;
            business.TrialEndsAt = null;
        }

        await _db.SaveChangesAsync();
    }

    private async Task HandleSubscriptionCreated(JsonElement data)
    {
        var customerCode = data.GetProperty("customer").GetProperty("customer_code").GetString();
        var subscriptionCode = data.GetProperty("subscription_code").GetString();
        var planCode = data.GetProperty("plan").GetProperty("plan_code").GetString();
        var nextPayment = data.TryGetProperty("next_payment_date", out var npd) ? npd.GetString() : null;

        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.PaystackCustomerCode == customerCode);
        if (business == null) { _logger.LogWarning("No business for customer {Code}", customerCode); return; }

        // Map Paystack plan code to our plan name
        var plan = await MapPlanCodeToName(planCode);
        if (plan == null) { _logger.LogWarning("Unknown plan code {Code}", planCode); return; }

        business.PaystackSubscriptionCode = subscriptionCode;
        business.PaystackPlanCode = planCode;
        business.Plan = plan;
        business.SubscribedPlan = plan;
        business.PendingPlanChange = null;
        business.SubscriptionStatus = "active";
        business.TrialEndsAt = null;

        var isAnnualSub = business.BillingCycle?.Equals("annual", StringComparison.OrdinalIgnoreCase) == true;
        business.SubscriptionEndsAt = isAnnualSub ? DateTime.UtcNow.AddYears(1) : DateTime.UtcNow.AddMonths(1);

        if (nextPayment != null && DateTime.TryParse(nextPayment, out var next))
        {
            var diff = Math.Abs((next - business.SubscriptionEndsAt.Value).TotalDays);
            if (diff > 3)
                _logger.LogWarning("Paystack next_payment_date {Next} differs from calculated {Calculated} by {Diff} days",
                    next, business.SubscriptionEndsAt, diff);
        }

        _db.BillingEvents.Add(new BillingEvent
        {
            BusinessId = business.Id,
            EventType = "subscription.created",
            Provider = "paystack",
            Plan = plan,
            SubscriptionId = subscriptionCode,
            Status = "active",
            CreatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        _logger.LogInformation("Subscription activated: {Business} → {Plan}", business.Name, plan);

        await SendPaymentConfirmationAsync(business, plan);
    }

    private async Task HandleChargeSuccess(JsonElement data)
    {
        if (!data.TryGetProperty("metadata", out var meta)) return;
        if (!meta.TryGetProperty("businessId", out var bizIdEl)) return;

        if (!Guid.TryParse(bizIdEl.GetString(), out var businessId)) return;

        var business = await _db.Businesses.FindAsync(businessId);
        if (business == null) return;

        // Voice AI add-on payment
        var product = meta.TryGetProperty("product", out var prodEl) ? prodEl.GetString() : null;
        if (product == "voice_ai")
        {
            decimal? chargedNaira = data.TryGetProperty("amount", out var vaAmtEl) ? vaAmtEl.GetInt64() / 100m : null;
            var isAnnual = business.BillingCycle?.Equals("annual", StringComparison.OrdinalIgnoreCase) == true;

            business.VoiceAIEnabled = true;
            business.VoiceAIPlanStatus = "active";
            business.VoiceAIEnabledAt ??= DateTime.UtcNow;
            business.VoiceAITrialEndsAt = null;
            var baseDate = (business.VoiceAISubscriptionEndsAt.HasValue && business.VoiceAISubscriptionEndsAt > DateTime.UtcNow)
                ? business.VoiceAISubscriptionEndsAt.Value : DateTime.UtcNow;
            business.VoiceAISubscriptionEndsAt = isAnnual ? baseDate.AddYears(1) : baseDate.AddMonths(1);

            if (data.TryGetProperty("subscription_code", out var scEl))
                business.VoiceAISubscriptionId = scEl.GetString();

            _db.BillingEvents.Add(new BillingEvent
            {
                BusinessId = business.Id,
                EventType = "voiceai.payment.success",
                Provider = "paystack",
                Plan = "voice_ai",
                Amount = chargedNaira,
                Currency = "NGN",
                PaymentMethod = "card",
                Status = "success"
            });

            await _db.SaveChangesAsync();
            _logger.LogInformation("Voice AI payment confirmed: {Business}", business.Name);

            var provisioner = _serviceProvider.GetRequiredService<VoiceAIProvisioningService>();
            await provisioner.EnsureProvisionedAsync(business);
            return;
        }

        var plan = meta.TryGetProperty("plan", out var planEl) ? planEl.GetString() : null;
        if (!string.IsNullOrEmpty(plan))
        {
            // Verify amount matches expected plan price
            var expectedPrice = PlanLimits.Get(plan).PricePerMonth;
            decimal? chargedNaira = null;
            if (data.TryGetProperty("amount", out var amountEl))
            {
                var chargedKobo = amountEl.GetInt64();
                chargedNaira = chargedKobo / 100m;
                if (Math.Abs(chargedNaira.Value - expectedPrice) > 1)
                {
                    _logger.LogWarning("Paystack charge amount mismatch for {Business}: charged {cs}{Charged}, expected {cs}{Expected}",
                        business.Name, BillingConfig.Symbol(business.Currency), chargedNaira, BillingConfig.Symbol(business.Currency), expectedPrice);
                    _db.BillingEvents.Add(new BillingEvent
                    {
                        BusinessId = business.Id,
                        EventType = "payment.rejected",
                        Provider = "paystack",
                        Plan = plan,
                        Amount = chargedNaira,
                        Currency = "NGN",
                        Status = "rejected",
                        ErrorDetails = $"Amount mismatch: paid {chargedNaira}, expected {expectedPrice}",
                        CreatedAtUtc = DateTime.UtcNow
                    });
                    await _db.SaveChangesAsync();
                    return;
                }
            }

            business.Plan = plan;
            business.SubscribedPlan = plan;
            business.PendingPlanChange = null;
            business.PaymentMethod = "card";
            business.IsAutoRenew = true;
            business.SubscriptionStatus = "active";
            business.TrialEndsAt = null;
            var baseDate = (business.SubscriptionEndsAt.HasValue && business.SubscriptionEndsAt > DateTime.UtcNow)
                ? business.SubscriptionEndsAt.Value
                : DateTime.UtcNow;
            business.SubscriptionEndsAt = baseDate.AddMonths(1);

            // Store customer code if not yet stored
            if (string.IsNullOrEmpty(business.PaystackCustomerCode) && data.TryGetProperty("customer", out var cust))
            {
                business.PaystackCustomerCode = cust.GetProperty("customer_code").GetString();
            }

            _db.BillingEvents.Add(new BillingEvent
            {
                BusinessId = business.Id,
                EventType = "payment.success",
                Provider = "paystack",
                Plan = plan,
                Amount = chargedNaira,
                Currency = business.Currency,
                PaymentMethod = "card",
                Status = "success",
                CreatedAtUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            _logger.LogInformation("Payment confirmed: {Business} → {Plan}", business.Name, plan);
        }
    }

    private async Task HandleSubscriptionCancelled(JsonElement data)
    {
        var subscriptionCode = data.GetProperty("subscription_code").GetString();
        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.PaystackSubscriptionCode == subscriptionCode);
        if (business == null) return;

        // Don't downgrade immediately — keep access until SubscriptionEndsAt
        // The TrialRevertJobService will handle the actual downgrade
        business.PaystackSubscriptionCode = null;
        business.PaystackPlanCode = null;
        business.SubscriptionStatus = "cancelled";

        _db.BillingEvents.Add(new BillingEvent
        {
            BusinessId = business.Id,
            EventType = "subscription.cancelled",
            Provider = "paystack",
            Plan = business.Plan,
            SubscriptionId = subscriptionCode,
            Status = "cancelled",
            CreatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation("Subscription cancelled: {Business}, access until {EndsAt}", business.Name, business.SubscriptionEndsAt);
    }

    private async Task HandleDisputeAsync(JsonElement data)
    {
        if (!data.TryGetProperty("metadata", out var meta)) return;
        if (!meta.TryGetProperty("businessId", out var bizIdEl)) return;
        if (!Guid.TryParse(bizIdEl.GetString(), out var businessId)) return;

        var business = await _db.Businesses.FindAsync(businessId);
        if (business == null) return;

        _logger.LogWarning("Payment dispute for {Business} on {Plan}", business.Name, business.Plan);

        _db.BillingEvents.Add(new BillingEvent
        {
            BusinessId = business.Id,
            EventType = "payment.disputed",
            Provider = "paystack",
            Plan = business.Plan,
            Status = "disputed",
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    private async Task HandleRefundAsync(JsonElement data)
    {
        if (!data.TryGetProperty("metadata", out var meta)) return;
        if (!meta.TryGetProperty("businessId", out var bizIdEl)) return;
        if (!Guid.TryParse(bizIdEl.GetString(), out var businessId)) return;

        var business = await _db.Businesses.FindAsync(businessId);
        if (business == null) return;

        business.SubscriptionStatus = "cancelled";
        business.PaystackSubscriptionCode = null;
        business.PaystackPlanCode = null;

        _db.BillingEvents.Add(new BillingEvent
        {
            BusinessId = business.Id,
            EventType = "payment.refunded",
            Provider = "paystack",
            Plan = business.Plan,
            Status = "refunded",
            CreatedAtUtc = DateTime.UtcNow
        });

        if (business.SubscriptionEndsAt == null || business.SubscriptionEndsAt <= DateTime.UtcNow)
        {
            business.Plan = "starter";
            business.SubscribedPlan = "starter";
            business.SubscriptionEndsAt = null;
        }

        await _db.SaveChangesAsync();

        _logger.LogWarning("Refund processed for {Business}: downgraded to {Plan}", business.Name, business.Plan);

        // Notify owner via WhatsApp
        try
        {
            var owner = await _db.Users.FirstOrDefaultAsync(u =>
                u.BusinessId == businessId && u.Role == UserRole.Owner && u.IsActive);
            if (owner != null)
            {
                var whatsApp = _serviceProvider.GetRequiredService<IWhatsAppService>();
                await whatsApp.SendMessageAsync(
                    $"whatsapp:{owner.PhoneNumber}",
                    "Your recent payment has been refunded. Your subscription has been cancelled. " +
                    "Visit app.bizpilot-ai.com/settings to resubscribe.",
                    businessId, owner.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send refund notification for {Business}", business.Name);
        }
    }

    private async Task HandlePaymentFailed(JsonElement data)
    {
        if (!data.TryGetProperty("subscription", out var sub)) return;
        var subscriptionCode = sub.TryGetProperty("subscription_code", out var sc) ? sc.GetString() : null;
        if (subscriptionCode == null) return;

        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.PaystackSubscriptionCode == subscriptionCode);
        if (business == null) return;

        _logger.LogWarning("Payment failed for {Business} on {Plan}", business.Name, business.Plan);

        business.SubscriptionStatus = "past_due";
        _db.BillingEvents.Add(new BillingEvent
        {
            BusinessId = business.Id,
            EventType = "payment.failed",
            Provider = "paystack",
            Plan = business.Plan,
            Status = "failed",
            SubscriptionId = subscriptionCode,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        // Send WhatsApp notification about failed payment
        try
        {
            var owner = await _db.Users.FirstOrDefaultAsync(u =>
                u.BusinessId == business.Id && u.Role == UserRole.Owner && u.IsActive);
            if (owner != null)
            {
                var planLabel = (business.Plan ?? "starter")[0..1].ToUpper() + (business.Plan ?? "starter")[1..];
                var whatsApp = _serviceProvider.GetRequiredService<IWhatsAppService>();
                await whatsApp.SendMessageAsync(
                    $"whatsapp:{owner.PhoneNumber}",
                    $"⚠️ *Payment Failed*\n\n" +
                    $"Your card payment for BizPilot could not be processed. " +
                    $"Please update your payment method at app.bizpilot-ai.com/settings to keep your {planLabel} plan active.",
                    business.Id, owner.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send payment-failed WhatsApp for {Business}", business.Name);
        }
    }

    private async Task<string> GetOrCreatePlanAsync(string planName, decimal amount, string interval = "monthly")
    {
        var existing = _config[$"Paystack:PlanCodes:{planName}"];
        if (!string.IsNullOrEmpty(existing)) return existing;

        var response = await GetAsync("/plan");
        if (!response.TryGetProperty("data", out var plans))
        {
            _logger.LogError("Paystack /plan response missing 'data'. Check your Paystack:SecretKey. Response: {Response}", response);
            throw new InvalidOperationException("Failed to connect to Paystack. Check API key configuration.");
        }

        foreach (var p in plans.EnumerateArray())
        {
            var name = p.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (name == $"BizPilot {planName}")
                return p.GetProperty("plan_code").GetString()!;
        }

        var result = await PostAsync("/plan", new
        {
            name = $"BizPilot {planName}",
            amount = (int)(amount * 100),
            interval
        });

        if (!result.TryGetProperty("data", out var data) || !data.TryGetProperty("plan_code", out var code))
        {
            _logger.LogError("Paystack plan creation failed: {Response}", result);
            throw new InvalidOperationException("Failed to create payment plan on Paystack.");
        }

        return code.GetString()!;
    }

    private async Task<string> CreateCustomerAsync(string email, string businessName, Guid businessId)
    {
        var result = await PostAsync("/customer", new
        {
            email,
            first_name = businessName,
            metadata = new { businessId = businessId.ToString() }
        });

        return result.GetProperty("data").GetProperty("customer_code").GetString()!;
    }

    private async Task<string?> MapPlanCodeToName(string? planCode)
    {
        if (string.IsNullOrEmpty(planCode)) return null;

        var response = await GetAsync("/plan");
        var plans = response.GetProperty("data");
        foreach (var p in plans.EnumerateArray())
        {
            if (p.GetProperty("plan_code").GetString() == planCode)
            {
                var name = p.GetProperty("name").GetString() ?? "";
                if (name.StartsWith("BizPilot "))
                    return name["BizPilot ".Length..].ToLower();
            }
        }
        return null;
    }

    private async Task<JsonElement> PostAsync(string path, object body)
    {
        var json = JsonSerializer.Serialize(body);
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var response = await _http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Paystack error: {Status} {Body}", response.StatusCode, content);
            throw new InvalidOperationException($"Paystack error: {content}");
        }
        // Clone the element so we can dispose the document without invalidating what we return.
        using var doc = JsonDocument.Parse(content);
        return doc.RootElement.Clone();
    }

    private async Task<JsonElement> GetAsync(string path)
    {
        var response = await _http.GetAsync(path);
        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        return doc.RootElement.Clone();
    }

    private async Task SendPaymentConfirmationAsync(Business business, string plan)
    {
        try
        {
            var owner = await _db.Users.FirstOrDefaultAsync(u =>
                u.BusinessId == business.Id && u.Role == UserRole.Owner && u.IsActive);
            if (owner == null) return;

            var planLabel = plan[0..1].ToUpper() + plan[1..];
            var planConfig = PlanLimits.Get(plan);
            var renewDate = business.SubscriptionEndsAt?.ToString("dd MMM yyyy") ?? "in 30 days";

            var whatsApp = _serviceProvider.GetRequiredService<IWhatsAppService>();
            await whatsApp.SendMessageAsync(
                $"whatsapp:{owner.PhoneNumber}",
                $"✅ *Payment successful!*\n\n" +
                $"Your *{planLabel}* plan is now active at {BillingConfig.FormatPrice(planConfig.PricePerMonth, "NGN")}/month.\n\n" +
                $"Next renewal: {renewDate}\n\n" +
                $"Say *my plan* to see your features, or *help* for commands.",
                business.Id, owner.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send payment confirmation WhatsApp");
        }
    }
}

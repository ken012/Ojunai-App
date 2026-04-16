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

        // Create or get Paystack plan
        var paystackPlanCode = await GetOrCreatePlanAsync(plan, planConfig.PricePerMonth);

        // Create or get Paystack customer
        var customerCode = business.PaystackCustomerCode;
        if (string.IsNullOrEmpty(customerCode))
        {
            customerCode = await CreateCustomerAsync(email, business.Name, businessId);
            business.PaystackCustomerCode = customerCode;
            await _db.SaveChangesAsync();
        }

        // Initialize transaction for subscription
        var body = new
        {
            email,
            amount = (int)(planConfig.PricePerMonth * 100), // kobo
            plan = paystackPlanCode,
            callback_url = $"{_config["App:DashboardUrl"] ?? "https://app.bizpilot-ai.com"}/settings?subscribed=true",
            metadata = new { businessId = businessId.ToString(), plan }
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
        }
    }

    public async Task CancelSubscriptionAsync(Guid businessId)
    {
        var business = await _db.Businesses.FindAsync(businessId)
            ?? throw new KeyNotFoundException("Business not found.");

        if (string.IsNullOrEmpty(business.PaystackSubscriptionCode))
            throw new InvalidOperationException("No active subscription to cancel.");

        // Get the email token needed for cancellation
        var subResponse = await GetAsync($"/subscription/{business.PaystackSubscriptionCode}");
        var emailToken = subResponse.GetProperty("data").GetProperty("email_token").GetString();

        await PostAsync("/subscription/disable", new
        {
            code = business.PaystackSubscriptionCode,
            token = emailToken
        });

        // Keep access until end of billing period — don't change Plan or SubscribedPlan yet
        // The subscription.disable webhook or SubscriptionEndsAt check will handle downgrade
        _logger.LogInformation("Subscription cancelled for {Business}, access until {EndsAt}",
            business.Name, business.SubscriptionEndsAt);
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
        business.TrialEndsAt = null;
        if (nextPayment != null && DateTime.TryParse(nextPayment, out var next))
            business.SubscriptionEndsAt = next;

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

        var plan = meta.TryGetProperty("plan", out var planEl) ? planEl.GetString() : null;
        if (!string.IsNullOrEmpty(plan))
        {
            // Verify amount matches expected plan price
            var expectedPrice = PlanLimits.Get(plan).PricePerMonth;
            if (data.TryGetProperty("amount", out var amountEl))
            {
                var chargedKobo = amountEl.GetInt64();
                var chargedNaira = chargedKobo / 100m;
                if (chargedNaira < expectedPrice)
                {
                    _logger.LogWarning("Paystack charge amount mismatch for {Business}: charged ₦{Charged}, expected ₦{Expected}",
                        business.Name, chargedNaira, expectedPrice);
                    return;
                }
            }

            business.Plan = plan;
            business.SubscribedPlan = plan;
            business.TrialEndsAt = null;
            business.SubscriptionEndsAt = DateTime.UtcNow.AddDays(32);

            // Store customer code if not yet stored
            if (string.IsNullOrEmpty(business.PaystackCustomerCode) && data.TryGetProperty("customer", out var cust))
            {
                business.PaystackCustomerCode = cust.GetProperty("customer_code").GetString();
            }

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
        await _db.SaveChangesAsync();

        _logger.LogInformation("Subscription cancelled: {Business}, access until {EndsAt}", business.Name, business.SubscriptionEndsAt);
    }

    private async Task HandlePaymentFailed(JsonElement data)
    {
        if (!data.TryGetProperty("subscription", out var sub)) return;
        var subscriptionCode = sub.TryGetProperty("subscription_code", out var sc) ? sc.GetString() : null;
        if (subscriptionCode == null) return;

        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.PaystackSubscriptionCode == subscriptionCode);
        if (business == null) return;

        _logger.LogWarning("Payment failed for {Business} on {Plan}", business.Name, business.Plan);
        // Paystack retries automatically. We just log it. If all retries fail, subscription.disable fires.
    }

    private async Task<string> GetOrCreatePlanAsync(string planName, decimal amount)
    {
        // Check if we already have this plan in Paystack
        var cacheKey = $"paystack_plan_{planName}";
        var existing = _config[$"Paystack:PlanCodes:{planName}"];
        if (!string.IsNullOrEmpty(existing)) return existing;

        // List existing plans and find ours
        var response = await GetAsync("/plan");
        var plans = response.GetProperty("data");
        foreach (var p in plans.EnumerateArray())
        {
            var name = p.GetProperty("name").GetString();
            if (name == $"BizPilot {planName}")
                return p.GetProperty("plan_code").GetString()!;
        }

        // Create new plan
        var result = await PostAsync("/plan", new
        {
            name = $"BizPilot {planName}",
            amount = (int)(amount * 100), // kobo
            interval = "monthly"
        });

        return result.GetProperty("data").GetProperty("plan_code").GetString()!;
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
                $"Your *{planLabel}* plan is now active at ₦{planConfig.PricePerMonth:N0}/month.\n\n" +
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

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BizPilot.API.Common;
using BizPilot.API.Data;
using BizPilot.API.Models;
using BizPilot.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BizPilot.API.Services;

/// <summary>
/// Flutterwave integration for non-NGN currencies (GHS, USD, GBP, KES, ZAR).
/// Mirrors the PaystackService pattern: initialize payment, handle webhooks, cancel subscriptions.
/// Flutterwave uses a hosted payment page flow similar to Paystack.
/// </summary>
public class FlutterwaveService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FlutterwaveService> _logger;

    public FlutterwaveService(
        AppDbContext db,
        IHttpClientFactory httpFactory,
        IConfiguration config,
        IServiceProvider serviceProvider,
        ILogger<FlutterwaveService> logger)
    {
        _db = db;
        _httpFactory = httpFactory;
        _config = config;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Initialize a Flutterwave payment. Returns a hosted payment page URL.
    /// Card payments are attached to a payment plan for auto-renewal.
    /// Mobile money / bank transfers are one-time charges.
    /// </summary>
    public async Task<string> InitializePaymentAsync(
        Guid businessId, string plan, string billingCycle, string currency, string email)
    {
        if (!Enum.TryParse<BillingConfig.BillingCycle>(billingCycle, true, out var cycle))
            throw new ArgumentException($"Invalid billing cycle: {billingCycle}");

        var amount = BillingConfig.GetPrice(plan, cycle, currency)
            ?? throw new ArgumentException($"No price found for {plan}/{billingCycle}/{currency}");

        var planId = await GetOrCreatePaymentPlanAsync(plan, cycle, currency, amount);

        var txRef = $"bizpilot-{businessId:N}-{DateTime.UtcNow.Ticks}";
        var callbackUrl = _config["Flutterwave:CallbackUrl"] ?? "https://app.bizpilot-ai.com/settings";

        var payload = new
        {
            tx_ref = txRef,
            amount = (double)amount,
            currency = currency.ToUpper(),
            redirect_url = callbackUrl,
            payment_plan = planId,
            customer = new { email },
            meta = new
            {
                businessId = businessId.ToString(),
                plan,
                billingCycle,
                currency
            },
            customizations = new
            {
                title = "BizPilot AI",
                description = $"{plan[0..1].ToUpper() + plan[1..]} Plan — {billingCycle}",
                logo = "https://app.bizpilot-ai.com/favicon.ico"
            }
        };

        var client = _httpFactory.CreateClient("Flutterwave");
        var json = JsonSerializer.Serialize(payload);
        var response = await client.PostAsync("/charges",
            new StringContent(json, Encoding.UTF8, "application/json"));

        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Flutterwave init failed: {Status} {Body}", response.StatusCode, body);
            throw new InvalidOperationException("Failed to initialize payment. Please try again.");
        }

        _logger.LogInformation("Flutterwave charge response: {Body}", body);

        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.TryGetProperty("data", out var d) ? d : doc.RootElement;
        // Try common field names for the checkout URL
        var link = (data.TryGetProperty("link", out var l) ? l.GetString() : null)
            ?? (data.TryGetProperty("checkout_url", out var cu) ? cu.GetString() : null)
            ?? (data.TryGetProperty("authorization_url", out var au) ? au.GetString() : null)
            ?? throw new InvalidOperationException($"Flutterwave did not return a payment link. Response: {body}");

        _logger.LogInformation("Flutterwave payment initialized: {TxRef} for {Plan}/{Cycle}/{Currency}",
            txRef, plan, billingCycle, currency);

        return link;
    }

    /// <summary>
    /// Process a Flutterwave webhook event. Verifies the transaction via the Flutterwave API,
    /// then activates the subscription if payment was successful.
    /// </summary>
    public async Task HandleWebhookAsync(JsonElement payload)
    {
        var eventType = payload.TryGetProperty("event", out var evt) ? evt.GetString() : null;
        _logger.LogInformation("Flutterwave webhook: {Event}", eventType);

        switch (eventType)
        {
            case "charge.completed":
                await HandleChargeCompleted(payload);
                break;
            case "subscription.cancelled":
                await HandleSubscriptionCancelled(payload);
                break;
        }
    }

    /// <summary>Verify the Flutterwave webhook hash matches our secret.</summary>
    public bool VerifyWebhook(string hash)
    {
        var secret = _config["Flutterwave:WebhookSecret"];
        if (string.IsNullOrEmpty(secret)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(hash),
            Encoding.UTF8.GetBytes(secret));
    }

    /// <summary>
    /// Verify a Flutterwave Inline checkout payment and activate the subscription.
    /// Called after the frontend JS SDK callback fires with the transaction details.
    /// </summary>
    public async Task<string?> VerifyAndActivateAsync(Guid businessId, string? transactionId, string? txRef)
    {
        var business = await _db.Businesses.FindAsync(businessId);
        if (business == null) return "Business not found.";

        // Verify via the v3 API with secret key (Inline SDK creates v3 transactions)
        var secretKey = _config["Flutterwave:SecretKey"];
        if (string.IsNullOrEmpty(secretKey))
            return "Flutterwave SecretKey not configured.";

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {secretKey}");

        HttpResponseMessage response;
        if (!string.IsNullOrEmpty(transactionId))
            response = await httpClient.GetAsync($"https://api.flutterwave.com/v3/transactions/{transactionId}/verify");
        else if (!string.IsNullOrEmpty(txRef))
            response = await httpClient.GetAsync($"https://api.flutterwave.com/v3/transactions/verify_by_reference?tx_ref={Uri.EscapeDataString(txRef)}");
        else
            return "No transaction identifier provided.";

        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Flutterwave verify failed: {Status} {Body}", response.StatusCode, body);
            return "Could not verify payment. Please try again.";
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Handle both single charge and list response
        JsonElement chargeData;
        if (root.TryGetProperty("data", out var data))
        {
            if (data.ValueKind == JsonValueKind.Array)
            {
                if (data.GetArrayLength() == 0) return "Transaction not found.";
                chargeData = data[0];
            }
            else
            {
                chargeData = data;
            }
        }
        else return "Invalid verification response.";

        var status = chargeData.TryGetProperty("status", out var st) ? st.GetString() : null;
        if (status != "successful" && status != "completed")
            return $"Payment was not successful. Status: {status}";

        // Activate the subscription
        var plan = business.Plan;
        var billingCycle = business.BillingCycle ?? "monthly";
        var currency = business.BillingCurrency ?? business.Currency;
        var isAnnual = billingCycle.Equals("annual", StringComparison.OrdinalIgnoreCase);

        // Read plan from the stored billing context (set during Initialize)
        if (chargeData.TryGetProperty("meta", out var meta) && meta.TryGetProperty("plan", out var mp))
            plan = mp.GetString() ?? plan;

        business.Plan = plan;
        business.SubscribedPlan = plan;
        business.BillingProvider = "flutterwave";
        business.PaymentMethod = "card";
        business.IsAutoRenew = false;
        business.TrialEndsAt = null;
        business.SubscriptionEndsAt = isAnnual
            ? DateTime.UtcNow.AddYears(1)
            : DateTime.UtcNow.AddMonths(1);

        await _db.SaveChangesAsync();

        _logger.LogInformation("Flutterwave inline payment verified: {Business} → {Plan} ({Cycle}/{Currency})",
            business.Name, plan, billingCycle, currency);

        // Send WhatsApp confirmation
        try
        {
            var owner = await _db.Users.FirstOrDefaultAsync(u =>
                u.BusinessId == businessId && u.Role == UserRole.Owner && u.IsActive);
            if (owner != null)
            {
                var planLabel = plan[0..1].ToUpper() + plan[1..];
                if (!Enum.TryParse<BillingConfig.BillingCycle>(billingCycle, true, out var bc))
                    bc = BillingConfig.BillingCycle.Monthly;
                var price = BillingConfig.GetPrice(plan, bc, currency);
                var formattedPrice = price.HasValue ? BillingConfig.FormatPrice(price.Value, currency) : "your selected plan";
                var cycleLabel = isAnnual ? "year" : "month";

                var whatsApp = _serviceProvider.GetRequiredService<IWhatsAppService>();
                await whatsApp.SendMessageAsync(
                    $"whatsapp:{owner.PhoneNumber}",
                    $"✅ *Payment successful!*\n\n" +
                    $"Your *{planLabel}* plan is now active at {formattedPrice}/{cycleLabel}.\n\n" +
                    $"Expires on {business.SubscriptionEndsAt:dd MMM yyyy}. You'll need to renew manually.\n\n" +
                    $"Say *my plan* to see your features.",
                    businessId, owner.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send payment confirmation for {Business}", business.Name);
        }

        return null; // success
    }

    public async Task CancelSubscriptionAsync(Guid businessId)
    {
        var business = await _db.Businesses.FindAsync(businessId)
            ?? throw new KeyNotFoundException("Business not found.");

        if (!string.IsNullOrEmpty(business.FlutterwaveSubscriptionId))
        {
            try
            {
                var client = _httpFactory.CreateClient("Flutterwave");
                await client.PutAsync($"/subscriptions/{business.FlutterwaveSubscriptionId}/cancel",
                    new StringContent("{}", Encoding.UTF8, "application/json"));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Flutterwave cancel failed for {Business}", business.Name);
            }
        }

        business.FlutterwaveSubscriptionId = null;
        business.FlutterwaveCustomerId = null;

        if (business.SubscriptionEndsAt == null || business.SubscriptionEndsAt <= DateTime.UtcNow)
        {
            business.Plan = "starter";
            business.SubscribedPlan = "starter";
            business.PendingPlanChange = null;
            business.SubscriptionEndsAt = null;
            business.TrialEndsAt = null;
        }

        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Get or create a Flutterwave payment plan for recurring billing.
    /// Plans are named "BizPilot {Plan} {Cycle} {Currency}" for dedup.
    /// Returns the plan ID to attach to the payment.
    /// </summary>
    private async Task<int?> GetOrCreatePaymentPlanAsync(
        string plan, BillingConfig.BillingCycle cycle, string currency, decimal amount)
    {
        var planName = $"BizPilot {plan} {cycle} {currency}".ToLower();
        var interval = cycle == BillingConfig.BillingCycle.Annual ? "annually" : "monthly";

        try
        {
            var client = _httpFactory.CreateClient("Flutterwave");

            // Check existing plans
            var listResponse = await client.GetAsync("/payment-plans");
            if (listResponse.IsSuccessStatusCode)
            {
                var listBody = await listResponse.Content.ReadAsStringAsync();
                using var listDoc = JsonDocument.Parse(listBody);
                if (listDoc.RootElement.TryGetProperty("data", out var plans) && plans.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in plans.EnumerateArray())
                    {
                        var name = p.TryGetProperty("name", out var n) ? n.GetString()?.ToLower() : null;
                        if (name == planName)
                        {
                            return p.GetProperty("id").GetInt32();
                        }
                    }
                }
            }

            // Create new plan
            var createPayload = JsonSerializer.Serialize(new
            {
                amount = (int)amount,
                name = planName,
                interval,
                currency = currency.ToUpper()
            });

            var createResponse = await client.PostAsync("/payment-plans",
                new StringContent(createPayload, Encoding.UTF8, "application/json"));

            var createBody = await createResponse.Content.ReadAsStringAsync();
            if (!createResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Flutterwave plan creation failed: {Status} {Body}. Proceeding without plan.",
                    createResponse.StatusCode, createBody);
                return null;
            }

            using var createDoc = JsonDocument.Parse(createBody);
            var newId = createDoc.RootElement.GetProperty("data").GetProperty("id").GetInt32();
            _logger.LogInformation("Created Flutterwave payment plan: {Name} → {Id}", planName, newId);
            return newId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get/create Flutterwave payment plan. Proceeding without plan.");
            return null;
        }
    }

    private async Task<string?> FetchCustomerSubscriptionIdAsync(string email)
    {
        try
        {
            var client = _httpFactory.CreateClient("Flutterwave");
            var response = await client.GetAsync($"/subscriptions?email={Uri.EscapeDataString(email)}");
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var subs) || subs.ValueKind != JsonValueKind.Array) return null;

            // Return the most recent active subscription
            foreach (var sub in subs.EnumerateArray())
            {
                var status = sub.TryGetProperty("status", out var st) ? st.GetString() : null;
                if (status == "active")
                    return sub.GetProperty("id").GetInt32().ToString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Flutterwave subscription for {Email}", email);
        }
        return null;
    }

    private async Task HandleSubscriptionCancelled(JsonElement payload)
    {
        if (!payload.TryGetProperty("data", out var data)) return;

        var subId = data.TryGetProperty("id", out var id) ? id.GetInt32().ToString() : null;
        if (string.IsNullOrEmpty(subId)) return;

        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.FlutterwaveSubscriptionId == subId);
        if (business == null) { _logger.LogWarning("No business for Flutterwave subscription {SubId}", subId); return; }

        business.FlutterwaveSubscriptionId = null;
        business.IsAutoRenew = false;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Flutterwave subscription cancelled: {Business}, access until {EndsAt}",
            business.Name, business.SubscriptionEndsAt);
    }

    private async Task HandleChargeCompleted(JsonElement payload)
    {
        if (!payload.TryGetProperty("data", out var data)) return;

        var status = data.TryGetProperty("status", out var s) ? s.GetString() : null;
        if (status != "successful") return;

        var txRef = data.TryGetProperty("tx_ref", out var tr) ? tr.GetString() : null;
        var flwId = data.TryGetProperty("id", out var id) ? id.GetInt64().ToString() : null;

        // Extract metadata
        if (!data.TryGetProperty("meta", out var meta)) return;
        var bizIdStr = meta.TryGetProperty("businessId", out var bi) ? bi.GetString() : null;
        var plan = meta.TryGetProperty("plan", out var pl) ? pl.GetString() : null;
        var billingCycle = meta.TryGetProperty("billingCycle", out var bc) ? bc.GetString() : null;
        var currency = meta.TryGetProperty("currency", out var cur) ? cur.GetString() : null;

        if (!Guid.TryParse(bizIdStr, out var businessId) || string.IsNullOrEmpty(plan)) return;

        // Idempotency check
        if (!string.IsNullOrEmpty(txRef))
        {
            var exists = await _db.PaystackEventLogs.AnyAsync(e => e.EventId == txRef);
            if (exists) { _logger.LogInformation("Duplicate Flutterwave event {TxRef}, skipping", txRef); return; }
            _db.PaystackEventLogs.Add(new PaystackEventLog { EventId = txRef, EventType = "flutterwave.charge.completed" });
        }

        var business = await _db.Businesses.FindAsync(businessId);
        if (business == null) { _logger.LogWarning("No business for Flutterwave payment {TxRef}", txRef); return; }

        var customerEmail = data.TryGetProperty("customer", out var cust)
            && cust.TryGetProperty("email", out var em) ? em.GetString() : null;
        var customerId = cust.TryGetProperty("id", out var ci) ? ci.GetInt64().ToString() : null;

        // Detect payment method — Flutterwave returns "card", "mobilemoney", "banktransfer", etc.
        var paymentType = data.TryGetProperty("payment_type", out var pt) ? pt.GetString()?.ToLower() : null;
        var isCard = paymentType is "card" or null;

        business.Plan = plan;
        business.SubscribedPlan = plan;
        business.BillingProvider = "flutterwave";
        business.BillingCycle = billingCycle ?? "monthly";
        business.BillingCurrency = currency ?? business.Currency;
        business.FlutterwaveCustomerId = customerId;
        business.PaymentMethod = paymentType ?? "card";
        business.IsAutoRenew = isCard;
        business.TrialEndsAt = null;

        // Set subscription end date based on cycle
        var isAnnual = billingCycle?.Equals("annual", StringComparison.OrdinalIgnoreCase) == true;
        business.SubscriptionEndsAt = isAnnual
            ? DateTime.UtcNow.AddYears(1)
            : DateTime.UtcNow.AddMonths(1);

        // For card payments with a payment plan, Flutterwave auto-creates a subscription.
        // Fetch it so we can cancel later if needed.
        if (isCard && !string.IsNullOrEmpty(customerEmail))
        {
            var subId = await FetchCustomerSubscriptionIdAsync(customerEmail);
            if (subId != null) business.FlutterwaveSubscriptionId = subId;
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("Flutterwave subscription activated: {Business} → {Plan} ({Cycle}/{Currency})",
            business.Name, plan, billingCycle, currency);

        // Send WhatsApp confirmation
        try
        {
            var owner = await _db.Users.FirstOrDefaultAsync(u =>
                u.BusinessId == businessId && u.Role == UserRole.Owner && u.IsActive);
            if (owner != null)
            {
                var planLabel = plan[0..1].ToUpper() + plan[1..];
                var price = BillingConfig.GetPrice(plan,
                    isAnnual ? BillingConfig.BillingCycle.Annual : BillingConfig.BillingCycle.Monthly,
                    currency ?? "USD");
                var formattedPrice = price.HasValue ? BillingConfig.FormatPrice(price.Value, currency ?? "USD") : "your selected plan";
                var cycleLabel = isAnnual ? "year" : "month";

                var renewalNote = isCard
                    ? $"Auto-renews on {business.SubscriptionEndsAt:dd MMM yyyy}."
                    : $"Expires on {business.SubscriptionEndsAt:dd MMM yyyy}. You'll need to renew manually.";

                var whatsApp = _serviceProvider.GetRequiredService<IWhatsAppService>();
                await whatsApp.SendMessageAsync(
                    $"whatsapp:{owner.PhoneNumber}",
                    $"✅ *Payment successful!*\n\n" +
                    $"Your *{planLabel}* plan is now active at {formattedPrice}/{cycleLabel}.\n\n" +
                    $"{renewalNote}\n\n" +
                    $"Say *my plan* to see your features.",
                    businessId, owner.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send Flutterwave payment confirmation for {Business}", business.Name);
        }
    }
}

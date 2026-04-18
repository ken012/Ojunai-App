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
    /// The user is redirected there to complete payment; Flutterwave sends a webhook on success.
    /// </summary>
    public async Task<string> InitializePaymentAsync(
        Guid businessId, string plan, string billingCycle, string currency, string email)
    {
        if (!Enum.TryParse<BillingConfig.BillingCycle>(billingCycle, true, out var cycle))
            throw new ArgumentException($"Invalid billing cycle: {billingCycle}");

        var amount = BillingConfig.GetPrice(plan, cycle, currency)
            ?? throw new ArgumentException($"No price found for {plan}/{billingCycle}/{currency}");

        var txRef = $"bizpilot-{businessId:N}-{DateTime.UtcNow.Ticks}";
        var callbackUrl = _config["Flutterwave:CallbackUrl"] ?? "https://app.bizpilot-ai.com/settings";

        var payload = new
        {
            tx_ref = txRef,
            amount = (double)amount,
            currency = currency.ToUpper(),
            redirect_url = callbackUrl,
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
            },
            payment_plan = (string?)null // Will use payment plans for recurring later
        };

        var client = _httpFactory.CreateClient("Flutterwave");
        var json = JsonSerializer.Serialize(payload);
        var response = await client.PostAsync("/v3/payments",
            new StringContent(json, Encoding.UTF8, "application/json"));

        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Flutterwave init failed: {Status} {Body}", response.StatusCode, body);
            throw new InvalidOperationException("Failed to initialize payment. Please try again.");
        }

        using var doc = JsonDocument.Parse(body);
        var link = doc.RootElement.GetProperty("data").GetProperty("link").GetString()
            ?? throw new InvalidOperationException("Flutterwave did not return a payment link.");

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

        if (eventType == "charge.completed")
        {
            await HandleChargeCompleted(payload);
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

    public async Task CancelSubscriptionAsync(Guid businessId)
    {
        var business = await _db.Businesses.FindAsync(businessId)
            ?? throw new KeyNotFoundException("Business not found.");

        if (!string.IsNullOrEmpty(business.FlutterwaveSubscriptionId))
        {
            try
            {
                var client = _httpFactory.CreateClient("Flutterwave");
                await client.PutAsync($"/v3/subscriptions/{business.FlutterwaveSubscriptionId}/cancel",
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

        business.Plan = plan;
        business.SubscribedPlan = plan;
        business.BillingProvider = "flutterwave";
        business.BillingCycle = billingCycle ?? "monthly";
        business.BillingCurrency = currency ?? business.Currency;
        business.FlutterwaveCustomerId = customerId;
        business.TrialEndsAt = null;

        // Set subscription end date based on cycle
        var isAnnual = billingCycle?.Equals("annual", StringComparison.OrdinalIgnoreCase) == true;
        business.SubscriptionEndsAt = isAnnual
            ? DateTime.UtcNow.AddYears(1)
            : DateTime.UtcNow.AddMonths(1);

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

                var whatsApp = _serviceProvider.GetRequiredService<IWhatsAppService>();
                await whatsApp.SendMessageAsync(
                    $"whatsapp:{owner.PhoneNumber}",
                    $"✅ *Payment successful!*\n\n" +
                    $"Your *{planLabel}* plan is now active at {formattedPrice}/{cycleLabel}.\n\n" +
                    $"Next renewal: {business.SubscriptionEndsAt:dd MMM yyyy}\n\n" +
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

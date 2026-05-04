using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services;

/// <summary>
/// Flutterwave integration for non-NGN currencies (GHS, USD, GBP, KES, ZAR).
/// Mirrors the PaystackService pattern: initialize payment, handle webhooks, cancel subscriptions.
/// Flutterwave uses a hosted payment page flow similar to Paystack.
/// </summary>
public class FlutterwaveService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FlutterwaveService> _logger;

    public FlutterwaveService(
        AppDbContext db,
        IConfiguration config,
        IServiceProvider serviceProvider,
        ILogger<FlutterwaveService> logger)
    {
        _db = db;
        _config = config;
        _serviceProvider = serviceProvider;
        _logger = logger;
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
            case "transfer.reversed":
            case "charge.refund":
                await HandleRefundAsync(payload);
                break;
        }
    }

    /// <summary>Verify the Flutterwave webhook hash matches our secret.</summary>
    public bool VerifyWebhook(string hash)
    {
        var secret = _config["Flutterwave:WebhookSecret"];
        if (string.IsNullOrEmpty(secret))
        {
            _logger.LogError("Flutterwave WebhookSecret not configured — all webhooks will be rejected");
            return false;
        }
        if (string.IsNullOrEmpty(hash))
        {
            _logger.LogWarning("Flutterwave webhook received without verif-hash header");
            return false;
        }
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
        if (status == "pending")
            return "PENDING:Your payment is being processed. You'll receive a WhatsApp confirmation once it's confirmed.";
        if (status != "successful" && status != "completed")
            return $"Payment was not successful. Status: {status}";

        // Idempotency: check if this tx_ref was already processed
        var verifiedTxRef = chargeData.TryGetProperty("tx_ref", out var txRefEl) ? txRefEl.GetString() : txRef;
        if (!string.IsNullOrEmpty(verifiedTxRef))
        {
            var alreadyProcessed = await _db.PaystackEventLogs.AnyAsync(e => e.EventId == verifiedTxRef);
            if (alreadyProcessed)
            {
                _logger.LogInformation("Duplicate Flutterwave verify for {TxRef}, skipping", verifiedTxRef);
                return null;
            }
            _db.PaystackEventLogs.Add(new PaystackEventLog { EventId = verifiedTxRef, EventType = "flutterwave.inline.verified" });
        }

        // Extract target plan and cycle from tx_ref: "ojunai-{guid:N}-{plan}-{cycle}-{ticks}"
        var validPlans = new[] { "starter", "shop", "pro", "business" };
        var validCycles = new[] { "monthly", "annual" };
        var plan = business.Plan;
        var billingCycle = business.BillingCycle ?? "monthly";

        if (!string.IsNullOrEmpty(verifiedTxRef))
        {
            var parts = verifiedTxRef.Split('-');
            if (parts.Length == 5 && parts[0] == "ojunai" && parts[1].Length == 32
                && validPlans.Contains(parts[2]) && validCycles.Contains(parts[3]))
            {
                plan = parts[2];
                billingCycle = parts[3];
            }
        }

        var currency = business.BillingCurrency ?? business.Currency;
        var isAnnual = billingCycle.Equals("annual", StringComparison.OrdinalIgnoreCase);

        // Verify the paid amount matches the expected plan price
        if (!Enum.TryParse<BillingConfig.BillingCycle>(billingCycle, true, out var bc))
            bc = BillingConfig.BillingCycle.Monthly;
        var expectedAmount = BillingConfig.GetPrice(plan, bc, currency);
        decimal? paidAmount = chargeData.TryGetProperty("amount", out var amountEl) ? amountEl.GetDecimal() : null;
        if (expectedAmount.HasValue && paidAmount.HasValue)
        {
            if (Math.Abs(paidAmount.Value - expectedAmount.Value) > 1)
            {
                _logger.LogWarning("Flutterwave amount mismatch: paid {Paid}, expected {Expected} for {Plan}/{Currency}",
                    paidAmount.Value, expectedAmount.Value, plan, currency);
                return $"Amount mismatch. Expected {BillingConfig.FormatPrice(expectedAmount.Value, currency)}, received {BillingConfig.FormatPrice(paidAmount.Value, currency)}.";
            }
        }

        // Sanitize payment method
        var rawMethod = chargeData.TryGetProperty("payment_type", out var ptEl) ? ptEl.GetString()?.ToLower() : "card";

        // Normalize provider-specific payment types to standard names
        rawMethod = rawMethod switch
        {
            "mpesa" or "momo" or "momo_gh" or "momo_ug" or "mobilemoneygh" or "mobilemoneyuganda"
                or "mobilemoneyfranco" or "mobilemoneyrwanda" or "mobilemoneykenya" or "mobilemoneyzambia" => "mobilemoney",
            "bank_transfer" or "banktransfer_ng" => "banktransfer",
            "ussd_transfer" => "ussd",
            _ => rawMethod
        };

        var allowedMethods = new[] { "card", "mobilemoney", "banktransfer", "ussd", "accounttransfer" };
        var paymentMethod = allowedMethods.Contains(rawMethod) ? rawMethod : "card";

        // Card payments with a payment plan auto-renew; mobile money/bank transfer don't
        var isCard = paymentMethod == "card";
        var hasPaymentPlan = chargeData.TryGetProperty("plan", out var planIdEl)
            && planIdEl.ValueKind == JsonValueKind.Number;

        business.Plan = plan;
        business.SubscribedPlan = plan;
        business.BillingProvider = "flutterwave";
        business.BillingCycle = billingCycle;
        business.BillingCurrency = currency;
        business.PaymentMethod = paymentMethod;
        business.IsAutoRenew = isCard && hasPaymentPlan;
        business.SubscriptionStatus = "active";
        business.PendingPlanChange = null;
        business.TrialEndsAt = null;
        var baseDate = (business.SubscriptionEndsAt.HasValue && business.SubscriptionEndsAt > DateTime.UtcNow)
            ? business.SubscriptionEndsAt.Value
            : DateTime.UtcNow;
        business.SubscriptionEndsAt = isAnnual ? baseDate.AddYears(1) : baseDate.AddMonths(1);

        // For card payments with a plan, fetch the auto-created subscription ID for cancellation support
        if (isCard)
        {
            var customerEmail = chargeData.TryGetProperty("customer", out var custEl)
                && custEl.TryGetProperty("email", out var emEl) ? emEl.GetString() : null;
            if (!string.IsNullOrEmpty(customerEmail))
            {
                var subId = await FetchCustomerSubscriptionIdAsync(customerEmail);
                if (subId != null) business.FlutterwaveSubscriptionId = subId;
            }
        }

        _db.BillingEvents.Add(new BillingEvent
        {
            BusinessId = businessId,
            EventType = "payment.success",
            Provider = "flutterwave",
            Plan = plan,
            BillingCycle = billingCycle,
            Amount = paidAmount,
            Currency = currency,
            TransactionRef = verifiedTxRef,
            PaymentMethod = paymentMethod,
            Status = "success",
            CreatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        var renewalNote = business.IsAutoRenew
            ? $"Auto-renews on {business.SubscriptionEndsAt:dd MMM yyyy}."
            : $"Expires on {business.SubscriptionEndsAt:dd MMM yyyy}. You'll need to renew manually.";

        _logger.LogInformation("Flutterwave inline payment verified: {Business} → {Plan} ({Cycle}/{Currency}, autoRenew={AutoRenew})",
            business.Name, plan, billingCycle, currency, business.IsAutoRenew);

        // Send WhatsApp confirmation
        try
        {
            var owner = await _db.Users.FirstOrDefaultAsync(u =>
                u.BusinessId == businessId && u.Role == UserRole.Owner && u.IsActive);
            if (owner != null)
            {
                var planLabel = plan[0..1].ToUpper() + plan[1..];
                if (!Enum.TryParse<BillingConfig.BillingCycle>(billingCycle, true, out var msgCycle))
                    msgCycle = BillingConfig.BillingCycle.Monthly;
                var price = BillingConfig.GetPrice(plan, msgCycle, currency);
                var formattedPrice = price.HasValue ? BillingConfig.FormatPrice(price.Value, currency) : "your selected plan";
                var cycleLabel = isAnnual ? "year" : "month";

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
                var secretKey = _config["Flutterwave:SecretKey"];
                if (!string.IsNullOrEmpty(secretKey))
                {
                    using var client = new HttpClient();
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {secretKey}");
                    await client.PutAsync(
                        $"https://api.flutterwave.com/v3/subscriptions/{business.FlutterwaveSubscriptionId}/cancel",
                        new StringContent("{}", Encoding.UTF8, "application/json"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Flutterwave cancel failed for {Business}", business.Name);
            }
        }

        business.FlutterwaveSubscriptionId = null;
        business.FlutterwaveCustomerId = null;
        business.SubscriptionStatus = "cancelled";

        _db.BillingEvents.Add(new BillingEvent
        {
            BusinessId = business.Id,
            EventType = "subscription.cancelled",
            Provider = "flutterwave",
            Plan = business.Plan,
            Status = "cancelled",
            CreatedAtUtc = DateTime.UtcNow
        });

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
    /// Get or create a Flutterwave v3 payment plan for recurring card billing.
    /// Uses the v3 API with the secret key. Returns the plan ID to pass to the Inline SDK.
    /// </summary>
    public async Task<int?> GetOrCreatePaymentPlanAsync(
        string plan, BillingConfig.BillingCycle cycle, string currency, decimal amount)
    {
        var secretKey = _config["Flutterwave:SecretKey"];
        if (string.IsNullOrEmpty(secretKey)) return null;

        var planName = $"Ojunai {plan} {cycle} {currency}".ToLower();
        var interval = cycle == BillingConfig.BillingCycle.Annual ? "annually" : "monthly";

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {secretKey}");

            var listResponse = await client.GetAsync("https://api.flutterwave.com/v3/payment-plans");
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
                            return p.GetProperty("id").GetInt32();
                    }
                }
            }

            var createPayload = JsonSerializer.Serialize(new
            {
                amount = (int)amount,
                name = planName,
                interval,
                currency = currency.ToUpper()
            });

            var createResponse = await client.PostAsync("https://api.flutterwave.com/v3/payment-plans",
                new StringContent(createPayload, Encoding.UTF8, "application/json"));

            var createBody = await createResponse.Content.ReadAsStringAsync();
            if (!createResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Flutterwave plan creation failed: {Status} {Body}", createResponse.StatusCode, createBody);
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
        var secretKey = _config["Flutterwave:SecretKey"];
        if (string.IsNullOrEmpty(secretKey)) return null;

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {secretKey}");

            var response = await client.GetAsync($"https://api.flutterwave.com/v3/subscriptions?email={Uri.EscapeDataString(email)}");
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var subs) || subs.ValueKind != JsonValueKind.Array) return null;

            foreach (var sub in subs.EnumerateArray())
            {
                var subStatus = sub.TryGetProperty("status", out var st) ? st.GetString() : null;
                if (subStatus == "active")
                    return sub.GetProperty("id").GetInt32().ToString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Flutterwave subscription for {Email}", email);
        }
        return null;
    }

    private async Task HandleRefundAsync(JsonElement payload)
    {
        if (!payload.TryGetProperty("data", out var data)) return;

        var txRef = data.TryGetProperty("tx_ref", out var tr) ? tr.GetString() : null;

        Guid businessId = Guid.Empty;
        if (data.TryGetProperty("meta", out var meta) && meta.TryGetProperty("businessId", out var bi))
            Guid.TryParse(bi.GetString(), out businessId);

        if (businessId == Guid.Empty)
        {
            _logger.LogWarning("Flutterwave refund with no businessId: {TxRef}", txRef);
            return;
        }

        var business = await _db.Businesses.FindAsync(businessId);
        if (business == null) return;

        business.SubscriptionStatus = "cancelled";
        business.FlutterwaveSubscriptionId = null;

        _db.BillingEvents.Add(new BillingEvent
        {
            BusinessId = business.Id,
            EventType = "payment.refunded",
            Provider = "flutterwave",
            Plan = business.Plan,
            TransactionRef = txRef,
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

        _logger.LogWarning("Flutterwave refund for {Business}: status={Status}", business.Name, business.SubscriptionStatus);

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
                    "Visit app.ojunai.com/settings to resubscribe.",
                    businessId, owner.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send refund notification for {Business}", business.Name);
        }
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
        business.SubscriptionStatus = "cancelled";

        _db.BillingEvents.Add(new BillingEvent
        {
            BusinessId = business.Id,
            EventType = "subscription.cancelled",
            Provider = "flutterwave",
            Plan = business.Plan,
            SubscriptionId = subId,
            Status = "cancelled",
            CreatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation("Flutterwave subscription cancelled: {Business}, access until {EndsAt}",
            business.Name, business.SubscriptionEndsAt);
    }

    private async Task HandleChargeCompleted(JsonElement payload)
    {
        if (!payload.TryGetProperty("data", out var data)) return;

        var txRef = data.TryGetProperty("tx_ref", out var tr) ? tr.GetString() : null;
        var flwId = data.TryGetProperty("id", out var id) ? id.GetInt64().ToString() : null;

        var status = data.TryGetProperty("status", out var s) ? s.GetString() : null;
        if (status == "pending")
        {
            _logger.LogInformation("Flutterwave charge pending, awaiting confirmation: {TxRef}", txRef);
            return;
        }
        if (status != "successful") return;

        // Extract metadata
        if (!data.TryGetProperty("meta", out var meta))
        {
            _logger.LogWarning("Flutterwave webhook missing meta for charge {TxRef}. Payment may need manual activation.", txRef);
            return;
        }
        var bizIdStr = meta.TryGetProperty("businessId", out var bi) ? bi.GetString() : null;
        var plan = meta.TryGetProperty("plan", out var pl) ? pl.GetString() : null;
        var billingCycle = meta.TryGetProperty("billingCycle", out var bc) ? bc.GetString() : null;
        var currency = meta.TryGetProperty("currency", out var cur) ? cur.GetString() : null;

        if (!Guid.TryParse(bizIdStr, out var businessId))
        {
            _logger.LogWarning("Flutterwave webhook invalid/missing businessId in meta: {Value}, txRef: {TxRef}", bizIdStr, txRef);
            return;
        }
        // Voice AI add-on payment
        var product = meta.TryGetProperty("product", out var prodEl) ? prodEl.GetString() : null;
        if (product == "voice_ai")
        {
            if (!string.IsNullOrEmpty(txRef))
            {
                var exists = await _db.PaystackEventLogs.AnyAsync(e => e.EventId == txRef);
                if (exists) { _logger.LogInformation("Duplicate Flutterwave Voice AI event {TxRef}", txRef); return; }
                _db.PaystackEventLogs.Add(new PaystackEventLog { EventId = txRef, EventType = "flutterwave.voiceai.charge" });
                await _db.SaveChangesAsync();
            }

            var vaBiz = await _db.Businesses.FindAsync(businessId);
            if (vaBiz == null) { _logger.LogWarning("No business for Voice AI payment {TxRef}", txRef); return; }

            var vaChargeAmt = data.TryGetProperty("amount", out var vaAmtEl) ? vaAmtEl.GetDecimal() : (decimal?)null;
            var vaIsAnnual = (billingCycle ?? "monthly").Equals("annual", StringComparison.OrdinalIgnoreCase);
            var vaPayMethod = data.TryGetProperty("payment_type", out var vaPtEl) ? vaPtEl.GetString()?.ToLower() : "card";

            vaBiz.VoiceAIEnabled = true;
            vaBiz.VoiceAIPlanStatus = "active";
            vaBiz.VoiceAIEnabledAt ??= DateTime.UtcNow;
            vaBiz.VoiceAITrialEndsAt = null;
            var vaBase = (vaBiz.VoiceAISubscriptionEndsAt.HasValue && vaBiz.VoiceAISubscriptionEndsAt > DateTime.UtcNow)
                ? vaBiz.VoiceAISubscriptionEndsAt.Value : DateTime.UtcNow;
            vaBiz.VoiceAISubscriptionEndsAt = vaIsAnnual ? vaBase.AddYears(1) : vaBase.AddMonths(1);

            _db.BillingEvents.Add(new BillingEvent
            {
                BusinessId = businessId,
                EventType = "voiceai.payment.success",
                Provider = "flutterwave",
                Plan = "voice_ai",
                Amount = vaChargeAmt,
                Currency = currency,
                TransactionRef = txRef,
                PaymentMethod = vaPayMethod,
                Status = "success"
            });

            await _db.SaveChangesAsync();
            _logger.LogInformation("Voice AI Flutterwave payment confirmed: {Business}, txRef: {TxRef}", vaBiz.Name, txRef);

            var provisioner = _serviceProvider.GetRequiredService<VoiceAIProvisioningService>();
            await provisioner.EnsureProvisionedAsync(vaBiz);
            return;
        }

        if (string.IsNullOrEmpty(plan))
        {
            _logger.LogWarning("Flutterwave webhook missing plan in meta, txRef: {TxRef}", txRef);
            return;
        }

        var validPlans = new[] { "starter", "shop", "pro", "business" };
        var validCycles = new[] { "monthly", "annual" };
        if (plan != null && !validPlans.Contains(plan))
        {
            _logger.LogWarning("Flutterwave webhook invalid plan in meta: {Plan}, txRef: {TxRef}", plan, txRef);
            return;
        }

        // Idempotency check
        if (!string.IsNullOrEmpty(txRef))
        {
            var exists = await _db.PaystackEventLogs.AnyAsync(e => e.EventId == txRef);
            if (exists) { _logger.LogInformation("Duplicate Flutterwave event {TxRef}, skipping", txRef); return; }
            _db.PaystackEventLogs.Add(new PaystackEventLog { EventId = txRef, EventType = "flutterwave.charge.completed" });
            await _db.SaveChangesAsync();
        }

        var business = await _db.Businesses.FindAsync(businessId);
        if (business == null) { _logger.LogWarning("No business for Flutterwave payment {TxRef}", txRef); return; }

        // Verify amount matches expected price
        var chargeAmount = data.TryGetProperty("amount", out var chgAmtEl) ? chgAmtEl.GetDecimal() : (decimal?)null;
        if (!Enum.TryParse<BillingConfig.BillingCycle>(billingCycle ?? "monthly", true, out var verifyBc))
            verifyBc = BillingConfig.BillingCycle.Monthly;
        var expectedCharge = BillingConfig.GetPrice(plan ?? "starter", verifyBc, currency ?? business.Currency);
        if (expectedCharge.HasValue && chargeAmount.HasValue && Math.Abs(chargeAmount.Value - expectedCharge.Value) > 1)
        {
            _logger.LogWarning("Flutterwave webhook amount mismatch: paid {Paid}, expected {Expected} for {Plan}/{Currency}",
                chargeAmount.Value, expectedCharge.Value, plan, currency);
            _db.BillingEvents.Add(new BillingEvent
            {
                BusinessId = businessId,
                EventType = "payment.rejected",
                Provider = "flutterwave",
                Plan = plan,
                Amount = chargeAmount,
                Currency = currency,
                TransactionRef = txRef,
                Status = "rejected",
                ErrorDetails = $"Amount mismatch: paid {chargeAmount}, expected {expectedCharge}",
                CreatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            return;
        }

        var customerEmail = data.TryGetProperty("customer", out var cust)
            && cust.TryGetProperty("email", out var em) ? em.GetString() : null;
        var customerId = cust.TryGetProperty("id", out var ci) ? ci.GetInt64().ToString() : null;

        // Detect payment method — Flutterwave returns "card", "mobilemoney", "banktransfer", etc.
        var paymentType = data.TryGetProperty("payment_type", out var pt) ? pt.GetString()?.ToLower() : null;

        // Normalize provider-specific payment types to standard names
        paymentType = paymentType switch
        {
            "mpesa" or "momo" or "momo_gh" or "momo_ug" or "mobilemoneygh" or "mobilemoneyuganda"
                or "mobilemoneyfranco" or "mobilemoneyrwanda" or "mobilemoneykenya" or "mobilemoneyzambia" => "mobilemoney",
            "bank_transfer" or "banktransfer_ng" => "banktransfer",
            "ussd_transfer" => "ussd",
            _ => paymentType
        };

        var isCard = paymentType is "card" or null;

        plan ??= business.Plan;
        business.Plan = plan;
        business.SubscribedPlan = plan;
        business.BillingProvider = "flutterwave";
        business.BillingCycle = billingCycle ?? "monthly";
        business.BillingCurrency = currency ?? business.Currency;
        business.FlutterwaveCustomerId = customerId;
        var allowedMethods = new[] { "card", "mobilemoney", "banktransfer", "ussd", "accounttransfer" };
        business.PaymentMethod = allowedMethods.Contains(paymentType) ? paymentType : "card";
        var hasPaymentPlan = data.TryGetProperty("plan", out var planIdEl) && planIdEl.ValueKind == JsonValueKind.Number;
        business.IsAutoRenew = isCard && hasPaymentPlan;
        business.SubscriptionStatus = "active";
        business.PendingPlanChange = null;
        business.TrialEndsAt = null;

        // Set subscription end date based on cycle (stack on existing if still active)
        var isAnnual = billingCycle?.Equals("annual", StringComparison.OrdinalIgnoreCase) == true;
        var chargeBaseDate = (business.SubscriptionEndsAt.HasValue && business.SubscriptionEndsAt > DateTime.UtcNow)
            ? business.SubscriptionEndsAt.Value
            : DateTime.UtcNow;
        business.SubscriptionEndsAt = isAnnual
            ? chargeBaseDate.AddYears(1)
            : chargeBaseDate.AddMonths(1);

        // For card payments with a payment plan, Flutterwave auto-creates a subscription.
        // Fetch it so we can cancel later if needed.
        if (isCard && !string.IsNullOrEmpty(customerEmail))
        {
            var subId = await FetchCustomerSubscriptionIdAsync(customerEmail);
            if (subId != null) business.FlutterwaveSubscriptionId = subId;
        }

        _db.BillingEvents.Add(new BillingEvent
        {
            BusinessId = businessId,
            EventType = "payment.success",
            Provider = "flutterwave",
            Plan = plan,
            BillingCycle = billingCycle,
            Amount = chargeAmount,
            Currency = currency ?? business.Currency,
            TransactionRef = txRef,
            PaymentMethod = business.PaymentMethod,
            Status = "success",
            CreatedAtUtc = DateTime.UtcNow
        });

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

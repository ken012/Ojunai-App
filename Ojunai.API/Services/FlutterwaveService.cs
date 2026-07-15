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
    private readonly IHttpClientFactory _httpFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FlutterwaveService> _logger;
    private readonly IActivityLogger _activity;

    public FlutterwaveService(
        AppDbContext db,
        IConfiguration config,
        IHttpClientFactory httpFactory,
        IServiceProvider serviceProvider,
        ILogger<FlutterwaveService> logger,
        IActivityLogger activity)
    {
        _db = db;
        _config = config;
        _httpFactory = httpFactory;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _activity = activity;
    }

    /// <summary>
    /// Process a Flutterwave webhook event. Verifies the transaction via the Flutterwave API,
    /// then activates the subscription if payment was successful.
    /// </summary>
    public async Task HandleWebhookAsync(JsonElement payload)
    {
        var eventType = payload.TryGetProperty("event", out var evt) ? evt.GetString() : null;
        _logger.LogInformation("Flutterwave webhook: {Event}", eventType);

        try
        {
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
        catch (DbUpdateException ex) when (IsDuplicateEventRace(ex))
        {
            // A concurrent delivery of the SAME event won the race on PaystackEventLog.EventId and
            // our SaveChanges rolled back atomically (no partial activation). Treat as a duplicate —
            // return 200 instead of 500-ing into a Flutterwave retry storm.
            _logger.LogInformation("Flutterwave webhook concurrent duplicate ignored: {Event}", eventType);
        }
    }

    /// <summary>
    /// True when a SaveChanges failed specifically because the idempotency row (PaystackEventLog)
    /// hit its unique index — i.e. a concurrent delivery of the same event won. Other unique
    /// violations are NOT swallowed.
    /// </summary>
    private static bool IsDuplicateEventRace(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation }
        && ex.Entries.Any(e => e.Entity is PaystackEventLog);

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
    /// Fail-closed amount check: true ONLY when both expected and paid amounts are known and within
    /// tolerance. A null expected price (e.g. an unsupported/attacker-chosen currency) or a missing paid
    /// amount returns false, so callers REJECT rather than activate a subscription for an unverifiable sum.
    /// </summary>
    internal static bool IsPaidAmountAcceptable(decimal? expected, decimal? paid, decimal tolerance)
        => expected.HasValue && paid.HasValue && Math.Abs(paid.Value - expected.Value) <= tolerance;

    /// <summary>
    /// Server-to-server confirmation of a Flutterwave transaction. The webhook body is authenticated
    /// only by a STATIC shared secret (verif-hash) — not an HMAC over the payload — so if that secret
    /// leaks (it is sent in plaintext on every delivery), an attacker can forge a "charge.completed"
    /// with any businessId/plan/amount. Payment state must therefore never be trusted from the payload
    /// alone: this re-fetches the transaction from Flutterwave's API using the secret key and returns
    /// the authoritative status/amount/currency. Returns Verified=false on ANY failure so callers fail
    /// closed (a forged transaction id will not resolve, and the reconciliation job retries real ones).
    /// </summary>
    private async Task<(bool Verified, string? Status, decimal? Amount, string? Currency)> VerifyChargeWithApiAsync(string? flwId)
    {
        if (string.IsNullOrEmpty(flwId)) return (false, null, null, null);
        var secretKey = _config["Flutterwave:SecretKey"];
        if (string.IsNullOrEmpty(secretKey))
        {
            _logger.LogError("Flutterwave SecretKey not configured — cannot server-verify webhook transaction {FlwId}", flwId);
            return (false, null, null, null);
        }
        try
        {
            var httpClient = _httpFactory.CreateClient("Flutterwave");
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {secretKey}");
            var response = await httpClient.GetAsync(
                $"https://api.flutterwave.com/v3/transactions/{Uri.EscapeDataString(flwId)}/verify");
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Flutterwave verify API returned {Status} for transaction {FlwId}", response.StatusCode, flwId);
                return (false, null, null, null);
            }
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var vd)) return (false, null, null, null);
            var vStatus = vd.TryGetProperty("status", out var vs) ? vs.GetString() : null;
            decimal? vAmount = vd.TryGetProperty("amount", out var va) ? va.GetDecimal() : (decimal?)null;
            var vCurrency = vd.TryGetProperty("currency", out var vc) ? vc.GetString() : null;
            return (true, vStatus, vAmount, vCurrency);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Flutterwave verify API threw for transaction {FlwId}", flwId);
            return (false, null, null, null);
        }
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

        var httpClient = _httpFactory.CreateClient("Flutterwave");
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

        // WhatsApp pack purchase — tx_ref pattern "ojunai-pack-{packCode}-{guid:N}-{ticks}".
        // Handle this BEFORE the tier-plan parsing below so a pack purchase doesn't accidentally
        // mutate business.Plan.
        if (!string.IsNullOrEmpty(verifiedTxRef) && verifiedTxRef.StartsWith("ojunai-pack-", StringComparison.Ordinal))
        {
            var packResult = await HandleWhatsAppPackVerifiedAsync(businessId, business, verifiedTxRef, chargeData);
            return packResult;
        }

        // Extract target plan and cycle from tx_ref: "ojunai-{guid:N}-{plan}-{cycle}-{ticks}"
        var validPlans = new[] { "starter", "lite", "operator", "pro", "scale" };
        var validCycles = new[] { "monthly", "annual" };
        var plan = business.Plan;
        var billingCycle = business.BillingCycle ?? "monthly";

        if (!string.IsNullOrEmpty(verifiedTxRef))
        {
            var parts = verifiedTxRef.Split('-');
            if (parts.Length == 5 && parts[0] == "ojunai" && parts[1].Length == 32
                && validPlans.Contains(parts[2]) && validCycles.Contains(parts[3]))
            {
                // The tx_ref embeds the businessId that INITIATED checkout (SubscriptionController sets it
                // at /initialize). Require it to match the authenticated caller so a user can't claim
                // another tenant's completed transaction to activate their own plan (and consume the
                // victim's single-use tx_ref in the process).
                if (!string.Equals(parts[1], businessId.ToString("N"), StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Flutterwave verify: tx_ref businessId {TxBiz} does not match caller {Caller}; rejecting.",
                        parts[1], businessId.ToString("N"));
                    return "This transaction belongs to a different account.";
                }
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
        // FAIL CLOSED: reject when the expected price can't be computed or the paid amount is missing
        // or mismatched, rather than silently activating. (Previously a null expected price bypassed it.)
        if (!IsPaidAmountAcceptable(expectedAmount, paidAmount, 1m))
        {
            _logger.LogWarning("Flutterwave amount not acceptable: paid {Paid}, expected {Expected} for {Plan}/{Currency}; rejecting.",
                paidAmount, expectedAmount, plan, currency);
            return expectedAmount.HasValue && paidAmount.HasValue
                ? $"Amount mismatch. Expected {BillingConfig.FormatPrice(expectedAmount.Value, currency)}, received {BillingConfig.FormatPrice(paidAmount.Value, currency)}."
                : "Could not confirm the payment amount. Please contact support if you were charged.";
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
                    var client = _httpFactory.CreateClient("Flutterwave");
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

        var cancelledPlan = business.Plan;   // snapshot before the revert-to-starter block below
        if (business.SubscriptionEndsAt == null || business.SubscriptionEndsAt <= DateTime.UtcNow)
        {
            business.Plan = "starter";
            business.SubscribedPlan = "starter";
            business.PendingPlanChange = null;
            business.SubscriptionEndsAt = null;
            business.TrialEndsAt = null;
        }

        await _activity.LogAsync(businessId, "subscription.cancelled", "Billing", null, cancelledPlan,
            "cancelled subscription");

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
            var client = _httpFactory.CreateClient("Flutterwave");
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
                // Send the exact price (major units, 2dp). Do NOT cast to int — that truncated the
                // cents on decimal currencies (USD/GBP), creating recurring plans that under-charged
                // by up to ~0.99 each cycle (e.g. $11.99 → $11). Whole-number currencies are unaffected.
                amount = Math.Round(amount, 2),
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
            var client = _httpFactory.CreateClient("Flutterwave");
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

        // ── Server-side confirmation (do NOT trust the webhook payload) ──────────────────────────
        // The webhook is authenticated only by a static shared secret, so re-verify the transaction
        // against Flutterwave's API before mutating ANY subscription/add-on state. Fail closed: if the
        // transaction can't be independently confirmed as successful, skip activation — a forged event
        // names a transaction id the API won't confirm, and the daily reconciliation job retries genuine
        // ones. The verified amount/currency below are the authoritative values used for price checks.
        var verified = await VerifyChargeWithApiAsync(flwId);
        if (!verified.Verified || (verified.Status != "successful" && verified.Status != "completed"))
        {
            _logger.LogWarning(
                "Flutterwave charge {TxRef} (flwId {FlwId}) could not be server-confirmed (status {Status}); skipping activation.",
                txRef, flwId, verified.Status);
            return;
        }

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
                // Track but don't commit — rides with the activation SaveChanges below so the event
                // is marked seen iff the activation also commits (avoids paid-but-not-activated).
                _db.PaystackEventLogs.Add(new PaystackEventLog { EventId = txRef, EventType = "flutterwave.voiceai.charge" });
            }

            var vaBiz = await _db.Businesses.FindAsync(businessId);
            if (vaBiz == null) { _logger.LogWarning("No business for Voice AI payment {TxRef}", txRef); return; }

            var vaChargeAmt = data.TryGetProperty("amount", out var vaAmtEl) ? vaAmtEl.GetDecimal() : (decimal?)null;
            var vaIsAnnual = (billingCycle ?? "monthly").Equals("annual", StringComparison.OrdinalIgnoreCase);
            var vaPayMethod = data.TryGetProperty("payment_type", out var vaPtEl) ? vaPtEl.GetString()?.ToLower() : "card";
            // Tier comes through meta.tier (set on /voice-ai/initialize). Default to existing tier
            // so we don't clobber it on edge cases where meta is incomplete.
            var vaTier = meta.TryGetProperty("tier", out var vaTierEl) ? vaTierEl.GetString()?.ToLower() : null;
            if (string.IsNullOrEmpty(vaTier) || !BillingConfig.VoiceAITierCodes.Contains(vaTier))
                vaTier = vaBiz.VoiceAITier;

            // FAIL CLOSED on amount, mirroring the plan branch. Use the SERVER-VERIFIED amount/currency
            // (never the webhook payload) and reject unless it matches the expected tier price. Without
            // this, a genuine-but-underpaid charge — or a meta.tier set higher than what was actually
            // paid — activated the Voice AI tier regardless of amount.
            var vaCycle = vaIsAnnual ? BillingConfig.BillingCycle.Annual : BillingConfig.BillingCycle.Monthly;
            var vaCurrency = verified.Currency ?? currency ?? vaBiz.BillingCurrency ?? vaBiz.Currency;
            var vaExpected = BillingConfig.GetVoiceAITierPrice(vaTier ?? "", vaCycle, vaCurrency);
            if (!IsPaidAmountAcceptable(vaExpected, verified.Amount, 0.5m))
            {
                _logger.LogWarning("Flutterwave Voice AI amount rejected: paid {Paid}, expected {Expected} for tier {Tier}/{Currency}",
                    verified.Amount, vaExpected, vaTier, vaCurrency);
                _db.BillingEvents.Add(new BillingEvent
                {
                    BusinessId = businessId,
                    EventType = "payment.rejected",
                    Provider = "flutterwave",
                    Plan = $"voice_ai.{vaTier ?? "unknown"}",
                    Amount = verified.Amount,
                    Currency = vaCurrency,
                    TransactionRef = txRef,
                    Status = "rejected",
                    ErrorDetails = $"Voice AI amount mismatch: paid {verified.Amount}, expected {vaExpected}",
                    CreatedAtUtc = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
                return;
            }

            vaBiz.VoiceAIEnabled = true;
            vaBiz.VoiceAIPlanStatus = "active";
            vaBiz.VoiceAIEnabledAt ??= DateTime.UtcNow;
            vaBiz.VoiceAITrialEndsAt = null;
            vaBiz.VoiceAITier = vaTier;
            vaBiz.VoiceAICycleMinutesUsed = 0;
            var vaBase = (vaBiz.VoiceAISubscriptionEndsAt.HasValue && vaBiz.VoiceAISubscriptionEndsAt > DateTime.UtcNow)
                ? vaBiz.VoiceAISubscriptionEndsAt.Value : DateTime.UtcNow;
            vaBiz.VoiceAISubscriptionEndsAt = vaIsAnnual ? vaBase.AddYears(1) : vaBase.AddMonths(1);

            _db.BillingEvents.Add(new BillingEvent
            {
                BusinessId = businessId,
                EventType = "voiceai.payment.success",
                Provider = "flutterwave",
                Plan = $"voice_ai.{vaTier ?? "unknown"}",
                Amount = vaChargeAmt,
                Currency = currency,
                TransactionRef = txRef,
                PaymentMethod = vaPayMethod,
                Status = "success"
            });

            await _db.SaveChangesAsync();
            _logger.LogInformation("Voice AI Flutterwave payment confirmed: {Business} ({Tier}), txRef: {TxRef}", vaBiz.Name, vaTier, txRef);

            var provisioner = _serviceProvider.GetRequiredService<VoiceAIProvisioningService>();
            await provisioner.EnsureProvisionedAsync(vaBiz);
            return;
        }

        if (string.IsNullOrEmpty(plan))
        {
            _logger.LogWarning("Flutterwave webhook missing plan in meta, txRef: {TxRef}", txRef);
            return;
        }

        var validPlans = new[] { "starter", "lite", "operator", "pro", "scale" };
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
            // Track but don't commit — rides with the activation/rejection SaveChanges below so the
            // event is marked seen iff that also commits (avoids paid-but-not-activated).
            _db.PaystackEventLogs.Add(new PaystackEventLog { EventId = txRef, EventType = "flutterwave.charge.completed" });
        }

        var business = await _db.Businesses.FindAsync(businessId);
        if (business == null) { _logger.LogWarning("No business for Flutterwave payment {TxRef}", txRef); return; }

        // A mid-cycle delta upgrade pays only (target − current) and keeps the cycle end date. The
        // "-delta-" marker is set server-side in the txRef at init, so the client can't forge it.
        var isDeltaUpgrade = txRef?.Contains("-delta-", StringComparison.Ordinal) == true;
        var priorPlan = business.SubscribedPlan; // plan we're upgrading FROM (captured before we overwrite it below)

        // Verify amount matches expected price. Amount + currency come from the SERVER-VERIFIED
        // transaction (see VerifyChargeWithApiAsync), never from the webhook payload / meta.
        var chargeAmount = verified.Amount;
        if (!Enum.TryParse<BillingConfig.BillingCycle>(billingCycle ?? "monthly", true, out var verifyBc))
            verifyBc = BillingConfig.BillingCycle.Monthly;
        var verifyCurrency = verified.Currency ?? business.Currency;
        var fullPrice = BillingConfig.GetPrice(plan ?? "starter", verifyBc, verifyCurrency);
        // For a delta upgrade, recompute the expected difference server-side and validate against THAT,
        // so a tampered request can't under-pay by faking the delta marker.
        decimal? expectedCharge = fullPrice;
        if (isDeltaUpgrade && fullPrice.HasValue)
        {
            var priorPrice = !string.IsNullOrEmpty(priorPlan)
                ? BillingConfig.GetPrice(priorPlan!, verifyBc, verifyCurrency) ?? 0
                : 0;
            expectedCharge = Math.Max(0, fullPrice.Value - priorPrice);
        }
        // FAIL CLOSED: reject unless we can compute an expected price AND the verified amount matches it.
        // Previously a null expected price (e.g. an unsupported currency) silently bypassed this check
        // and activated the plan for any amount. Tolerance 0.5 guards float/rounding echo only.
        if (!IsPaidAmountAcceptable(expectedCharge, chargeAmount, 0.5m))
        {
            _logger.LogWarning("Flutterwave webhook amount rejected: paid {Paid}, expected {Expected} for {Plan}/{Currency}",
                chargeAmount, expectedCharge, plan, currency);
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
        business.SubscriptionStatus = "active";
        business.PendingPlanChange = null;
        business.TrialEndsAt = null;

        var isAnnual = billingCycle?.Equals("annual", StringComparison.OrdinalIgnoreCase) == true;
        if (isDeltaUpgrade)
        {
            // Mid-cycle upgrade: only the difference was paid and the existing cycle end date stands.
            // No recurring plan backs a one-time delta, so auto-renew is off — the user re-subscribes at
            // expiry (the expiry banner prompts them). Mirrors the Paystack delta fallback path.
            business.IsAutoRenew = false;
        }
        else
        {
            business.IsAutoRenew = isCard && hasPaymentPlan;
            // Set subscription end date based on cycle (stack on existing if still active)
            var chargeBaseDate = (business.SubscriptionEndsAt.HasValue && business.SubscriptionEndsAt > DateTime.UtcNow)
                ? business.SubscriptionEndsAt.Value
                : DateTime.UtcNow;
            business.SubscriptionEndsAt = isAnnual
                ? chargeBaseDate.AddYears(1)
                : chargeBaseDate.AddMonths(1);
        }

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

    /// <summary>
    /// Build the tx_ref for a WhatsApp pack purchase. Pattern: ojunai-pack-{packCode}-{businessId:N}-{ticks}.
    /// Webhook + verify recognize this prefix and route to pack activation.
    /// </summary>
    /// <remarks>
    /// KNOWN GAP — Flutterwave WhatsApp pack auto-renew is not yet implemented:
    ///
    /// Flutterwave's inline-checkout subscription flow needs a payment_plan_id attached to
    /// the transaction so the card auto-renews. We have the infrastructure for tier
    /// subscriptions but haven't verified the meta-forwarding behavior on recurring charges
    /// for packs specifically. Two questions to confirm with Flutterwave before wiring it up:
    ///   1. Does inline-checkout meta (packCode, billingCycle) get forwarded to the renewal
    ///      charge.completed webhook? Their docs are inconsistent.
    ///   2. Can we reliably look up the renewal by tx_ref pattern or do we need the
    ///      subscription ID? FetchCustomerSubscriptionIdAsync is one route, but it's by
    ///      email — packs may collide with tier subs on the same customer.
    ///
    /// Until that's verified, Flutterwave pack purchases stay one-time regardless of the
    /// frontend's autoRenew flag. The frontend disables the auto-renew checkbox for non-NGN
    /// currencies and shows a "currently NGN only" note. Paystack pack auto-renew works
    /// end-to-end (see PaystackService.InitializeWhatsAppPackChargeAsync).
    /// </remarks>
    public static string BuildPackTxRef(string packCode, Guid businessId)
        => $"ojunai-pack-{packCode.ToLowerInvariant()}-{businessId:N}-{DateTime.UtcNow.Ticks}";

    /// <summary>
    /// Activate a WhatsApp pack after a verified Flutterwave charge. Validates amount matches
    /// the canonical pack price, cancels any existing active pack, upserts a new BusinessAddOn,
    /// logs a BillingEvent. Returns null on success or an error message string for the caller
    /// to surface (mirrors VerifyAndActivateAsync's return convention).
    /// </summary>
    private async Task<string?> HandleWhatsAppPackVerifiedAsync(
        Guid businessId, Business business, string txRef, JsonElement chargeData)
    {
        // Pattern: ojunai-pack-{packCode}-{guid:N}-{ticks}
        var parts = txRef.Split('-');
        if (parts.Length != 5 || parts[0] != "ojunai" || parts[1] != "pack")
            return "Malformed pack tx_ref.";
        var packCode = parts[2].ToLowerInvariant();
        if (!BillingConfig.WhatsAppPackCodes.Contains(packCode))
            return $"Unknown WhatsApp pack code: {packCode}.";

        // The tx_ref embeds the businessId that INITIATED checkout (parts[3]). Require it to match the
        // authenticated caller so a user can't claim another tenant's completed pack transaction to
        // activate a pack on their own account (and consume the victim's single-use tx_ref, denying
        // them the pack they paid for). Mirrors the tier-path guard in VerifyAndActivateAsync.
        if (!string.Equals(parts[3], businessId.ToString("N"), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Flutterwave pack verify: tx_ref businessId {TxBiz} does not match caller {Caller}; rejecting.",
                parts[3], businessId.ToString("N"));
            return "This transaction belongs to a different account.";
        }

        var currency = business.BillingCurrency ?? business.Currency ?? "USD";
        var cycle = (business.BillingCycle ?? "monthly").Equals("annual", StringComparison.OrdinalIgnoreCase)
            ? BillingConfig.BillingCycle.Annual
            : BillingConfig.BillingCycle.Monthly;
        var expected = BillingConfig.GetWhatsAppPackPrice(packCode, cycle, currency);
        if (!expected.HasValue)
            return $"No price for pack {packCode}/{cycle}/{currency}.";

        decimal? paid = chargeData.TryGetProperty("amount", out var amtEl) ? amtEl.GetDecimal() : null;
        if (paid.HasValue && Math.Abs(paid.Value - expected.Value) > 1)
        {
            _logger.LogWarning(
                "Flutterwave pack amount mismatch for {Business}/{Pack}: paid={Paid} expected={Expected}",
                business.Name, packCode, paid.Value, expected.Value);
            return $"Amount mismatch. Expected {BillingConfig.FormatPrice(expected.Value, currency)}, received {BillingConfig.FormatPrice(paid ?? 0, currency)}.";
        }

        // Cancel current active pack(s) and activate the new one ATOMICALLY, ordered so the partial
        // unique index (one active whatsapp_pack per business) isn't tripped by the in-flight old
        // row: cancel is an immediate UPDATE before the insert, inside one transaction. A rare
        // concurrent double-activation trips the index → rolls back (no partial state, no double
        // pack) and surfaces for retry. Mirrors PaystackService.UpsertWhatsAppPackAddOnAsync.
        var now = DateTime.UtcNow;
        await using var tx = await _db.Database.BeginTransactionAsync();

        await _db.BusinessAddOns
            .Where(a => a.BusinessId == businessId
                && a.Status == "active"
                && a.AddOnCode.StartsWith("whatsapp_pack."))
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, "cancelled")
                .SetProperty(a => a.CancelledAtUtc, now)
                .SetProperty(a => a.UpdatedAtUtc, now));

        var nextBilling = cycle == BillingConfig.BillingCycle.Annual ? now.AddYears(1) : now.AddMonths(1);
        _db.BusinessAddOns.Add(new BusinessAddOn
        {
            BusinessId = businessId,
            AddOnCode = $"whatsapp_pack.{packCode}",
            Status = "active",
            Quantity = 1,
            BilledAmount = expected.Value,
            BilledCurrency = currency,
            AddedAtUtc = now,
            NextBillingAtUtc = nextBilling,
            UpdatedAtUtc = now,
        });

        _db.BillingEvents.Add(new BillingEvent
        {
            BusinessId = businessId,
            EventType = "whatsapp_pack.activated",
            Provider = "flutterwave",
            Plan = $"whatsapp_pack.{packCode}",
            Amount = expected.Value,
            Currency = currency,
            PaymentMethod = "card",
            Status = "success"
        });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        _logger.LogInformation(
            "WhatsApp pack activated via Flutterwave: business={Business} pack={Pack} amount={Amount} {Currency}",
            business.Name, packCode, expected.Value, currency);
        return null;
    }
}

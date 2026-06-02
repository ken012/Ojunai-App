using System.Text;
using System.Text.Json;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services;

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

        // Drive everything from business.BillingCurrency (no literal "NGN").
        // Paystack only handles NGN today, so the controller routes by currency: NGN → here,
        // others → Flutterwave. We enforce that contract with a defensive guard — if a non-NGN
        // business reaches this method, the routing layer is broken.
        var billingCurrency = business.BillingCurrency ?? business.Currency ?? "NGN";
        if (BillingConfig.GetProvider(billingCurrency) != BillingConfig.BillingProvider.Paystack)
        {
            throw new InvalidOperationException(
                $"PaystackService received a {billingCurrency} business — that currency routes to a different provider. " +
                $"This indicates a routing bug in SubscriptionController.");
        }

        // Determine billing cycle and amount from BillingConfig
        var cycle = business.BillingCycle ?? "monthly";
        var isAnnual = cycle.Equals("annual", StringComparison.OrdinalIgnoreCase);
        var billingCycleEnum = isAnnual ? BillingConfig.BillingCycle.Annual : BillingConfig.BillingCycle.Monthly;
        // GetPriceOrThrow surfaces "no price configured" loudly instead of silently falling back to
        // PricePerMonth (which would be wrong if PricePerMonth and BillingConfig pricing ever diverge).
        var amount = BillingConfig.GetPriceOrThrow(plan, billingCycleEnum, billingCurrency);
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

        // Before creating a new transaction, disable any existing active subscriptions on this customer.
        // Without this, a user upgrading from Starter → Shop → Pro accumulates 3 active subscriptions
        // on Paystack, all billing concurrently. Idempotent / non-fatal — failures here log + continue.
        await DisableAllActiveSubscriptionsAsync(customerCode!, business.Name);

        // ── Mid-cycle upgrade pricing (Pattern B Lite) ──
        // Eligible when: user has an ACTIVE paid plan with future end date AND
        //                new plan costs more (real upgrade) AND
        //                we're within the first 3 weeks of the cycle (≥10 days remaining).
        // In that case, charge ONLY the price delta as a one-time charge. The user keeps
        // their existing SubscriptionEndsAt. Auto-renew breaks for this cycle — user must
        // re-subscribe at expiry (we'll surface a banner near expiry in a follow-up).
        // Annual cycles always go full-price (proration math is rarer + harder there).
        bool isDeltaUpgrade = false;
        decimal deltaAmount = 0;
        if (!isAnnual &&
            business.SubscriptionStatus?.Equals("active", StringComparison.OrdinalIgnoreCase) == true &&
            business.SubscriptionEndsAt.HasValue && business.SubscriptionEndsAt.Value > DateTime.UtcNow &&
            !string.IsNullOrEmpty(business.SubscribedPlan))
        {
            var currentPrice = BillingConfig.GetPrice(business.SubscribedPlan!, billingCycleEnum, billingCurrency) ?? 0;
            var daysRemaining = (business.SubscriptionEndsAt.Value - DateTime.UtcNow).TotalDays;
            // 30-day cycle, first 3 weeks = ≥10 days remaining
            if (amount > currentPrice && daysRemaining >= 10)
            {
                isDeltaUpgrade = true;
                deltaAmount = amount - currentPrice;
            }
        }

        if (isDeltaUpgrade)
        {
            // One-time delta charge. NO `plan` param so Paystack doesn't auto-create a subscription.
            // The webhook handler reads metadata.mode == "upgrade_delta" and updates the plan
            // without extending SubscriptionEndsAt.
            var deltaBody = new
            {
                email,
                amount = (int)(deltaAmount * 100),
                callback_url = $"{_config["App:DashboardUrl"] ?? "https://app.ojunai.com"}/settings?subscribed=true",
                metadata = new { businessId = businessId.ToString(), plan, mode = "upgrade_delta" }
            };
            var deltaResp = await PostAsync("/transaction/initialize", deltaBody);
            _logger.LogInformation("Mid-cycle delta upgrade for {Business}: {From} → {To} · ₦{Delta} (kept end date {EndsAt})",
                business.Name, business.SubscribedPlan, plan, deltaAmount, business.SubscriptionEndsAt);
            return deltaResp.GetProperty("data").GetProperty("authorization_url").GetString()!;
        }

        // Full-price flow (new subscriptions, downgrades, last-week-of-cycle upgrades, expired).
        var body = new
        {
            email,
            amount = (int)(amount * 100), // kobo
            plan = paystackPlanCode,
            callback_url = $"{_config["App:DashboardUrl"] ?? "https://app.ojunai.com"}/settings?subscribed=true",
            metadata = new { businessId = businessId.ToString(), plan }
        };

        var response = await PostAsync("/transaction/initialize", body);
        return response.GetProperty("data").GetProperty("authorization_url").GetString()!;
    }

    /// <summary>
    /// Disables every active Paystack subscription for the given customer code.
    /// Called before initializing a new subscription transaction so plan changes don't
    /// stack subs (Starter + Shop + Pro all billing). Errors are logged but non-fatal —
    /// we still want to proceed with the new transaction even if disable fails.
    /// </summary>
    /// <remarks>
    /// Two-step lookup: the list endpoint (GET /subscription?customer=...) may not return
    /// `email_token` in older API versions, but `/subscription/:code` always does. So we
    /// list to find subs, then fetch each by code to get its email_token before disabling.
    /// </remarks>
    private async Task DisableAllActiveSubscriptionsAsync(string customerCode, string businessName)
    {
        try
        {
            // List subs for this customer
            var listResp = await GetAsync($"/subscription?customer={Uri.EscapeDataString(customerCode)}&perPage=50");

            // Surface what Paystack actually returned so we can diagnose silent failures.
            var listOk = listResp.TryGetProperty("status", out var stEl) && stEl.ValueKind == JsonValueKind.True;
            if (!listOk)
            {
                var msg = listResp.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : "(no message)";
                _logger.LogWarning("Paystack list-subscriptions returned not-OK for customer {Customer}: {Message}", customerCode, msg);
                return;
            }

            if (!listResp.TryGetProperty("data", out var subs) || subs.ValueKind != JsonValueKind.Array)
            {
                _logger.LogInformation("No subscriptions array in Paystack list response for customer {Customer}", customerCode);
                return;
            }

            // Collect active subscription codes first (the iteration is over the list response;
            // fetching each individually below gets the email_token reliably)
            var activeSubCodes = new List<string>();
            foreach (var sub in subs.EnumerateArray())
            {
                var status = sub.TryGetProperty("status", out var st) ? st.GetString() : null;
                if (!string.Equals(status, "active", StringComparison.OrdinalIgnoreCase)) continue;
                var subCode = sub.TryGetProperty("subscription_code", out var sc) ? sc.GetString() : null;
                if (!string.IsNullOrEmpty(subCode)) activeSubCodes.Add(subCode);
            }

            _logger.LogInformation("Found {Count} active Paystack sub(s) for {Business} ({Customer}) to disable: {Codes}",
                activeSubCodes.Count, businessName, customerCode, string.Join(", ", activeSubCodes));

            int disabled = 0;
            foreach (var subCode in activeSubCodes)
            {
                try
                {
                    // Fetch the single subscription to guarantee we have email_token
                    var detail = await GetAsync($"/subscription/{Uri.EscapeDataString(subCode)}");
                    if (!detail.TryGetProperty("data", out var dt) || dt.ValueKind != JsonValueKind.Object)
                    {
                        _logger.LogWarning("Paystack /subscription/{Code} returned no data — skipping disable", subCode);
                        continue;
                    }

                    var emailToken = dt.TryGetProperty("email_token", out var et) ? et.GetString() : null;
                    if (string.IsNullOrEmpty(emailToken))
                    {
                        _logger.LogWarning("Paystack sub {Code} has no email_token in detail response — cannot disable", subCode);
                        continue;
                    }

                    await PostAsync("/subscription/disable", new { code = subCode, token = emailToken });
                    disabled++;
                    _logger.LogInformation("Disabled Paystack sub {Code} for {Business}", subCode, businessName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to disable Paystack sub {Code} for {Business}", subCode, businessName);
                }
            }

            _logger.LogInformation("Disable sweep complete for {Business}: {Disabled}/{Found} subs disabled",
                businessName, disabled, activeSubCodes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate Paystack subs for {Customer} — proceeding with new init", customerCode);
        }
    }

    public async Task<string> InitializeVoiceAIAsync(Guid businessId, string email, decimal amount, string currency, string cycle, string tier)
    {
        var business = await _db.Businesses.FindAsync(businessId)
            ?? throw new KeyNotFoundException("Business not found.");

        var isAnnual = cycle.Equals("annual", StringComparison.OrdinalIgnoreCase);
        var interval = isAnnual ? "annually" : "monthly";
        // Tier in the plan name so Paystack has a distinct subscription per tier (lets a merchant
        // switch tiers cleanly without colliding plan codes). Voice Starter ≠ Voice Pro at the
        // billing-provider level.
        var paystackPlanCode = await GetOrCreatePlanAsync($"voice-ai-{tier}-{cycle}", amount, interval);

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
            callback_url = $"{_config["App:DashboardUrl"] ?? "https://app.ojunai.com"}/settings?voiceai=true",
            metadata = new { businessId = businessId.ToString(), product = "voice_ai", tier }
        };

        var response = await PostAsync("/transaction/initialize", body);
        return response.GetProperty("data").GetProperty("authorization_url").GetString()!;
    }

    /// <summary>
    /// Initialize a Paystack checkout for a WhatsApp pack. The merchant can opt into auto-renew
    /// (card payments only) — when enabled, the transaction carries a Paystack plan code so
    /// subsequent charges from Paystack auto-renew the pack. Without auto-renew it's a pure
    /// one-time charge and the daily PackExpiryJobService cancels the pack at NextBillingAtUtc.
    ///
    /// The webhook recognizes pack purchases by metadata.mode == "whatsapp_pack" + packCode,
    /// validates the amount matches the canonical price, then upserts a BusinessAddOn row.
    /// metadata.autoRenew tells the webhook whether to mark the row as auto-renewing.
    /// </summary>
    public async Task<string> InitializeWhatsAppPackChargeAsync(
        Guid businessId, string packCode, string email, bool autoRenew = false)
    {
        var business = await _db.Businesses.FindAsync(businessId)
            ?? throw new KeyNotFoundException("Business not found.");

        if (!business.IsBillable)
            throw new InvalidOperationException("This account is not billable.");

        var billingCurrency = business.BillingCurrency ?? business.Currency ?? "NGN";
        if (BillingConfig.GetProvider(billingCurrency) != BillingConfig.BillingProvider.Paystack)
        {
            throw new InvalidOperationException(
                $"PaystackService received a {billingCurrency} pack purchase — that currency routes to Flutterwave.");
        }

        var cycle = (business.BillingCycle ?? "monthly").Equals("annual", StringComparison.OrdinalIgnoreCase)
            ? BillingConfig.BillingCycle.Annual
            : BillingConfig.BillingCycle.Monthly;
        var amount = BillingConfig.GetWhatsAppPackPriceOrThrow(packCode, cycle, billingCurrency);
        var interval = cycle == BillingConfig.BillingCycle.Annual ? "annually" : "monthly";

        // Build the transaction body. If auto-renew is on, include `plan` so Paystack creates
        // a recurring subscription on the first successful charge. Without `plan`, it's a
        // one-time transaction even on a card payment method.
        object body;
        if (autoRenew)
        {
            var paystackPlanCode = await GetOrCreatePlanAsync(
                $"whatsapp-pack-{packCode}-{cycle.ToString().ToLowerInvariant()}",
                amount,
                interval);

            body = new
            {
                email,
                amount = (int)(amount * 100),
                plan = paystackPlanCode,
                callback_url = $"{_config["App:DashboardUrl"] ?? "https://app.ojunai.com"}/settings?pack=true",
                metadata = new
                {
                    businessId = businessId.ToString(),
                    mode = "whatsapp_pack",
                    packCode,
                    cycle = cycle.ToString().ToLowerInvariant(),
                    autoRenew = true,
                }
            };
        }
        else
        {
            body = new
            {
                email,
                amount = (int)(amount * 100),
                callback_url = $"{_config["App:DashboardUrl"] ?? "https://app.ojunai.com"}/settings?pack=true",
                metadata = new
                {
                    businessId = businessId.ToString(),
                    mode = "whatsapp_pack",
                    packCode,
                    cycle = cycle.ToString().ToLowerInvariant(),
                    autoRenew = false,
                }
            };
        }

        var response = await PostAsync("/transaction/initialize", body);
        _logger.LogInformation(
            "Initialized Paystack pack purchase: business={Business} pack={Pack} amount={Amount} {Currency} autoRenew={AutoRenew}",
            business.Name, packCode, amount, billingCurrency, autoRenew);
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

        // Idempotency: if we already stored this subscription code (e.g. created via API call
        // from HandleChargeSuccess for a delta upgrade), skip the rest. Otherwise this handler
        // would overwrite the carefully preserved SubscriptionEndsAt with `now + 1 month`.
        if (!string.IsNullOrEmpty(subscriptionCode) && business.PaystackSubscriptionCode == subscriptionCode)
        {
            _logger.LogInformation("subscription.create webhook for already-stored sub {Code} on {Business} — skipping double-handling",
                subscriptionCode, business.Name);
            return;
        }

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
        // Path 1: metadata-driven (first charge from /transaction/initialize carries our metadata).
        Business? business = null;
        string? metaPlan = null;
        string? product = null;
        string? metaMode = null;
        bool hasMeta = data.TryGetProperty("metadata", out var meta) && meta.ValueKind == JsonValueKind.Object;
        if (hasMeta)
        {
            if (meta.TryGetProperty("businessId", out var bizIdEl) &&
                Guid.TryParse(bizIdEl.GetString(), out var businessId))
            {
                business = await _db.Businesses.FindAsync(businessId);
            }
            if (meta.TryGetProperty("plan", out var planEl)) metaPlan = planEl.GetString();
            if (meta.TryGetProperty("product", out var prodEl)) product = prodEl.GetString();
            if (meta.TryGetProperty("mode", out var modeEl)) metaMode = modeEl.GetString();
        }

        // Path 2: customer-code fallback (subscription renewals + any charge missing metadata).
        // Without this, every monthly auto-renewal would silently fail to extend the subscription.
        if (business == null && data.TryGetProperty("customer", out var custEl) &&
            custEl.TryGetProperty("customer_code", out var ccEl))
        {
            var customerCode = ccEl.GetString();
            if (!string.IsNullOrEmpty(customerCode))
            {
                business = await _db.Businesses.FirstOrDefaultAsync(b => b.PaystackCustomerCode == customerCode);
            }
        }

        if (business == null)
        {
            // Audit row even on no-match so unmatched payments are visible in BillingEvents
            // instead of vanishing silently. Helps the same diagnosis we just did.
            var refStr = data.TryGetProperty("reference", out var rEl) ? rEl.GetString() : null;
            _logger.LogWarning("Paystack charge.success: no business matched (ref={Ref}, hasMeta={HasMeta})", refStr, hasMeta);
            return;
        }

        // Voice AI standalone-product payment
        if (product == "voice_ai")
        {
            decimal? chargedNaira = data.TryGetProperty("amount", out var vaAmtEl) ? vaAmtEl.GetInt64() / 100m : null;
            var isAnnual = business.BillingCycle?.Equals("annual", StringComparison.OrdinalIgnoreCase) == true;
            // Tier comes from metadata.tier set in InitializeVoiceAIAsync. Default to the existing
            // tier on renewals (Paystack subscription charges carry the original metadata).
            var newTier = meta.TryGetProperty("tier", out var tierEl) ? tierEl.GetString()?.ToLower() : null;
            if (string.IsNullOrEmpty(newTier) || !BillingConfig.VoiceAITierCodes.Contains(newTier))
                newTier = business.VoiceAITier;

            business.VoiceAIEnabled = true;
            business.VoiceAIPlanStatus = "active";
            business.VoiceAIEnabledAt ??= DateTime.UtcNow;
            business.VoiceAITrialEndsAt = null;
            // First charge on a tier OR a tier switch → zero the cycle counter so the new cap starts
            // fresh. Renewal charges (same tier) also reset because the merchant just paid for
            // another month of inbound minutes.
            business.VoiceAITier = newTier;
            business.VoiceAICycleMinutesUsed = 0;
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
                Plan = $"voice_ai.{newTier ?? "unknown"}",
                Amount = chargedNaira,
                Currency = business.BillingCurrency ?? business.Currency,
                PaymentMethod = "card",
                Status = "success"
            });

            await _db.SaveChangesAsync();
            _logger.LogInformation("Voice AI {Tier} payment confirmed: {Business}", newTier, business.Name);

            var provisioner = _serviceProvider.GetRequiredService<VoiceAIProvisioningService>();
            await provisioner.EnsureProvisionedAsync(business);
            return;
        }

        // WhatsApp pack first-charge — metadata.mode is set on /transaction/initialize so it's
        // ours, validate amount and upsert the BusinessAddOn.
        if (string.Equals(metaMode, "whatsapp_pack", StringComparison.OrdinalIgnoreCase))
        {
            await HandleWhatsAppPackChargeAsync(business, meta, data);
            return;
        }

        // WhatsApp pack recurring charge — auto-renew transactions fire charge.success WITHOUT
        // our metadata (Paystack auto-charges from the subscription). The disambiguator is
        // subscription_code → BusinessAddOn.ProviderSubscriptionId. Returns true if matched +
        // handled; if false, fall through to the existing tier-renewal logic below.
        if (await TryHandleWhatsAppPackRenewalAsync(business, data))
            return;

        // Plan name resolution:
        //  1. Prefer metadata.plan (first-charge initialization)
        //  2. Fall back to deriving from data.plan.plan_code (renewal events)
        //  3. Fall back to current SubscribedPlan (last-resort renewal)
        var plan = metaPlan;
        if (string.IsNullOrEmpty(plan) &&
            data.TryGetProperty("plan", out var planObj) && planObj.ValueKind == JsonValueKind.Object &&
            planObj.TryGetProperty("plan_code", out var pcEl))
        {
            plan = await MapPlanCodeToName(pcEl.GetString());
        }
        if (string.IsNullOrEmpty(plan)) plan = business.SubscribedPlan;

        if (!string.IsNullOrEmpty(plan))
        {
            // Verify amount matches expected plan price
            // Mid-cycle delta upgrade: charged amount is intentionally less than full plan price.
            // Skip the amount-mismatch check, don't extend SubscriptionEndsAt, and disable auto-renew
            // (no Paystack sub backs this charge — user must re-subscribe at expiry).
            bool isDeltaUpgrade = string.Equals(metaMode, "upgrade_delta", StringComparison.OrdinalIgnoreCase);

            var expectedPrice = PlanLimits.Get(plan).PricePerMonth;
            decimal? chargedNaira = null;
            if (data.TryGetProperty("amount", out var amountEl))
            {
                var chargedKobo = amountEl.GetInt64();
                chargedNaira = chargedKobo / 100m;
                if (!isDeltaUpgrade && Math.Abs(chargedNaira.Value - expectedPrice) > 1)
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
            business.SubscriptionStatus = "active";
            business.TrialEndsAt = null;

            if (isDeltaUpgrade)
            {
                // Mid-cycle upgrade — keep existing SubscriptionEndsAt and bridge auto-renew via
                // a future-dated Paystack subscription that starts charging full price at the
                // current cycle end. (Scenario A in the redesign brief.)
                business.IsAutoRenew = true;

                try
                {
                    var newPlanPrice = PlanLimits.Get(plan).PricePerMonth;
                    var bizCycle = business.BillingCycle ?? "monthly";
                    var bizIsAnnual = bizCycle.Equals("annual", StringComparison.OrdinalIgnoreCase);
                    var bizInterval = bizIsAnnual ? "annually" : "monthly";
                    var newPlanCode = await GetOrCreatePlanAsync($"{plan}-{bizCycle}", newPlanPrice, bizInterval);
                    var startDateIso = business.SubscriptionEndsAt!.Value.ToString("o");

                    var subResp = await PostAsync("/subscription", new
                    {
                        customer = business.PaystackCustomerCode,
                        plan = newPlanCode,
                        start_date = startDateIso
                    });

                    var newSubCode = subResp.GetProperty("data").GetProperty("subscription_code").GetString();
                    business.PaystackSubscriptionCode = newSubCode;
                    business.PaystackPlanCode = newPlanCode;
                    _logger.LogInformation("Scheduled future-dated sub {Code} for {Business} starting {Start}",
                        newSubCode, business.Name, startDateIso);
                }
                catch (Exception ex)
                {
                    // Fall back to no auto-renew rather than silently leaving the user without coverage.
                    _logger.LogError(ex, "Failed to schedule future-dated sub for {Business} after delta upgrade — auto-renew NOT active", business.Name);
                    business.IsAutoRenew = false;
                }
            }
            else
            {
                business.IsAutoRenew = true;
                // Scenario B: full-price flow always anchors EndsAt to now + 1 month.
                // Earlier behavior used max(EndsAt, now) which gave a small bonus window when
                // upgrading in the last week of an existing cycle. New rule: pay full price = full
                // 30 days from this moment, simpler and what users expect.
                business.SubscriptionEndsAt = DateTime.UtcNow.AddMonths(1);
            }

            // Store customer code if not yet stored
            if (string.IsNullOrEmpty(business.PaystackCustomerCode) && data.TryGetProperty("customer", out var cust))
            {
                business.PaystackCustomerCode = cust.GetProperty("customer_code").GetString();
            }

            _db.BillingEvents.Add(new BillingEvent
            {
                BusinessId = business.Id,
                EventType = isDeltaUpgrade ? "payment.upgrade_delta" : "payment.success",
                Provider = "paystack",
                Plan = plan,
                Amount = chargedNaira,
                Currency = business.Currency,
                PaymentMethod = "card",
                Status = "success",
                CreatedAtUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            _logger.LogInformation("{Type} confirmed: {Business} → {Plan} (auto-renew={AutoRenew}, ends={EndsAt})",
                isDeltaUpgrade ? "Delta upgrade" : "Payment", business.Name, plan, business.IsAutoRenew, business.SubscriptionEndsAt);
        }
    }

    /// <summary>
    /// Activates a WhatsApp pack after a verified Paystack charge.success webhook. Validates the
    /// charged amount matches the canonical pack price (defense against tampering), cancels any
    /// existing active pack for the business, then upserts a new BusinessAddOn row.
    /// </summary>
    private async Task HandleWhatsAppPackChargeAsync(Business business, JsonElement meta, JsonElement data)
    {
        var packCode = meta.TryGetProperty("packCode", out var pcEl) ? pcEl.GetString() : null;
        var cycleStr = meta.TryGetProperty("cycle", out var cyEl) ? cyEl.GetString() : "monthly";
        var autoRenew = meta.TryGetProperty("autoRenew", out var arEl) && arEl.GetBoolean();

        if (string.IsNullOrEmpty(packCode) || !BillingConfig.WhatsAppPackCodes.Contains(packCode.ToLowerInvariant()))
        {
            _logger.LogWarning(
                "Paystack pack webhook: unknown pack code '{Pack}' for business {Business}",
                packCode, business.Name);
            return;
        }

        var cycle = string.Equals(cycleStr, "annual", StringComparison.OrdinalIgnoreCase)
            ? BillingConfig.BillingCycle.Annual
            : BillingConfig.BillingCycle.Monthly;
        var currency = business.BillingCurrency ?? business.Currency ?? "NGN";
        var expectedAmount = BillingConfig.GetWhatsAppPackPrice(packCode, cycle, currency);
        if (!expectedAmount.HasValue)
        {
            _logger.LogWarning(
                "Paystack pack webhook: no canonical price for {Pack}/{Cycle}/{Currency} — rejecting",
                packCode, cycle, currency);
            return;
        }

        // Paystack amounts are in kobo (smallest unit). For NGN, expected naira × 100.
        var chargedMinorUnits = data.TryGetProperty("amount", out var amtEl) ? amtEl.GetInt64() : 0;
        var expectedMinorUnits = (long)(expectedAmount.Value * 100);
        if (chargedMinorUnits != expectedMinorUnits)
        {
            _logger.LogWarning(
                "Paystack pack webhook: amount mismatch for {Business}/{Pack}: expected={Expected} charged={Charged}",
                business.Name, packCode, expectedMinorUnits, chargedMinorUnits);
            return;
        }

        // Paystack populates subscription_code on the first charge of a subscription-mode
        // transaction. Capture it so future renewal charges (which arrive WITHOUT our metadata)
        // can be matched back to this business + pack.
        string? subscriptionCode = null;
        if (autoRenew && data.TryGetProperty("subscription_code", out var sscEl))
            subscriptionCode = sscEl.GetString();

        await UpsertWhatsAppPackAddOnAsync(
            business.Id, packCode.ToLowerInvariant(), expectedAmount.Value, currency, cycle,
            autoRenew, subscriptionCode);

        _db.BillingEvents.Add(new BillingEvent
        {
            BusinessId = business.Id,
            EventType = "whatsapp_pack.activated",
            Provider = "paystack",
            Plan = $"whatsapp_pack.{packCode}",
            Amount = expectedAmount.Value,
            Currency = currency,
            PaymentMethod = "card",
            Status = "success"
        });

        await _db.SaveChangesAsync();
        _logger.LogInformation(
            "WhatsApp pack activated via Paystack: business={Business} pack={Pack} amount={Amount} {Currency} autoRenew={AutoRenew}",
            business.Name, packCode, expectedAmount.Value, currency, autoRenew);
    }

    /// <summary>
    /// Handles a recurring Paystack charge for an already-active WhatsApp pack subscription.
    /// Match key is subscription_code → BusinessAddOn.ProviderSubscriptionId. Bumps
    /// NextBillingAtUtc and logs a BillingEvent. Returns true if a renewal was handled,
    /// false if the subscription_code doesn't match any pack we know about (caller continues
    /// with the existing tier-renewal logic).
    /// </summary>
    private async Task<bool> TryHandleWhatsAppPackRenewalAsync(Business business, JsonElement data)
    {
        if (!data.TryGetProperty("subscription_code", out var scEl)) return false;
        var subscriptionCode = scEl.GetString();
        if (string.IsNullOrEmpty(subscriptionCode)) return false;

        // Match against active OR past_due rows — a successful renewal after a failed charge
        // should flip the pack back to active rather than no-op'ing because Status changed.
        var addon = await _db.BusinessAddOns.FirstOrDefaultAsync(a =>
            a.BusinessId == business.Id
            && a.ProviderSubscriptionId == subscriptionCode
            && (a.Status == "active" || a.Status == "past_due")
            && a.AddOnCode.StartsWith("whatsapp_pack."));
        if (addon == null) return false;

        var now = DateTime.UtcNow;
        var cycle = addon.NextBillingAtUtc.HasValue && (addon.NextBillingAtUtc.Value - addon.AddedAtUtc).TotalDays > 60
            ? BillingConfig.BillingCycle.Annual
            : BillingConfig.BillingCycle.Monthly;
        addon.NextBillingAtUtc = cycle == BillingConfig.BillingCycle.Annual ? now.AddYears(1) : now.AddMonths(1);
        addon.Status = "active"; // back from past_due if applicable
        addon.UpdatedAtUtc = now;

        decimal? chargedNaira = data.TryGetProperty("amount", out var amtEl) ? amtEl.GetInt64() / 100m : null;
        _db.BillingEvents.Add(new BillingEvent
        {
            BusinessId = business.Id,
            EventType = "whatsapp_pack.renewed",
            Provider = "paystack",
            Plan = addon.AddOnCode,
            Amount = chargedNaira,
            Currency = addon.BilledCurrency,
            PaymentMethod = "card",
            Status = "success"
        });

        await _db.SaveChangesAsync();
        _logger.LogInformation(
            "WhatsApp pack renewed via Paystack: business={Business} addon={AddOn} nextBilling={Next}",
            business.Name, addon.AddOnCode, addon.NextBillingAtUtc);
        return true;
    }

    /// <summary>
    /// Cancel auto-renew on an active WhatsApp pack subscription. Calls Paystack's
    /// /subscription/disable endpoint, then flips the BusinessAddOn's IsAutoRenew flag.
    /// The pack stays active until <c>NextBillingAtUtc</c> — at which point the daily
    /// PackExpiryJobService will mark it expired (because IsAutoRenew is now false).
    ///
    /// Idempotent: callable on a pack that's already non-renewing or already cancelled — both
    /// are no-ops with a logged note.
    /// </summary>
    public async Task CancelWhatsAppPackAutoRenewAsync(Guid businessId)
    {
        var addon = await _db.BusinessAddOns.FirstOrDefaultAsync(a =>
            a.BusinessId == businessId
            && a.Status == "active"
            && a.AddOnCode.StartsWith("whatsapp_pack."));
        if (addon == null)
        {
            _logger.LogInformation("CancelPackAutoRenew: no active WhatsApp pack for business {Business}", businessId);
            return;
        }
        if (!addon.IsAutoRenew)
        {
            _logger.LogInformation("CancelPackAutoRenew: pack {AddOn} is already one-time — nothing to cancel", addon.AddOnCode);
            return;
        }

        // Best-effort cancellation at Paystack. If their API fails we still flip the local
        // flag so the merchant doesn't see a "cancel" button that does nothing — the daily
        // expiry job will close the loop locally even if Paystack keeps trying to renew.
        if (!string.IsNullOrEmpty(addon.ProviderSubscriptionId))
        {
            try
            {
                var detail = await GetAsync($"/subscription/{Uri.EscapeDataString(addon.ProviderSubscriptionId)}");
                if (detail.TryGetProperty("data", out var d) && d.TryGetProperty("email_token", out var et))
                {
                    var token = et.GetString();
                    if (!string.IsNullOrEmpty(token))
                    {
                        await PostAsync("/subscription/disable", new
                        {
                            code = addon.ProviderSubscriptionId,
                            token,
                        });
                        _logger.LogInformation("Paystack pack subscription disabled: business={Business} sub={Sub}",
                            businessId, addon.ProviderSubscriptionId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Paystack disable failed for pack sub {Sub} — flipping locally anyway",
                    addon.ProviderSubscriptionId);
            }
        }

        addon.IsAutoRenew = false;
        addon.UpdatedAtUtc = DateTime.UtcNow;

        _db.BillingEvents.Add(new BillingEvent
        {
            BusinessId = businessId,
            EventType = "whatsapp_pack.auto_renew_cancelled",
            Provider = "paystack",
            Plan = addon.AddOnCode,
            Amount = addon.BilledAmount,
            Currency = addon.BilledCurrency,
            Status = "cancelled",
        });

        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Cancel any existing active WhatsApp pack rows, then insert a new active one. Shared by
    /// the Paystack webhook path. (FlutterwaveService has its own equivalent that calls this
    /// same DB pattern.)
    /// </summary>
    private async Task UpsertWhatsAppPackAddOnAsync(
        Guid businessId, string packCode, decimal amount, string currency, BillingConfig.BillingCycle cycle,
        bool autoRenew = false, string? providerSubscriptionId = null)
    {
        var now = DateTime.UtcNow;
        var existing = await _db.BusinessAddOns
            .Where(a => a.BusinessId == businessId
                && a.Status == "active"
                && a.AddOnCode.StartsWith("whatsapp_pack."))
            .ToListAsync();
        foreach (var old in existing)
        {
            old.Status = "cancelled";
            old.CancelledAtUtc = now;
            old.UpdatedAtUtc = now;
        }

        var nextBilling = cycle == BillingConfig.BillingCycle.Annual ? now.AddYears(1) : now.AddMonths(1);
        _db.BusinessAddOns.Add(new BusinessAddOn
        {
            BusinessId = businessId,
            AddOnCode = $"whatsapp_pack.{packCode}",
            Status = "active",
            Quantity = 1,
            BilledAmount = amount,
            BilledCurrency = currency,
            AddedAtUtc = now,
            NextBillingAtUtc = nextBilling,
            UpdatedAtUtc = now,
            IsAutoRenew = autoRenew,
            ProviderSubscriptionId = providerSubscriptionId,
        });
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
                    "Visit app.ojunai.com/settings to resubscribe.",
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

        // Pack subscription failure: subscription_code matches a pack's ProviderSubscriptionId.
        // Mark pack past_due and log; the PackExpiryJobService will expire it after 5 days if
        // no successful renewal charge fires before then. A successful renewal will flip the
        // Status back to "active" via TryHandleWhatsAppPackRenewalAsync.
        var packAddon = await _db.BusinessAddOns.FirstOrDefaultAsync(a =>
            a.ProviderSubscriptionId == subscriptionCode
            && a.AddOnCode.StartsWith("whatsapp_pack."));
        if (packAddon != null)
        {
            var now = DateTime.UtcNow;
            packAddon.Status = "past_due";
            packAddon.UpdatedAtUtc = now;
            _db.BillingEvents.Add(new BillingEvent
            {
                BusinessId = packAddon.BusinessId,
                EventType = "whatsapp_pack.payment_failed",
                Provider = "paystack",
                Plan = packAddon.AddOnCode,
                Status = "failed",
                SubscriptionId = subscriptionCode,
                CreatedAtUtc = now,
            });
            await _db.SaveChangesAsync();
            _logger.LogWarning("WhatsApp pack payment failed: business={Business} pack={Pack}",
                packAddon.BusinessId, packAddon.AddOnCode);
            return;
        }

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
                    $"Your card payment for Ojunai could not be processed. " +
                    $"Please update your payment method at app.ojunai.com/settings to keep your {planLabel} plan active.",
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
            if (name == $"Ojunai {planName}")
                return p.GetProperty("plan_code").GetString()!;
        }

        var result = await PostAsync("/plan", new
        {
            name = $"Ojunai {planName}",
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
                if (name.StartsWith("Ojunai "))
                    return name["Ojunai ".Length..].ToLower();
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
            var renewDate = business.SubscriptionEndsAt?.ToString("dd MMM yyyy") ?? "in 30 days";

            // Format the price in the user's actual billing currency, not a hardcoded NGN.
            // Falls back to "your plan price" if the price lookup fails (e.g. unsupported currency).
            var billingCurrency = business.BillingCurrency ?? business.Currency ?? "NGN";
            var bcCycle = business.BillingCycle?.Equals("annual", StringComparison.OrdinalIgnoreCase) == true
                ? BillingConfig.BillingCycle.Annual
                : BillingConfig.BillingCycle.Monthly;
            var bcPrice = BillingConfig.GetPrice(plan, bcCycle, billingCurrency);
            var priceText = bcPrice.HasValue
                ? BillingConfig.FormatPrice(bcPrice.Value, billingCurrency)
                : "your plan price";
            var cycleLabel = bcCycle == BillingConfig.BillingCycle.Annual ? "year" : "month";

            var whatsApp = _serviceProvider.GetRequiredService<IWhatsAppService>();
            await whatsApp.SendMessageAsync(
                $"whatsapp:{owner.PhoneNumber}",
                $"✅ *Payment successful!*\n\n" +
                $"Your *{planLabel}* plan is now active at {priceText}/{cycleLabel}.\n\n" +
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

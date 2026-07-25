using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;

namespace Ojunai.API.Services;

/// <summary>
/// Stripe payment provider for USD / GBP / CAD / EUR (Western-market cards, SCA, Stripe Tax).
/// Mirrors the public method shape of PaystackService/FlutterwaveService so SubscriptionController
/// can branch on BillingConfig.GetProvider(currency) uniformly.
///
/// DIVERGENCE FROM the other two providers: this uses the official Stripe.net SDK rather than a raw
/// HttpClient. Reasons: (a) EventUtility.ConstructEvent implements Stripe's signed-webhook
/// verification (timestamped HMAC + tolerance window) correctly — error-prone to hand-roll; (b) the
/// SDK gives typed request/response objects, idempotency keys, retries, and API-version pinning. The
/// SDK owns its own pooled HttpClient (StripeConfiguration.ApiKey is set once at boot in Program.cs),
/// so there is no IHttpClientFactory registration for Stripe.
///
/// Checkout model: hosted Stripe Checkout Sessions (redirect), mode=subscription for tiers + Voice AI,
/// mode=payment (one-time) for WhatsApp packs. Prices come inline from BillingConfig (no Stripe-side
/// price catalog to keep in sync). Stripe Tax is ON — amount validation uses the pre-tax subtotal.
/// </summary>
public class StripeService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StripeService> _logger;
    private readonly IActivityLogger _activity;

    public StripeService(
        AppDbContext db, IConfiguration config, IServiceProvider serviceProvider,
        ILogger<StripeService> logger, IActivityLogger activity)
    {
        _db = db;
        _config = config;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _activity = activity;
    }

    private string DashboardUrl() => _config["App:DashboardUrl"] ?? "https://app.ojunai.com";

    /// <summary>USD/GBP/CAD/EUR are all 2-decimal → minor units = amount × 100. A future zero-decimal
    /// currency (JPY, etc.) would need a special case; none are Stripe-routed today.</summary>
    private static long ToMinorUnits(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
    private static decimal FromMinorUnits(long minor) => minor / 100m;

    // ── Checkout initialization ────────────────────────────────────────────────

    public async Task<string> InitializeSubscriptionAsync(Guid businessId, string plan, string email)
    {
        var business = await _db.Businesses.FindAsync(businessId)
            ?? throw new KeyNotFoundException("Business not found.");
        if (!business.IsBillable) throw new InvalidOperationException("This account is not billable.");

        var currency = business.BillingCurrency ?? business.Currency ?? "USD";
        GuardStripeRouting(currency);

        var cycle = business.BillingCycle ?? "monthly";
        var cycleEnum = IsAnnual(cycle) ? BillingConfig.BillingCycle.Annual : BillingConfig.BillingCycle.Monthly;
        var amount = BillingConfig.GetPriceOrThrow(plan, cycleEnum, currency);

        await GetOrCreateCustomerAsync(business, email);
        await CancelExistingStripeSubscriptionAsync(business);

        var metadata = new Dictionary<string, string>
        {
            ["businessId"] = businessId.ToString(),
            ["product"] = "tier",
            ["plan"] = plan,
            ["cycle"] = cycle,
            ["currency"] = currency,
        };
        return await CreateCheckoutSessionAsync(
            business, $"Ojunai {plan} ({cycle})", amount, currency, cycle,
            recurring: true, metadata, successPath: "subscribed=true");
    }

    public async Task<string> InitializeVoiceAIAsync(
        Guid businessId, string email, decimal amount, string currency, string cycle, string tier)
    {
        var business = await _db.Businesses.FindAsync(businessId)
            ?? throw new KeyNotFoundException("Business not found.");
        GuardStripeRouting(currency);
        await GetOrCreateCustomerAsync(business, email);

        var metadata = new Dictionary<string, string>
        {
            ["businessId"] = businessId.ToString(),
            ["product"] = "voice_ai",
            ["tier"] = tier,
            ["cycle"] = cycle,
            ["currency"] = currency,
        };
        return await CreateCheckoutSessionAsync(
            business, $"OjunaiVoice {tier} ({cycle})", amount, currency, cycle,
            recurring: true, metadata, successPath: "voiceai=true");
    }

    /// <summary>WhatsApp pack via Stripe — one-time charge (mode=payment). Auto-renew is deferred for
    /// Stripe currencies (matches the Flutterwave behavior); the autoRenew flag is accepted for
    /// signature parity but ignored until recurring packs ship in phase 2.</summary>
    public async Task<string> InitializeWhatsAppPackChargeAsync(
        Guid businessId, string packCode, string email, bool autoRenew = false)
    {
        var business = await _db.Businesses.FindAsync(businessId)
            ?? throw new KeyNotFoundException("Business not found.");
        if (!business.IsBillable) throw new InvalidOperationException("This account is not billable.");

        var currency = business.BillingCurrency ?? business.Currency ?? "USD";
        GuardStripeRouting(currency);

        var cycle = business.BillingCycle ?? "monthly";
        var cycleEnum = IsAnnual(cycle) ? BillingConfig.BillingCycle.Annual : BillingConfig.BillingCycle.Monthly;
        var amount = BillingConfig.GetWhatsAppPackPriceOrThrow(packCode, cycleEnum, currency);

        await GetOrCreateCustomerAsync(business, email);

        var metadata = new Dictionary<string, string>
        {
            ["businessId"] = businessId.ToString(),
            ["product"] = "whatsapp_pack",
            ["packCode"] = packCode.ToLowerInvariant(),
            ["cycle"] = cycle,
            ["currency"] = currency,
        };
        return await CreateCheckoutSessionAsync(
            business, $"{BillingConfig.WhatsAppPackLabels.GetValueOrDefault(packCode, packCode)} ({cycle})",
            amount, currency, cycle, recurring: false, metadata, successPath: "pack=true");
    }

    private async Task<string> CreateCheckoutSessionAsync(
        Business business, string itemName, decimal amount, string currency, string cycle,
        bool recurring, Dictionary<string, string> metadata, string successPath)
    {
        var priceData = new SessionLineItemPriceDataOptions
        {
            Currency = currency.ToLowerInvariant(),
            UnitAmount = ToMinorUnits(amount),
            ProductData = new SessionLineItemPriceDataProductDataOptions { Name = itemName },
        };
        if (recurring)
            priceData.Recurring = new SessionLineItemPriceDataRecurringOptions { Interval = IsAnnual(cycle) ? "year" : "month" };

        var options = new SessionCreateOptions
        {
            Mode = recurring ? "subscription" : "payment",
            Customer = business.StripeCustomerId,
            ClientReferenceId = business.Id.ToString(),
            LineItems = new List<SessionLineItemOptions> { new() { Quantity = 1, PriceData = priceData } },
            Metadata = metadata,
            SuccessUrl = $"{DashboardUrl()}/settings?{successPath}&session_id={{CHECKOUT_SESSION_ID}}",
            CancelUrl = $"{DashboardUrl()}/settings?checkout=cancelled",
            // Stripe Tax ON — Stripe calculates VAT/GST/sales tax by customer location and adds it on
            // top of our price. Webhook validation uses AmountSubtotal (pre-tax) against BillingConfig.
            AutomaticTax = new SessionAutomaticTaxOptions { Enabled = true },
            BillingAddressCollection = "required",
            CustomerUpdate = new SessionCustomerUpdateOptions { Address = "auto" },
        };
        if (recurring)
            options.SubscriptionData = new SessionSubscriptionDataOptions { Metadata = metadata };

        var idempotencyKey = $"checkout-{business.Id:N}-{metadata["product"]}-{DateTime.UtcNow.Ticks}";
        var session = await new SessionService().CreateAsync(options, new RequestOptions { IdempotencyKey = idempotencyKey });
        _logger.LogInformation("Stripe checkout session {Session} created for {Business} ({Item}, {Amount} {Currency})",
            session.Id, business.Name, itemName, amount, currency);
        return session.Url;
    }

    private async Task GetOrCreateCustomerAsync(Business business, string email)
    {
        if (!string.IsNullOrEmpty(business.StripeCustomerId)) return;
        var customer = await new CustomerService().CreateAsync(new CustomerCreateOptions
        {
            Email = email,
            Name = business.Name,
            Metadata = new Dictionary<string, string> { ["businessId"] = business.Id.ToString() },
        }, new RequestOptions { IdempotencyKey = $"cust-{business.Id:N}" });
        business.StripeCustomerId = customer.Id;
        await _db.SaveChangesAsync();
    }

    /// <summary>Cancel any live Stripe tier subscription before a new full-price checkout so a plan
    /// change (e.g. Lite → Pro) doesn't leave two active subscriptions billing concurrently. Best-effort.</summary>
    private async Task CancelExistingStripeSubscriptionAsync(Business business)
    {
        if (string.IsNullOrEmpty(business.StripeSubscriptionId)) return;
        try
        {
            await new SubscriptionService().CancelAsync(business.StripeSubscriptionId);
            _logger.LogInformation("Cancelled prior Stripe sub {Sub} for {Business} before new checkout",
                business.StripeSubscriptionId, business.Name);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Failed to cancel prior Stripe sub {Sub} for {Business} — continuing",
                business.StripeSubscriptionId, business.Name);
        }
        business.StripeSubscriptionId = null;
        await _db.SaveChangesAsync();
    }

    // ── Webhook ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Handle a signature-verified Stripe event (verification done in SubscriptionController via
    /// EventUtility.ConstructEvent — the Event object here is Stripe's server-side truth, not a
    /// client-forgeable payload). Idempotent via PaystackEventLog keyed on the Stripe event id;
    /// the staged dedup row commits atomically with the activation (same pattern as PaystackService).
    /// </summary>
    public async Task HandleWebhookAsync(Event stripeEvent)
    {
        var eventId = stripeEvent.Id; // Stripe guarantees uniqueness; retries carry the same id.
        var seen = await _db.PaystackEventLogs.AnyAsync(e => e.EventId == eventId);
        if (seen)
        {
            _logger.LogInformation("Stripe webhook duplicate ignored: {Type} {Id}", stripeEvent.Type, eventId);
            return;
        }
        _db.PaystackEventLogs.Add(new PaystackEventLog { EventId = eventId, EventType = stripeEvent.Type });

        _logger.LogInformation("Stripe webhook: {Type}", stripeEvent.Type);

        try
        {
            switch (stripeEvent.Type)
            {
                case "checkout.session.completed":
                    await HandleCheckoutCompletedAsync((Session)stripeEvent.Data.Object);
                    break;
                case "invoice.paid":
                    await HandleInvoicePaidAsync((Invoice)stripeEvent.Data.Object);
                    break;
                case "invoice.payment_failed":
                    await HandleInvoicePaymentFailedAsync((Invoice)stripeEvent.Data.Object);
                    break;
                case "customer.subscription.deleted":
                    await HandleSubscriptionDeletedAsync((Stripe.Subscription)stripeEvent.Data.Object);
                    break;
                default:
                    // Unknown/uninteresting event — still commit the dedup row so a retry no-ops.
                    await _db.SaveChangesAsync();
                    break;
            }
        }
        catch (DbUpdateException ex) when (IsDuplicateEventRace(ex))
        {
            // A concurrent delivery of the same event committed first; the unique index on
            // PaystackEventLog.EventId tripped and rolled OUR SaveChanges back atomically (no partial
            // activation). Treat as duplicate — return 200, don't 500 into a retry storm.
            _logger.LogInformation("Stripe webhook concurrent duplicate ignored: {Type} {Id}", stripeEvent.Type, eventId);
        }
    }

    private static bool IsDuplicateEventRace(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation }
        && ex.Entries.Any(e => e.Entity is PaystackEventLog);

    /// <summary>First activation after a completed checkout. Routes by the `product` metadata.</summary>
    private async Task HandleCheckoutCompletedAsync(Session session)
    {
        var meta = session.Metadata ?? new Dictionary<string, string>();
        if (!Guid.TryParse(meta.GetValueOrDefault("businessId") ?? session.ClientReferenceId, out var businessId))
        {
            _logger.LogWarning("Stripe checkout.session.completed with no resolvable businessId (session {Id})", session.Id);
            await _db.SaveChangesAsync();
            return;
        }
        var business = await _db.Businesses.FindAsync(businessId);
        if (business == null)
        {
            _logger.LogWarning("Stripe checkout.session.completed: business {Id} not found", businessId);
            await _db.SaveChangesAsync();
            return;
        }

        var product = meta.GetValueOrDefault("product") ?? "tier";
        var currency = (meta.GetValueOrDefault("currency") ?? business.BillingCurrency ?? "USD").ToUpperInvariant();
        var cycle = meta.GetValueOrDefault("cycle") ?? business.BillingCycle ?? "monthly";
        var isAnnual = IsAnnual(cycle);
        // Tax ON → the pre-tax subtotal is what must match our price. AmountTotal includes tax.
        var subtotalMinor = session.AmountSubtotal ?? session.AmountTotal ?? 0;
        var taxMinor = session.TotalDetails?.AmountTax ?? 0;

        // Currency fail-closed
        if (!string.Equals(session.Currency, currency, StringComparison.OrdinalIgnoreCase))
        {
            RejectPayment(business, product, currency, $"Currency mismatch: paid {session.Currency}, expected {currency}");
            await _db.SaveChangesAsync();
            return;
        }

        business.StripeCustomerId = session.CustomerId ?? business.StripeCustomerId;
        business.BillingProvider = "stripe";
        business.BillingCurrency = currency;
        business.BillingCycle = cycle;
        business.PaymentMethod = "card";

        if (product == "voice_ai")
        {
            var tier = meta.GetValueOrDefault("tier");
            if (string.IsNullOrEmpty(tier) || !BillingConfig.VoiceAITierCodes.Contains(tier)) tier = business.VoiceAITier;
            var expected = BillingConfig.GetVoiceAITierPrice(tier ?? "", isAnnual ? BillingConfig.BillingCycle.Annual : BillingConfig.BillingCycle.Monthly, currency);
            if (expected.HasValue && subtotalMinor != ToMinorUnits(expected.Value))
            {
                RejectPayment(business, $"voice_ai.{tier}", currency, $"Amount mismatch: subtotal {FromMinorUnits(subtotalMinor)}, expected {expected}");
                await _db.SaveChangesAsync();
                return;
            }

            business.VoiceAISubscriptionId = session.SubscriptionId;
            business.VoiceAIEnabled = true;
            business.VoiceAIPlanStatus = "active";
            business.VoiceAIEnabledAt ??= DateTime.UtcNow;
            business.VoiceAITrialEndsAt = null;
            business.VoiceAITier = tier;
            business.VoiceAICycleMinutesUsed = 0;
            var baseDate = (business.VoiceAISubscriptionEndsAt.HasValue && business.VoiceAISubscriptionEndsAt > DateTime.UtcNow)
                ? business.VoiceAISubscriptionEndsAt.Value : DateTime.UtcNow;
            business.VoiceAISubscriptionEndsAt = isAnnual ? baseDate.AddYears(1) : baseDate.AddMonths(1);

            _db.BillingEvents.Add(BuildBillingEvent(business, "voiceai.payment.success", $"voice_ai.{tier}", FromMinorUnits(subtotalMinor), currency, taxMinor));
            await _db.SaveChangesAsync();
            var provisioner = _serviceProvider.GetRequiredService<VoiceAIProvisioningService>();
            await provisioner.EnsureProvisionedAsync(business);
            _logger.LogInformation("Stripe Voice AI {Tier} activated: {Business}", tier, business.Name);
            return;
        }

        if (product == "whatsapp_pack")
        {
            var packCode = (meta.GetValueOrDefault("packCode") ?? "").ToLowerInvariant();
            if (!BillingConfig.WhatsAppPackCodes.Contains(packCode))
            {
                RejectPayment(business, $"whatsapp_pack.{packCode}", currency, $"Unknown pack code '{packCode}'");
                await _db.SaveChangesAsync();
                return;
            }
            var cycleEnum = isAnnual ? BillingConfig.BillingCycle.Annual : BillingConfig.BillingCycle.Monthly;
            var expected = BillingConfig.GetWhatsAppPackPrice(packCode, cycleEnum, currency);
            if (expected.HasValue && subtotalMinor != ToMinorUnits(expected.Value))
            {
                RejectPayment(business, $"whatsapp_pack.{packCode}", currency, $"Amount mismatch: subtotal {FromMinorUnits(subtotalMinor)}, expected {expected}");
                await _db.SaveChangesAsync();
                return;
            }
            await UpsertWhatsAppPackAddOnAsync(business.Id, packCode, expected ?? FromMinorUnits(subtotalMinor), currency, cycleEnum);
            _db.BillingEvents.Add(BuildBillingEvent(business, "whatsapp_pack.activated", $"whatsapp_pack.{packCode}", FromMinorUnits(subtotalMinor), currency, taxMinor));
            await _db.SaveChangesAsync();
            _logger.LogInformation("Stripe WhatsApp pack {Pack} activated: {Business}", packCode, business.Name);
            return;
        }

        // Main tier subscription
        var plan = meta.GetValueOrDefault("plan") ?? business.SubscribedPlan ?? "";
        if (string.IsNullOrEmpty(plan))
        {
            _logger.LogWarning("Stripe tier checkout with no plan metadata (business {Id})", businessId);
            await _db.SaveChangesAsync();
            return;
        }
        var expectedTier = BillingConfig.GetPrice(plan, isAnnual ? BillingConfig.BillingCycle.Annual : BillingConfig.BillingCycle.Monthly, currency);
        if (expectedTier.HasValue && subtotalMinor != ToMinorUnits(expectedTier.Value))
        {
            RejectPayment(business, plan, currency, $"Amount mismatch: subtotal {FromMinorUnits(subtotalMinor)}, expected {expectedTier}");
            await _db.SaveChangesAsync();
            return;
        }

        business.StripeSubscriptionId = session.SubscriptionId;
        business.Plan = plan;
        business.SubscribedPlan = plan;
        business.PendingPlanChange = null;
        business.SubscriptionStatus = "active";
        business.TrialEndsAt = null;
        business.IsAutoRenew = true;
        business.SubscriptionEndsAt = isAnnual ? DateTime.UtcNow.AddYears(1) : DateTime.UtcNow.AddMonths(1);

        _db.BillingEvents.Add(BuildBillingEvent(business, "payment.success", plan, FromMinorUnits(subtotalMinor), currency, taxMinor));
        await _db.SaveChangesAsync();
        _logger.LogInformation("Stripe tier activated: {Business} → {Plan} (ends {EndsAt})", business.Name, plan, business.SubscriptionEndsAt);
        await SendPaymentConfirmationAsync(business, plan);
    }

    /// <summary>Renewal charges (2nd cycle onward). The first invoice (BillingReason=subscription_create)
    /// is owned by checkout.session.completed — skip it to avoid double-extending.</summary>
    private async Task HandleInvoicePaidAsync(Invoice invoice)
    {
        if (string.Equals(invoice.BillingReason, "subscription_create", StringComparison.OrdinalIgnoreCase))
        {
            await _db.SaveChangesAsync();
            return;
        }
        var subId = invoice.Parent?.SubscriptionDetails?.SubscriptionId;
        if (string.IsNullOrEmpty(subId)) { await _db.SaveChangesAsync(); return; }

        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.StripeSubscriptionId == subId || b.VoiceAISubscriptionId == subId);
        if (business == null)
        {
            _logger.LogWarning("Stripe invoice.paid for unknown subscription {Sub}", subId);
            await _db.SaveChangesAsync();
            return;
        }

        var isAnnual = IsAnnual(business.BillingCycle ?? "monthly");
        var currency = (invoice.Currency ?? business.BillingCurrency ?? "USD").ToUpperInvariant();
        var subtotal = FromMinorUnits(invoice.Subtotal);

        if (business.VoiceAISubscriptionId == subId)
        {
            var baseDate = (business.VoiceAISubscriptionEndsAt.HasValue && business.VoiceAISubscriptionEndsAt > DateTime.UtcNow)
                ? business.VoiceAISubscriptionEndsAt.Value : DateTime.UtcNow;
            business.VoiceAISubscriptionEndsAt = isAnnual ? baseDate.AddYears(1) : baseDate.AddMonths(1);
            business.VoiceAIPlanStatus = "active";
            business.VoiceAICycleMinutesUsed = 0;
            _db.BillingEvents.Add(BuildBillingEvent(business, "voiceai.payment.success", $"voice_ai.{business.VoiceAITier}", subtotal, currency, Math.Max(0, invoice.Total - invoice.Subtotal)));
        }
        else
        {
            var baseDate = (business.SubscriptionEndsAt.HasValue && business.SubscriptionEndsAt > DateTime.UtcNow)
                ? business.SubscriptionEndsAt.Value : DateTime.UtcNow;
            business.SubscriptionEndsAt = isAnnual ? baseDate.AddYears(1) : baseDate.AddMonths(1);
            business.SubscriptionStatus = "active";
            _db.BillingEvents.Add(BuildBillingEvent(business, "payment.success", business.SubscribedPlan, subtotal, currency, Math.Max(0, invoice.Total - invoice.Subtotal)));
        }
        await _db.SaveChangesAsync();
        _logger.LogInformation("Stripe renewal for {Business} (sub {Sub}) → ends {EndsAt}", business.Name, subId, business.SubscriptionEndsAt);
    }

    private async Task HandleInvoicePaymentFailedAsync(Invoice invoice)
    {
        var subId = invoice.Parent?.SubscriptionDetails?.SubscriptionId;
        if (string.IsNullOrEmpty(subId)) { await _db.SaveChangesAsync(); return; }
        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.StripeSubscriptionId == subId || b.VoiceAISubscriptionId == subId);
        if (business == null) { await _db.SaveChangesAsync(); return; }

        if (business.VoiceAISubscriptionId == subId) business.VoiceAIPlanStatus = "past_due";
        else business.SubscriptionStatus = "past_due";

        _db.BillingEvents.Add(BuildBillingEvent(business, "payment.failed", business.SubscribedPlan, null, business.BillingCurrency, 0, "failed"));
        await _db.SaveChangesAsync();
        _logger.LogWarning("Stripe payment failed for {Business} (sub {Sub}) — marked past_due", business.Name, subId);
        await SendPaymentFailedAsync(business);
    }

    private async Task HandleSubscriptionDeletedAsync(Stripe.Subscription subscription)
    {
        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.StripeSubscriptionId == subscription.Id || b.VoiceAISubscriptionId == subscription.Id);
        if (business == null) { await _db.SaveChangesAsync(); return; }

        if (business.VoiceAISubscriptionId == subscription.Id)
        {
            business.VoiceAISubscriptionId = null;
            business.VoiceAIPlanStatus = "cancelled";
        }
        else
        {
            business.StripeSubscriptionId = null;
            business.SubscriptionStatus = "cancelled";
        }
        _db.BillingEvents.Add(BuildBillingEvent(business, "subscription.cancelled", business.Plan, null, business.BillingCurrency, 0, "cancelled"));
        await _db.SaveChangesAsync();
        _logger.LogInformation("Stripe subscription {Sub} deleted for {Business}; access until {EndsAt}",
            subscription.Id, business.Name, business.SubscriptionEndsAt);
    }

    // ── Cancel ─────────────────────────────────────────────────────────────────

    public async Task CancelSubscriptionAsync(Guid businessId)
    {
        var business = await _db.Businesses.FindAsync(businessId)
            ?? throw new KeyNotFoundException("Business not found.");

        if (!string.IsNullOrEmpty(business.StripeSubscriptionId))
        {
            try
            {
                // Keep access until period end (matches the Paystack/Flutterwave cancel semantics —
                // the TrialRevertJobService downgrades when SubscriptionEndsAt passes).
                await new SubscriptionService().UpdateAsync(business.StripeSubscriptionId,
                    new SubscriptionUpdateOptions { CancelAtPeriodEnd = true });
                _logger.LogInformation("Stripe subscription {Sub} set to cancel at period end for {Business}",
                    business.StripeSubscriptionId, business.Name);
            }
            catch (StripeException ex)
            {
                _logger.LogWarning(ex, "Stripe cancel API failed for {Business} — clearing locally", business.Name);
            }
        }

        var cancelledPlan = business.Plan;
        business.SubscriptionStatus = "cancelled";
        _db.BillingEvents.Add(BuildBillingEvent(business, "subscription.cancelled", cancelledPlan, null, business.BillingCurrency, 0, "cancelled"));

        if (business.SubscriptionEndsAt == null || business.SubscriptionEndsAt <= DateTime.UtcNow)
        {
            business.Plan = "starter";
            business.SubscribedPlan = "starter";
            business.PendingPlanChange = null;
            business.SubscriptionEndsAt = null;
            business.TrialEndsAt = null;
            business.StripeSubscriptionId = null;
        }

        await _activity.LogAsync(businessId, "subscription.cancelled", "Billing", null, cancelledPlan, "cancelled subscription");
        await _db.SaveChangesAsync();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static bool IsAnnual(string cycle) => cycle.Equals("annual", StringComparison.OrdinalIgnoreCase);

    private static void GuardStripeRouting(string currency)
    {
        if (BillingConfig.GetProvider(currency) != BillingConfig.BillingProvider.Stripe)
            throw new InvalidOperationException(
                $"StripeService received a {currency} request — that currency routes to a different provider. " +
                $"This indicates a routing bug in SubscriptionController.");
    }

    private void RejectPayment(Business business, string? plan, string currency, string reason)
    {
        _logger.LogWarning("Stripe payment rejected for {Business}: {Reason}", business.Name, reason);
        _db.BillingEvents.Add(new BillingEvent
        {
            BusinessId = business.Id,
            EventType = "payment.rejected",
            Provider = "stripe",
            Plan = plan,
            Currency = currency,
            Status = "rejected",
            ErrorDetails = reason,
            CreatedAtUtc = DateTime.UtcNow,
        });
    }

    private static BillingEvent BuildBillingEvent(
        Business business, string eventType, string? plan, decimal? amount, string? currency, long taxMinor, string status = "success")
        => new()
        {
            BusinessId = business.Id,
            EventType = eventType,
            Provider = "stripe",
            Plan = plan,
            BillingCycle = business.BillingCycle,
            Amount = amount,
            Currency = currency,
            PaymentMethod = "card",
            Status = status,
            ErrorDetails = taxMinor > 0 ? $"tax={FromMinorUnits(taxMinor)} {currency}" : null,
            CreatedAtUtc = DateTime.UtcNow,
        };

    /// <summary>Cancel active WhatsApp pack rows then insert a new active one, atomically (one active
    /// pack per business enforced by a partial unique index). Same pattern as PaystackService.</summary>
    private async Task UpsertWhatsAppPackAddOnAsync(
        Guid businessId, string packCode, decimal amount, string currency, BillingConfig.BillingCycle cycle)
    {
        var now = DateTime.UtcNow;
        await using var tx = await _db.Database.BeginTransactionAsync();
        await _db.BusinessAddOns
            .Where(a => a.BusinessId == businessId && a.Status == "active" && a.AddOnCode.StartsWith("whatsapp_pack."))
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
            BilledAmount = amount,
            BilledCurrency = currency,
            AddedAtUtc = now,
            NextBillingAtUtc = nextBilling,
            UpdatedAtUtc = now,
            IsAutoRenew = false, // one-time on Stripe for v1
            ProviderSubscriptionId = null,
        });
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private async Task SendPaymentConfirmationAsync(Business business, string plan)
    {
        try
        {
            var owner = await _db.Users.FirstOrDefaultAsync(u => u.BusinessId == business.Id && u.Role == UserRole.Owner && u.IsActive);
            if (owner == null) return;
            var planLabel = plan[0..1].ToUpper() + plan[1..];
            var renewDate = business.SubscriptionEndsAt?.ToString("dd MMM yyyy") ?? "in 30 days";
            var currency = business.BillingCurrency ?? business.Currency ?? "USD";
            var bcCycle = IsAnnual(business.BillingCycle ?? "monthly") ? BillingConfig.BillingCycle.Annual : BillingConfig.BillingCycle.Monthly;
            var bcPrice = BillingConfig.GetPrice(plan, bcCycle, currency);
            var priceText = bcPrice.HasValue ? BillingConfig.FormatPrice(bcPrice.Value, currency) : "your plan price";
            var cycleLabel = bcCycle == BillingConfig.BillingCycle.Annual ? "year" : "month";

            var whatsApp = _serviceProvider.GetRequiredService<IWhatsAppService>();
            await whatsApp.SendMessageAsync(
                $"whatsapp:{owner.PhoneNumber}",
                $"✅ *Payment successful!*\n\nYour *{planLabel}* plan is now active at {priceText}/{cycleLabel}.\n\n" +
                $"Next renewal: {renewDate}\n\nSay *my plan* to see your features, or *help* for commands.",
                business.Id, owner.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send Stripe payment confirmation for {Business}", business.Name);
        }
    }

    private async Task SendPaymentFailedAsync(Business business)
    {
        try
        {
            var owner = await _db.Users.FirstOrDefaultAsync(u => u.BusinessId == business.Id && u.Role == UserRole.Owner && u.IsActive);
            if (owner == null) return;
            var planLabel = (business.Plan ?? "starter")[0..1].ToUpper() + (business.Plan ?? "starter")[1..];
            var whatsApp = _serviceProvider.GetRequiredService<IWhatsAppService>();
            await whatsApp.SendMessageAsync(
                $"whatsapp:{owner.PhoneNumber}",
                $"⚠️ *Payment Failed*\n\nYour card payment for Ojunai could not be processed. " +
                $"Please update your payment method at app.ojunai.com/settings to keep your {planLabel} plan active.",
                business.Id, owner.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send Stripe payment-failed WhatsApp for {Business}", business.Name);
        }
    }
}

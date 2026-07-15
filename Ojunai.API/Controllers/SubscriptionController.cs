using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Services;
using Ojunai.API.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ojunai.API.Controllers;

[Route("api/subscription")]
public class SubscriptionController : OjunaiBaseController
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTime> _lastInitialize = new();

    private readonly PaystackService _paystack;
    private readonly FlutterwaveService _flutterwave;
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly IUsageService _usage;
    private readonly ILogger<SubscriptionController> _logger;
    private readonly IActivityLogger _activity;

    public SubscriptionController(
        PaystackService paystack,
        FlutterwaveService flutterwave,
        AppDbContext db,
        IConfiguration config,
        IUsageService usage,
        ILogger<SubscriptionController> logger,
        IActivityLogger activity)
    {
        _paystack = paystack;
        _flutterwave = flutterwave;
        _db = db;
        _config = config;
        _usage = usage;
        _logger = logger;
        _activity = activity;
    }

    /// <summary>
    /// Per-business quota snapshot for the current calendar month. Read by the dashboard's
    /// quota meters + cap-hit upsell modal — refreshed every minute or so on the client.
    /// </summary>
    [HttpGet("quota")]
    public async Task<IActionResult> GetQuota(CancellationToken ct)
    {
        var snapshot = await _usage.GetSnapshotAsync(BusinessId, ct);
        return Ok(snapshot);
    }

    /// <summary>
    /// WhatsApp pack catalog + the business's currently active pack (if any). Public catalog data
    /// + per-business state combined into one call so the pack picker only does one round-trip.
    /// </summary>
    [HttpGet("whatsapp-packs")]
    public async Task<IActionResult> GetWhatsAppPacks(CancellationToken ct)
    {
        // EF can't translate the range expression `[...]` for the code suffix, so we pull the
        // raw row and strip the prefix client-side. Cheap — at most one row matches.
        var activeRow = await _db.BusinessAddOns
            .Where(a => a.BusinessId == BusinessId
                && a.Status == "active"
                && a.AddOnCode.StartsWith("whatsapp_pack."))
            .OrderByDescending(a => a.UpdatedAtUtc)
            .Select(a => new
            {
                a.AddOnCode,
                a.BilledAmount,
                a.BilledCurrency,
                a.NextBillingAtUtc,
                a.AddedAtUtc,
                a.IsAutoRenew,
            })
            .FirstOrDefaultAsync(ct);

        object? activePack = activeRow == null ? null : new
        {
            code = activeRow.AddOnCode["whatsapp_pack.".Length..],
            activeRow.BilledAmount,
            activeRow.BilledCurrency,
            activeRow.NextBillingAtUtc,
            activeRow.AddedAtUtc,
            activeRow.IsAutoRenew,
        };

        return Ok(new
        {
            catalog = BillingConfig.GetAllWhatsAppPackPricing(),
            activePack,
        });
    }

    /// <summary>
    /// Initialize a real WhatsApp pack purchase. Routes to Paystack (NGN) or Flutterwave
    /// (everything else) — one-time charge, no auto-renew. On successful payment the
    /// provider's webhook upserts the BusinessAddOn row.
    /// </summary>
    [HttpPost("whatsapp-packs/purchase")]
    [RequirePermission(Permission.ManageSettings)]
    public async Task<ActionResult<ApiResponse<object>>> PurchaseWhatsAppPack(
        [FromBody] ActivateWhatsAppPackRequest request,
        CancellationToken ct)
    {
        var packCode = (request.Code ?? "").ToLowerInvariant().Trim();
        if (string.IsNullOrEmpty(packCode) || !BillingConfig.WhatsAppPackCodes.Contains(packCode))
            return BadRequest(ApiResponse<object>.Fail("Unknown WhatsApp pack code."));

        var business = await _db.Businesses.FindAsync(new object[] { BusinessId }, ct);
        if (business == null) return NotFound(ApiResponse<object>.Fail("Business not found."));
        if (!business.IsBillable) return BadRequest(ApiResponse<object>.Fail("This account is not billable."));

        var currency = (business.BillingCurrency ?? business.Currency ?? "NGN").ToUpperInvariant();
        if (!BillingConfig.IsCurrencySupported(currency))
            return BadRequest(ApiResponse<object>.Fail($"Billing in {currency} isn't supported yet."));

        var cycleStr = (business.BillingCycle ?? "monthly").ToLowerInvariant();
        if (!BillingConfig.IsValidWhatsAppPackCombination(packCode, cycleStr, currency))
            return BadRequest(ApiResponse<object>.Fail(
                $"No price for pack '{packCode}' / {cycleStr} / {currency}."));

        var user = await _db.Users.FindAsync(new object[] { UserId }, ct);
        var email = user?.Email ?? $"{user?.PhoneNumber}@ojunai.com";

        var provider = BillingConfig.GetProvider(currency);

        if (provider == BillingConfig.BillingProvider.Paystack)
        {
            var url = await _paystack.InitializeWhatsAppPackChargeAsync(
                BusinessId, packCode, email, autoRenew: request.AutoRenew);
            return Ok(ApiResponse<object>.Ok(
                new { paymentUrl = url, provider = "paystack", autoRenew = request.AutoRenew },
                "Redirecting to payment..."));
        }

        // Flutterwave inline checkout — same shape as the tier flow so frontend reuses the
        // existing checkout component.
        var cycle = cycleStr == "annual"
            ? BillingConfig.BillingCycle.Annual
            : BillingConfig.BillingCycle.Monthly;
        var amount = BillingConfig.GetWhatsAppPackPriceOrThrow(packCode, cycle, currency);
        var txRef = FlutterwaveService.BuildPackTxRef(packCode, BusinessId);
        var publicKey = _config["Flutterwave:PublicKey"];
        if (string.IsNullOrEmpty(publicKey))
            return StatusCode(500, ApiResponse<object>.Fail("Payment gateway not configured."));
        var callbackUrl = (_config["Flutterwave:CallbackUrl"] ?? "https://app.ojunai.com/settings")
            + "?pack=true";

        return Ok(ApiResponse<object>.Ok(new
        {
            provider = "flutterwave",
            inlineCheckout = true,
            publicKey,
            txRef,
            amount,
            currency,
            email,
            packCode,
            billingCycle = cycleStr,
            callbackUrl,
            businessId = BusinessId.ToString(),
            businessName = business.Name,
        }, "Ready for checkout."));
    }

    /// <summary>
    /// Cancel auto-renew on the business's currently active WhatsApp pack. Pack stays usable
    /// until the end of the current billing period; PackExpiryJobService expires it afterwards.
    /// </summary>
    [HttpPost("whatsapp-packs/cancel-auto-renew")]
    [RequirePermission(Permission.ManageSettings)]
    public async Task<ActionResult<ApiResponse<object>>> CancelWhatsAppPackAutoRenew()
    {
        await _paystack.CancelWhatsAppPackAutoRenewAsync(BusinessId);
        return Ok(ApiResponse<object>.Ok(null!, "Auto-renew cancelled. Pack stays active until the end of the current billing period."));
    }

    /// <summary>
    /// Manually activate a WhatsApp pack for the current business. ADMIN/DEV — bypasses
    /// Paystack/Flutterwave so internal staff can grant packs without a real charge (support
    /// flow, testing). End-user UI uses /whatsapp-packs/purchase instead.
    ///
    /// Behavior:
    ///   - Cancels any existing active WhatsApp pack on this business (one pack at a time).
    ///   - Upserts a BusinessAddOn for the new pack code at the current month's BilledAmount.
    ///   - Returns the activated pack row.
    /// </summary>
    [HttpPost("whatsapp-packs/activate")]
    [RequirePermission(Permission.ManageSettings)]
    public async Task<ActionResult<ApiResponse<object>>> ActivateWhatsAppPack(
        [FromBody] ActivateWhatsAppPackRequest request,
        CancellationToken ct)
    {
        // This endpoint activates a PAID pack WITHOUT charging (admin/dev bypass). The normal flow is
        // the payment-provider webhook. Guard it with the admin key so a business owner (who holds
        // ManageSettings) cannot grant themselves a free paid pack. Header X-Admin-Key preferred; a
        // ?key= query fallback matches the existing admin toolkit convention.
        var adminKey = Request.Headers["X-Admin-Key"].FirstOrDefault() ?? Request.Query["key"].FirstOrDefault();
        var adminSecret = _config["Admin:AnalyticsKey"];
        if (string.IsNullOrEmpty(adminSecret) || adminSecret.Length < 32
            || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(adminKey ?? ""),
                System.Text.Encoding.UTF8.GetBytes(adminSecret)))
        {
            return StatusCode(403, ApiResponse<object>.Fail("This action requires administrator authorization."));
        }

        var code = (request.Code ?? "").ToLowerInvariant().Trim();
        if (string.IsNullOrEmpty(code) || !BillingConfig.WhatsAppPackCodes.Contains(code))
            return BadRequest(ApiResponse<object>.Fail("Unknown WhatsApp pack code."));

        var business = await _db.Businesses.FindAsync(new object[] { BusinessId }, ct);
        if (business == null) return NotFound(ApiResponse<object>.Fail("Business not found."));

        var currency = (business.BillingCurrency ?? business.Currency ?? "NGN").ToUpperInvariant();
        var cycle = (business.BillingCycle ?? "monthly").Equals("annual", StringComparison.OrdinalIgnoreCase)
            ? BillingConfig.BillingCycle.Annual
            : BillingConfig.BillingCycle.Monthly;

        var amount = BillingConfig.GetWhatsAppPackPrice(code, cycle, currency);
        if (!amount.HasValue)
            return BadRequest(ApiResponse<object>.Fail(
                $"No price for pack '{code}' in {currency}. Switch your billing currency first."));

        var now = DateTime.UtcNow;
        var nextBilling = cycle == BillingConfig.BillingCycle.Annual ? now.AddYears(1) : now.AddMonths(1);

        // Cancel any existing active WhatsApp pack — one pack per business at a time.
        var existing = await _db.BusinessAddOns
            .Where(a => a.BusinessId == BusinessId
                && a.Status == "active"
                && a.AddOnCode.StartsWith("whatsapp_pack."))
            .ToListAsync(ct);
        foreach (var old in existing)
        {
            old.Status = "cancelled";
            old.CancelledAtUtc = now;
            old.UpdatedAtUtc = now;
        }

        var addOn = new BusinessAddOn
        {
            BusinessId = BusinessId,
            AddOnCode = $"whatsapp_pack.{code}",
            Status = "active",
            Quantity = 1,
            BilledAmount = amount.Value,
            BilledCurrency = currency,
            AddedAtUtc = now,
            NextBillingAtUtc = nextBilling,
            UpdatedAtUtc = now,
        };
        _db.BusinessAddOns.Add(addOn);
        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse<object>.Ok(new
        {
            code,
            label = BillingConfig.WhatsAppPackLabels[code],
            actions = BillingConfig.WhatsAppPackActions[code],
            billedAmount = amount.Value,
            billedCurrency = currency,
            nextBillingAtUtc = nextBilling,
        }, $"{BillingConfig.WhatsAppPackLabels[code]} activated."));
    }

    /// <summary>
    /// Returns all pricing data for the frontend pricing page.
    /// Public endpoint — no auth needed so the pricing page can render for visitors.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("pricing")]
    public IActionResult GetPricing()
    {
        return Ok(BillingConfig.GetAllPricing());
    }

    /// <summary>
    /// Initialize a checkout session. Routes to Paystack (NGN) or Flutterwave (non-NGN)
    /// based on the selected currency. Returns a payment page URL.
    /// </summary>
    [HttpPost("initialize")]
    [RequirePermission(Permission.ManageSettings)]
    public async Task<ActionResult<ApiResponse<object>>> Initialize([FromBody] InitializeSubscriptionRequest request)
    {
        if (_lastInitialize.TryGetValue(BusinessId, out var last) && (DateTime.UtcNow - last).TotalSeconds < 10)
            return BadRequest(ApiResponse<object>.Fail("Please wait a moment before trying again."));
        _lastInitialize[BusinessId] = DateTime.UtcNow;

        var business = await _db.Businesses.FindAsync(BusinessId);
        if (business == null) return NotFound(ApiResponse<object>.Fail("Business not found."));
        if (!business.IsBillable) return BadRequest(ApiResponse<object>.Fail("This account is not billable."));

        var plan = request.Plan?.ToLower() ?? "";
        var currency = request.Currency?.ToUpper() ?? business.Currency ?? "NGN";
        var cycle = request.BillingCycle?.ToLower() ?? "monthly";

        // Reject unsupported currencies up-front with a clear message — protects against silently
        // charging the wrong amount when a Pan-African user picks a currency we don't yet support.
        if (!BillingConfig.IsCurrencySupported(currency))
        {
            return BadRequest(ApiResponse<object>.Fail(
                $"Billing in {currency} isn't supported yet. Supported: {string.Join(", ", BillingConfig.SupportedCurrencies)}. Contact support if you'd like {currency} added."));
        }

        if (!BillingConfig.IsValidCombination(plan, cycle, currency))
            return BadRequest(ApiResponse<object>.Fail($"Invalid plan/cycle/currency combination: {plan}/{cycle}/{currency}"));

        var user = await _db.Users.FindAsync(UserId);
        var email = user?.Email ?? $"{user?.PhoneNumber}@ojunai.com";

        var provider = BillingConfig.GetProvider(currency);

        // Store the billing context
        business.BillingProvider = provider.ToString().ToLower();
        business.BillingCycle = cycle;
        business.BillingCurrency = currency;
        business.PendingPlanChange = null;
        await _db.SaveChangesAsync();

        if (provider == BillingConfig.BillingProvider.Paystack)
        {
            var url = await _paystack.InitializeSubscriptionAsync(BusinessId, plan, email);
            return Ok(ApiResponse<object>.Ok(new { paymentUrl = url, provider = "paystack" },
                "Redirecting to payment..."));
        }

        // Flutterwave: return inline checkout config for the frontend JS SDK
        if (!Enum.TryParse<BillingConfig.BillingCycle>(cycle, true, out var billingCycle))
            return BadRequest(ApiResponse<object>.Fail("Invalid billing cycle."));

        // GetPriceOrThrow surfaces unsupported plan/cycle/currency loudly. The IsValidCombination
        // check above should have caught this, but the throw is a defensive safety net so we never
        // accidentally initialize a Flutterwave transaction at amount=0.
        var fullAmount = BillingConfig.GetPriceOrThrow(plan, billingCycle, currency);

        // Mid-cycle delta upgrade — mirrors PaystackService.InitializeSubscriptionAsync. Eligible when
        // monthly, currently on an ACTIVE paid plan with a future end date, the new plan costs more, and
        // ≥10 days remain. Charge ONLY the price difference and keep the existing cycle end. The "-delta-"
        // marker in txRef tells the webhook to validate the delta (not full price) and not extend the period.
        // One-time charge (no recurring plan), so auto-renew is off this cycle — the expiry banner prompts a
        // re-subscribe, same as the Paystack delta fallback. Annual always goes full price.
        var chargeAmount = fullAmount;
        var isDeltaUpgrade = false;
        if (billingCycle == BillingConfig.BillingCycle.Monthly
            && string.Equals(business.SubscriptionStatus, "active", StringComparison.OrdinalIgnoreCase)
            && business.SubscriptionEndsAt.HasValue && business.SubscriptionEndsAt.Value > DateTime.UtcNow
            && !string.IsNullOrEmpty(business.SubscribedPlan))
        {
            var currentPrice = BillingConfig.GetPrice(business.SubscribedPlan!, billingCycle, currency) ?? 0;
            var daysRemaining = (business.SubscriptionEndsAt.Value - DateTime.UtcNow).TotalDays;
            if (fullAmount > currentPrice && daysRemaining >= 10)
            {
                isDeltaUpgrade = true;
                chargeAmount = fullAmount - currentPrice;
            }
        }

        var txRef = isDeltaUpgrade
            ? $"ojunai-{BusinessId:N}-{plan}-{cycle}-delta-{DateTime.UtcNow.Ticks}"
            : $"ojunai-{BusinessId:N}-{plan}-{cycle}-{DateTime.UtcNow.Ticks}";
        var publicKey = _config["Flutterwave:PublicKey"];
        if (string.IsNullOrEmpty(publicKey))
            return StatusCode(500, ApiResponse<object>.Fail("Payment gateway not configured. Please contact support."));
        var callbackUrl = _config["Flutterwave:CallbackUrl"] ?? "https://app.ojunai.com/settings";

        // Recurring payment plan only for full-price subscriptions. A delta upgrade is one-time — attaching
        // a plan would set up recurring billing at the (wrong) delta amount.
        int? paymentPlanId = isDeltaUpgrade
            ? null
            : await _flutterwave.GetOrCreatePaymentPlanAsync(plan, billingCycle, currency, fullAmount);

        return Ok(ApiResponse<object>.Ok(new
        {
            provider = "flutterwave",
            inlineCheckout = true,
            publicKey,
            txRef,
            amount = chargeAmount,
            currency,
            email,
            plan,
            billingCycle = cycle,
            callbackUrl,
            businessId = BusinessId.ToString(),
            businessName = business.Name,
            paymentPlanId,
            isUpgradeDelta = isDeltaUpgrade,
        }, "Ready for checkout."));
    }

    [HttpPost("cancel")]
    [RequirePermission(Permission.ManageSettings)]
    public async Task<ActionResult<ApiResponse<object>>> Cancel()
    {
        var business = await _db.Businesses.FindAsync(BusinessId);
        if (business == null) return NotFound(ApiResponse<object>.Fail("Business not found."));

        if (business.BillingProvider == "flutterwave")
            await _flutterwave.CancelSubscriptionAsync(BusinessId);
        else
            await _paystack.CancelSubscriptionAsync(BusinessId);

        // Clear any pending downgrade — user explicitly cancelled, so scheduled changes are moot
        if (!string.IsNullOrEmpty(business.PendingPlanChange))
        {
            business.PendingPlanChange = null;
            await _db.SaveChangesAsync();
        }

        return Ok(ApiResponse<object>.Ok(null!, "Subscription cancelled. You'll keep access until the end of your billing period."));
    }

    [HttpPost("change-plan")]
    [RequirePermission(Permission.ManageSettings)]
    public async Task<ActionResult<ApiResponse<object>>> ChangePlan([FromBody] ChangePlanRequest request)
    {
        var business = await _db.Businesses.FindAsync(BusinessId);
        if (business == null) return NotFound(ApiResponse<object>.Fail("Business not found."));

        var targetPlan = request.Plan?.ToLower();
        var planConfig = PlanLimits.Get(targetPlan ?? "");
        if (planConfig.PricePerMonth <= 0 && targetPlan != "starter")
            return BadRequest(ApiResponse<object>.Fail("Invalid plan. Choose: starter, shop, pro, or business."));

        // Rank the change against the actually-PAID tier (SubscribedPlan), NOT the effective Plan. A
        // trial elevates business.Plan above SubscribedPlan, so keying the "downgrades only" guard off
        // Plan let a merchant on a Pro trial "downgrade" into a paid tier ABOVE what they actually pay
        // for — a free upgrade. Fall back to Plan only when no paid tier is recorded.
        var paidPlan = string.IsNullOrEmpty(business.SubscribedPlan) ? business.Plan : business.SubscribedPlan;
        var currentRank = PlanGuard.PlanRank(paidPlan);
        var targetRank = PlanGuard.PlanRank(targetPlan);

        if (targetRank == currentRank)
            return BadRequest(ApiResponse<object>.Fail($"You're already on the {business.Plan} plan."));

        if (targetRank > currentRank)
            return BadRequest(ApiResponse<object>.Fail("Use the subscribe/upgrade button for upgrades — this endpoint handles downgrades only."));

        var targetLabel = targetPlan![0..1].ToUpper() + targetPlan[1..];

        if (business.SubscriptionEndsAt.HasValue && business.SubscriptionEndsAt > DateTime.UtcNow)
        {
            business.PendingPlanChange = targetPlan;
            await _activity.LogAsync(BusinessId, "plan.downgrade_scheduled", "Billing", null, targetLabel,
                $"scheduled downgrade to {targetPlan}");
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(null!,
                $"Plan change scheduled. You'll keep your current features until {business.SubscriptionEndsAt.Value:dd MMM yyyy}, then switch to {targetLabel}."));
        }

        business.Plan = targetPlan;
        business.SubscribedPlan = targetPlan;
        business.PendingPlanChange = null;
        business.TrialEndsAt = null;
        await _activity.LogAsync(BusinessId, "plan.changed", "Billing", null, targetLabel,
            $"changed plan to {targetPlan}");
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(null!, $"Plan changed to {targetLabel}."));
    }

    [HttpPost("cancel-pending-change")]
    [RequirePermission(Permission.ManageSettings)]
    public async Task<ActionResult<ApiResponse<object>>> CancelPendingChange()
    {
        var business = await _db.Businesses.FindAsync(BusinessId);
        if (business == null) return NotFound(ApiResponse<object>.Fail("Business not found."));

        if (string.IsNullOrEmpty(business.PendingPlanChange))
            return BadRequest(ApiResponse<object>.Fail("No pending plan change to cancel."));

        var was = business.PendingPlanChange;
        business.PendingPlanChange = null;
        await _activity.LogAsync(BusinessId, "plan.downgrade_scheduled_cancelled", "Billing", null, was,
            "cancelled scheduled downgrade");
        await _db.SaveChangesAsync();

        var currentLabel = business.Plan[0..1].ToUpper() + business.Plan[1..];
        return Ok(ApiResponse<object>.Ok(null!, $"Scheduled downgrade cancelled. You'll stay on {currentLabel}."));
    }

    /// <summary>
    /// Verify a Flutterwave Inline checkout payment after the frontend JS SDK callback fires.
    /// Checks the transaction via Flutterwave API and activates the subscription.
    /// </summary>
    [HttpPost("verify-flutterwave")]
    [RequirePermission(Permission.ManageSettings)]
    public async Task<ActionResult<ApiResponse<object>>> VerifyFlutterwave([FromBody] VerifyFlutterwaveRequest request)
    {
        if (string.IsNullOrEmpty(request.TransactionId) && string.IsNullOrEmpty(request.TxRef))
            return BadRequest(ApiResponse<object>.Fail("Transaction ID or reference is required."));

        var result = await _flutterwave.VerifyAndActivateAsync(BusinessId, request.TransactionId, request.TxRef);
        if (result != null)
            return BadRequest(ApiResponse<object>.Fail(result));

        return Ok(ApiResponse<object>.Ok(null!, "Payment verified. Your plan is now active."));
    }

    /// <summary>Paystack webhook (NGN payments)</summary>
    [AllowAnonymous]
    [HttpPost("webhook")]
    [RequestSizeLimit(128 * 1024)]
    public async Task<IActionResult> PaystackWebhook()
    {
        var secret = _config["Paystack:SecretKey"];
        if (string.IsNullOrEmpty(secret)) return StatusCode(500);

        if (!Request.Headers.TryGetValue("x-paystack-signature", out var signature))
        {
            _logger.LogWarning("Paystack webhook received without x-paystack-signature header");
            return Unauthorized();
        }

        Request.Body.Position = 0;
        string body;
        using (var reader = new StreamReader(Request.Body))
            body = await reader.ReadToEndAsync();

        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(secret));
        var hash = BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).Replace("-", "").ToLower();

        var hashBytes = Encoding.UTF8.GetBytes(hash);
        var sigBytes = Encoding.UTF8.GetBytes(signature.ToString().ToLower());
        if (hashBytes.Length != sigBytes.Length || !CryptographicOperations.FixedTimeEquals(hashBytes, sigBytes))
        {
            _logger.LogWarning("Paystack webhook signature verification failed");
            return Unauthorized();
        }

        using var payload = JsonDocument.Parse(body);
        await _paystack.HandleWebhookAsync(payload.RootElement);
        return Ok();
    }

    /// <summary>Flutterwave webhook (non-NGN payments)</summary>
    [AllowAnonymous]
    [HttpPost("webhook/flutterwave")]
    [RequestSizeLimit(128 * 1024)]
    public async Task<IActionResult> FlutterwaveWebhook()
    {
        // Flutterwave sends a secret hash in this header for verification
        var hash = Request.Headers["verif-hash"].ToString();
        if (!_flutterwave.VerifyWebhook(hash))
        {
            _logger.LogWarning("Flutterwave webhook signature verification failed");
            return Unauthorized();
        }

        Request.Body.Position = 0;
        string body;
        using (var reader = new StreamReader(Request.Body))
            body = await reader.ReadToEndAsync();

        using var payload = JsonDocument.Parse(body);
        await _flutterwave.HandleWebhookAsync(payload.RootElement);
        return Ok();
    }

    // ── Voice AI add-on ──────────────────────────────────────────────────────

    [AllowAnonymous]
    [HttpGet("voice-ai-pricing")]
    public IActionResult GetVoiceAIPricing()
    {
        if (!_config.GetValue<bool>("VoiceAI:FeatureEnabled"))
            return NotFound();
        return Ok(BillingConfig.GetVoiceAIPricing());
    }

    [HttpPost("voice-ai/initialize")]
    [RequirePermission(Permission.ManageSettings)]
    public async Task<ActionResult<ApiResponse<object>>> InitializeVoiceAI([FromBody] InitializeVoiceAIRequest request)
    {
        if (!_config.GetValue<bool>("VoiceAI:FeatureEnabled"))
            return NotFound(ApiResponse<object>.Fail("Voice AI is not available."));

        if (_lastInitialize.TryGetValue(BusinessId, out var last) && (DateTime.UtcNow - last).TotalSeconds < 10)
            return BadRequest(ApiResponse<object>.Fail("Please wait a moment before trying again."));
        _lastInitialize[BusinessId] = DateTime.UtcNow;

        var business = await _db.Businesses.FindAsync(BusinessId);
        if (business == null) return NotFound(ApiResponse<object>.Fail("Business not found."));
        if (!business.IsBillable) return BadRequest(ApiResponse<object>.Fail("This account is not billable."));

        var tier = request.Tier?.ToLower();
        if (string.IsNullOrEmpty(tier) || !BillingConfig.VoiceAITierCodes.Contains(tier))
            return BadRequest(ApiResponse<object>.Fail(
                $"Invalid Voice AI tier. Pick one of: {string.Join(", ", BillingConfig.VoiceAITierCodes)}."));

        var currency = request.Currency?.ToUpper() ?? business.BillingCurrency ?? business.Currency ?? "NGN";
        var cycle = request.BillingCycle?.ToLower() ?? "monthly";

        if (!BillingConfig.IsValidVoiceAITierCombination(tier, cycle, currency))
            return BadRequest(ApiResponse<object>.Fail(
                $"Invalid Voice tier/cycle/currency: {tier}/{cycle}/{currency}"));

        if (!Enum.TryParse<BillingConfig.BillingCycle>(cycle, true, out var billingCycle))
            return BadRequest(ApiResponse<object>.Fail("Invalid billing cycle."));

        var amount = BillingConfig.GetVoiceAITierPriceOrThrow(tier, billingCycle, currency);
        var user = await _db.Users.FindAsync(UserId);
        var email = user?.Email ?? $"{user?.PhoneNumber}@ojunai.com";
        var provider = BillingConfig.GetProvider(currency);

        var txRef = $"ojunai-voiceai-{tier}-{BusinessId:N}-{cycle}-{DateTime.UtcNow.Ticks}";

        if (provider == BillingConfig.BillingProvider.Paystack)
        {
            var url = await _paystack.InitializeVoiceAIAsync(BusinessId, email, amount, currency, cycle, tier);
            return Ok(ApiResponse<object>.Ok(new { paymentUrl = url, provider = "paystack", tier },
                "Redirecting to payment..."));
        }

        var publicKey = _config["Flutterwave:PublicKey"];
        if (string.IsNullOrEmpty(publicKey))
            return StatusCode(500, ApiResponse<object>.Fail("Payment gateway not configured."));
        var callbackUrl = _config["Flutterwave:CallbackUrl"] ?? "https://app.ojunai.com/settings";

        return Ok(ApiResponse<object>.Ok(new
        {
            provider = "flutterwave",
            inlineCheckout = true,
            publicKey,
            txRef,
            amount,
            currency,
            email,
            plan = "voice_ai",
            billingCycle = cycle,
            callbackUrl,
            businessId = BusinessId.ToString(),
            businessName = business.Name,
            // tier is carried in meta so the Flutterwave webhook persists it onto Business.VoiceAITier
            meta = new { product = "voice_ai", tier },
        }, "Ready for checkout."));
    }

    [HttpPost("voice-ai/cancel")]
    [RequirePermission(Permission.ManageSettings)]
    public async Task<ActionResult<ApiResponse<object>>> CancelVoiceAI()
    {
        var business = await _db.Businesses.FindAsync(BusinessId);
        if (business == null) return NotFound(ApiResponse<object>.Fail("Business not found."));

        if (business.VoiceAIPlanStatus is not ("active" or "trial"))
            return BadRequest(ApiResponse<object>.Fail("Voice AI is not active."));

        business.VoiceAIPlanStatus = "suspended";
        business.VoiceAIEnabled = false;

        _db.BillingEvents.Add(new Models.BillingEvent
        {
            BusinessId = BusinessId,
            EventType = "voiceai.subscription.cancelled",
            Provider = business.BillingProvider,
            Plan = "voice_ai",
            Status = "cancelled"
        });

        await _activity.LogAsync(BusinessId, "voice_ai.cancelled", "Billing", null, "Voice AI",
            "disabled Voice AI");
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(null!, "Voice AI subscription cancelled."));
    }
}

public class InitializeSubscriptionRequest
{
    public string Plan { get; set; } = string.Empty;
    public string? Currency { get; set; }
    public string? BillingCycle { get; set; }
}

public class ActivateWhatsAppPackRequest
{
    public string? Code { get; set; }

    /// <summary>
    /// Opt the pack into auto-renew via Paystack subscription. Currently honored only for
    /// NGN/Paystack purchases — Flutterwave currencies fall back to one-time charges.
    /// </summary>
    public bool AutoRenew { get; set; } = false;
}

public class ChangePlanRequest
{
    public string Plan { get; set; } = string.Empty;
}

public class VerifyFlutterwaveRequest
{
    public string? TransactionId { get; set; }
    public string? TxRef { get; set; }
}

public class InitializeVoiceAIRequest
{
    /// <summary>"starter" ($39/mo, 300 min, 1 line) or "pro" ($79/mo, 1000 min, 3 lines).</summary>
    public string? Tier { get; set; }
    public string? Currency { get; set; }
    public string? BillingCycle { get; set; }
}

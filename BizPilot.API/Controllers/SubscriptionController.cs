using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BizPilot.API.Common;
using BizPilot.API.Data;
using BizPilot.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BizPilot.API.Controllers;

[Route("api/subscription")]
public class SubscriptionController : BizPilotBaseController
{
    private readonly PaystackService _paystack;
    private readonly FlutterwaveService _flutterwave;
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<SubscriptionController> _logger;

    public SubscriptionController(PaystackService paystack, FlutterwaveService flutterwave, AppDbContext db, IConfiguration config, ILogger<SubscriptionController> logger)
    {
        _paystack = paystack;
        _flutterwave = flutterwave;
        _db = db;
        _config = config;
        _logger = logger;
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
        var business = await _db.Businesses.FindAsync(BusinessId);
        if (business == null) return NotFound(ApiResponse<object>.Fail("Business not found."));
        if (!business.IsBillable) return BadRequest(ApiResponse<object>.Fail("This account is not billable."));

        var plan = request.Plan?.ToLower() ?? "";
        var currency = request.Currency?.ToUpper() ?? business.Currency ?? "NGN";
        var cycle = request.BillingCycle?.ToLower() ?? "monthly";

        if (!BillingConfig.IsValidCombination(plan, cycle, currency))
            return BadRequest(ApiResponse<object>.Fail($"Invalid plan/cycle/currency combination: {plan}/{cycle}/{currency}"));

        var user = await _db.Users.FindAsync(UserId);
        var email = user?.Email ?? $"{user?.PhoneNumber}@bizpilot-ai.com";

        var provider = BillingConfig.GetProvider(currency);

        // Store the billing context
        business.BillingProvider = provider.ToString().ToLower();
        business.BillingCycle = cycle;
        business.BillingCurrency = currency;
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

        var amount = BillingConfig.GetPrice(plan, billingCycle, currency)
            ?? 0m;

        var txRef = $"bizpilot-{BusinessId:N}-{plan}-{cycle}-{DateTime.UtcNow.Ticks}";
        var publicKey = _config["Flutterwave:PublicKey"];
        if (string.IsNullOrEmpty(publicKey))
            return StatusCode(500, ApiResponse<object>.Fail("Payment gateway not configured. Please contact support."));
        var callbackUrl = _config["Flutterwave:CallbackUrl"] ?? "https://app.bizpilot-ai.com/settings";

        // Create a payment plan for auto-renewing card subscriptions
        var paymentPlanId = await _flutterwave.GetOrCreatePaymentPlanAsync(plan, billingCycle, currency, amount);

        return Ok(ApiResponse<object>.Ok(new
        {
            provider = "flutterwave",
            inlineCheckout = true,
            publicKey,
            txRef,
            amount,
            currency,
            email,
            plan,
            billingCycle = cycle,
            callbackUrl,
            businessId = BusinessId.ToString(),
            businessName = business.Name,
            paymentPlanId,
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

        var currentRank = PlanGuard.PlanRank(business.Plan);
        var targetRank = PlanGuard.PlanRank(targetPlan);

        if (targetRank == currentRank)
            return BadRequest(ApiResponse<object>.Fail($"You're already on the {business.Plan} plan."));

        if (targetRank > currentRank)
            return BadRequest(ApiResponse<object>.Fail("Use the subscribe/upgrade button for upgrades — this endpoint handles downgrades only."));

        var targetLabel = targetPlan![0..1].ToUpper() + targetPlan[1..];

        if (business.SubscriptionEndsAt.HasValue && business.SubscriptionEndsAt > DateTime.UtcNow)
        {
            business.PendingPlanChange = targetPlan;
            await _db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(null!,
                $"Plan change scheduled. You'll keep your current features until {business.SubscriptionEndsAt.Value:dd MMM yyyy}, then switch to {targetLabel}."));
        }

        business.Plan = targetPlan;
        business.SubscribedPlan = targetPlan;
        business.PendingPlanChange = null;
        business.TrialEndsAt = null;
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
}

public class InitializeSubscriptionRequest
{
    public string Plan { get; set; } = string.Empty;
    public string? Currency { get; set; }
    public string? BillingCycle { get; set; }
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

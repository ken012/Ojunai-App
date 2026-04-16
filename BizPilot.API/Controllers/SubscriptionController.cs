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
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public SubscriptionController(PaystackService paystack, AppDbContext db, IConfiguration config)
    {
        _paystack = paystack;
        _db = db;
        _config = config;
    }

    [HttpPost("initialize")]
    [RequirePermission(Permission.ManageSettings)]
    public async Task<ActionResult<ApiResponse<object>>> Initialize([FromBody] InitializeSubscriptionRequest request)
    {
        var business = await _db.Businesses.FindAsync(BusinessId);
        if (business == null) return NotFound(ApiResponse<object>.Fail("Business not found."));
        if (!business.IsBillable) return BadRequest(ApiResponse<object>.Fail("This account is not billable."));

        var user = await _db.Users.FindAsync(UserId);
        var email = user?.Email ?? $"{user?.PhoneNumber}@bizpilot-ai.com";

        var url = await _paystack.InitializeSubscriptionAsync(BusinessId, request.Plan, email);
        return Ok(ApiResponse<object>.Ok(new { paymentUrl = url }, "Redirecting to payment..."));
    }

    [HttpPost("cancel")]
    [RequirePermission(Permission.ManageSettings)]
    public async Task<ActionResult<ApiResponse<object>>> Cancel()
    {
        await _paystack.CancelSubscriptionAsync(BusinessId);
        return Ok(ApiResponse<object>.Ok(null!, "Subscription cancelled. You'll keep access until the end of your billing period."));
    }

    /// <summary>
    /// Paystack webhook endpoint. Paystack POSTs JSON events here whenever a subscription is created, charged, or cancelled.
    ///
    /// Security:
    ///   - [AllowAnonymous] because Paystack doesn't have a JWT, but the signature check below verifies authenticity.
    ///   - HMAC-SHA512 signature verification with the Paystack secret key ensures the event came from Paystack.
    ///   - Body size limited to 128KB (Paystack events are small; this blocks DoS via giant payloads).
    ///   - Idempotency is handled inside PaystackService.HandleWebhookAsync (prevents replay attacks).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("webhook")]
    [RequestSizeLimit(128 * 1024)]
    public async Task<IActionResult> Webhook()
    {
        var secret = _config["Paystack:SecretKey"];
        if (string.IsNullOrEmpty(secret)) return StatusCode(500);

        // Paystack sends the signature in this header. Missing header = not a legitimate Paystack request.
        if (!Request.Headers.TryGetValue("x-paystack-signature", out var signature))
            return Unauthorized();

        // Read the raw body (body buffering was enabled in Program.cs so we can read it after ASP.NET Core already did).
        Request.Body.Position = 0;
        string body;
        using (var reader = new StreamReader(Request.Body))
        {
            body = await reader.ReadToEndAsync();
        }

        // Paystack signs the raw request body with your secret key using HMAC-SHA512.
        // We recompute the signature with the same secret and compare.
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(secret));
        var hash = BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).Replace("-", "").ToLower();

        // Constant-time comparison prevents timing attacks that could leak the expected signature byte-by-byte.
        var hashBytes = Encoding.UTF8.GetBytes(hash);
        var sigBytes = Encoding.UTF8.GetBytes(signature.ToString().ToLower());
        if (hashBytes.Length != sigBytes.Length || !CryptographicOperations.FixedTimeEquals(hashBytes, sigBytes))
            return Unauthorized();

        using var payload = JsonDocument.Parse(body);
        await _paystack.HandleWebhookAsync(payload.RootElement);

        return Ok();
    }
}

public class InitializeSubscriptionRequest
{
    public string Plan { get; set; } = string.Empty;
}

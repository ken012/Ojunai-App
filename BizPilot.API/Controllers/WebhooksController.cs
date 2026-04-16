using System.Security.Cryptography;
using System.Text;
using BizPilot.API.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BizPilot.API.Controllers;

[AllowAnonymous]
[Route("api/webhooks")]
[ApiController]
public class WebhooksController : ControllerBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WebhooksController> _logger;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public WebhooksController(IServiceScopeFactory scopeFactory, ILogger<WebhooksController> logger, IConfiguration config, IWebHostEnvironment env)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _config = config;
        _env = env;
    }

    [HttpPost("whatsapp")]
    [Consumes("application/x-www-form-urlencoded")]
    [RequestSizeLimit(64 * 1024)] // 64KB — Twilio webhooks are small form posts
    public async Task<IActionResult> Receive([FromForm] TwilioInboundForm form)
    {
        if (!await ValidateTwilioSignatureAsync())
        {
            _logger.LogWarning("Rejected webhook with invalid Twilio signature");
            return Unauthorized();
        }

        if (string.IsNullOrEmpty(form.From))
            return Ok(); // Status callback, ignore

        if (string.IsNullOrEmpty(form.Body))
        {
            // Media message (image, voice note, etc.) — reply with friendly message
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var whatsApp = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();
                await whatsApp.SendMessageAsync(form.From, "I can only process text messages for now. Please type your request.");
            });
            return Content("<Response/>", "text/xml");
        }

        _logger.LogInformation("Inbound WhatsApp from {From}: {Body}", form.From, form.Body);

        // Create a new scope so the DbContext isn't disposed before the task finishes
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var whatsApp = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();
            try
            {
                await whatsApp.HandleInboundAsync(form.From, form.MessageSid, form.Body);
            }
            catch (Exception ex)
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<WebhooksController>>();
                logger.LogError(ex, "Error processing message {Sid}", form.MessageSid);
            }
        });

        return Content("<Response/>", "text/xml");
    }

    private async Task<bool> ValidateTwilioSignatureAsync()
    {
        // Allow bypass ONLY in Development environment
        if (_env.IsDevelopment() && _config.GetValue<bool>("Twilio:SkipSignatureValidation"))
            return true;

        var authToken = _config["Twilio:AuthToken"];
        if (string.IsNullOrEmpty(authToken))
        {
            _logger.LogError("Twilio:AuthToken not configured");
            return false;
        }

        if (!Request.Headers.TryGetValue("X-Twilio-Signature", out var providedSig))
            return false;

        // Reconstruct the URL Twilio signed. Honor X-Forwarded-* from Nginx.
        var scheme = Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? Request.Scheme;
        var host = Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? Request.Host.Value;
        var url = $"{scheme}://{host}{Request.Path}{Request.QueryString}";

        // Read form fields, sort by key, append to URL
        Request.Body.Position = 0;
        var form = await Request.ReadFormAsync();
        var sb = new StringBuilder(url);
        foreach (var key in form.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            sb.Append(key).Append(form[key]);
        }

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(authToken));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        var expected = Convert.ToBase64String(hash);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(providedSig.ToString()));
    }
}

public class TwilioInboundForm
{
    [System.ComponentModel.DataAnnotations.Required]
    public string From { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string MessageSid { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string AccountSid { get; set; } = string.Empty;
}

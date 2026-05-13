using System.Text.Json;
using Amazon.SimpleNotificationService.Util;
using Microsoft.AspNetCore.Mvc;
using Ojunai.API.Services;

namespace Ojunai.API.Controllers;

/// <summary>
/// Receives Amazon SNS notifications for SES bounces + complaints. Wired in the SES
/// configuration set → SNS topic → HTTPS subscription pointing at /api/webhooks/ses.
///
/// Three SNS message types arrive here:
///   • SubscriptionConfirmation — fires once when AWS sets up the subscription. We must
///     GET the SubscribeURL to activate it. After this we receive Notifications.
///   • Notification              — the actual SES event envelope (bounce / complaint).
///   • UnsubscribeConfirmation   — fires if the subscription is torn down. No action needed.
///
/// Security: the AWS SDK's Message.IsMessageSignatureValid() handles cert download +
/// signature verification. Anything that fails verification is rejected with 400.
/// </summary>
[ApiController]
[Route("api/webhooks/ses")]
public class SesNotificationsController : ControllerBase
{
    private readonly ISuppressionService _suppression;
    private readonly ILogger<SesNotificationsController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public SesNotificationsController(
        ISuppressionService suppression,
        ILogger<SesNotificationsController> logger,
        IHttpClientFactory httpClientFactory)
    {
        _suppression = suppression;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        // SNS sends raw JSON in the body. AWS SDK parses + signature-verifies in one step.
        string body;
        using (var reader = new StreamReader(Request.Body))
            body = await reader.ReadToEndAsync(ct);

        Message message;
        try { message = Message.ParseMessage(body); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SNS message parse failed");
            return BadRequest("malformed SNS envelope");
        }

        if (!message.IsMessageSignatureValid())
        {
            _logger.LogWarning("SNS message signature invalid (topic={Topic} type={Type})", message.TopicArn, message.Type);
            return BadRequest("invalid signature");
        }

        if (message.IsSubscriptionType)
        {
            // Confirm the subscription by GET-ing the SubscribeURL. AWS gives us 3 days
            // before it expires, but doing it immediately is the standard pattern.
            using var http = _httpClientFactory.CreateClient();
            try
            {
                var resp = await http.GetAsync(message.SubscribeURL, ct);
                resp.EnsureSuccessStatusCode();
                _logger.LogInformation("SNS subscription confirmed for topic {Topic}", message.TopicArn);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to confirm SNS subscription {Url}", message.SubscribeURL);
                return StatusCode(500, "could not confirm subscription");
            }
            return Ok();
        }

        if (message.IsUnsubscriptionType)
        {
            _logger.LogWarning("SNS unsubscribe received for topic {Topic}", message.TopicArn);
            return Ok();
        }

        if (message.IsNotificationType)
        {
            await HandleSesEventAsync(message.MessageText, ct);
            return Ok();
        }

        _logger.LogWarning("Unknown SNS message type {Type}", message.Type);
        return Ok();
    }

    /// <summary>
    /// Parse the SES event JSON (which is the body of the SNS Notification) and suppress
    /// any bounced / complained recipients.
    ///
    /// SES event shape (notificationType: "Bounce"):
    ///   { "notificationType":"Bounce", "bounce": { "bounceType":"Permanent",
    ///       "bounceSubType":"General", "bouncedRecipients":[ {"emailAddress":"..."} ] }, ... }
    /// SES event shape (notificationType: "Complaint"):
    ///   { "notificationType":"Complaint", "complaint": {
    ///       "complainedRecipients":[ {"emailAddress":"..."} ] }, ... }
    /// </summary>
    private async Task HandleSesEventAsync(string sesPayload, CancellationToken ct)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(sesPayload); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SES payload parse failed: {Body}", sesPayload);
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;
            var type = root.TryGetProperty("notificationType", out var t) ? t.GetString() : null;

            if (type == "Bounce")
            {
                var bounce = root.GetProperty("bounce");
                var bounceType = bounce.TryGetProperty("bounceType", out var bt) ? bt.GetString() : null;
                var bounceSubType = bounce.TryGetProperty("bounceSubType", out var bst) ? bst.GetString() : null;

                // Only permanent bounces go on the suppression list. Transient bounces are
                // mailbox-full / out-of-office / temporary DNS — they can recover and we'd
                // be wrong to permanently block them.
                if (!string.Equals(bounceType, "Permanent", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "Ignoring non-permanent bounce type={BounceType} subType={SubType}",
                        bounceType, bounceSubType);
                    return;
                }

                if (bounce.TryGetProperty("bouncedRecipients", out var rs) && rs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var r in rs.EnumerateArray())
                    {
                        var email = r.TryGetProperty("emailAddress", out var ea) ? ea.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(email))
                        {
                            await _suppression.SuppressAsync(email!, "bounce", bounceType, bounceSubType, sesPayload, ct);
                        }
                    }
                }
            }
            else if (type == "Complaint")
            {
                var complaint = root.GetProperty("complaint");
                if (complaint.TryGetProperty("complainedRecipients", out var rs) && rs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var r in rs.EnumerateArray())
                    {
                        var email = r.TryGetProperty("emailAddress", out var ea) ? ea.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(email))
                        {
                            await _suppression.SuppressAsync(email!, "complaint", null, null, sesPayload, ct);
                        }
                    }
                }
            }
            else
            {
                // SES also publishes Delivery / DeliveryDelay / Send / etc events depending on
                // configuration set settings — those are informational and don't change state.
                _logger.LogDebug("Ignoring SES notificationType={Type}", type);
            }
        }
    }
}

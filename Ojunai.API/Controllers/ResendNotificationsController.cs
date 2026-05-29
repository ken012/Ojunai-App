using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Ojunai.API.Services;

namespace Ojunai.API.Controllers;

/// <summary>
/// Receives webhook events from Resend (email provider). Wired in Resend's dashboard:
/// Webhooks → Add endpoint → URL: https://api.ojunai.com/api/webhooks/resend, subscribe
/// to email.bounced and email.complained.
///
/// Replaces the prior SES + SNS pipeline. AWS removed our SES setup after denying the
/// production-access request; Resend is the new primary email provider.
///
/// Signature verification uses Svix (the webhook delivery layer Resend uses under the hood):
///   • Three headers carry the proof: svix-id, svix-timestamp, svix-signature
///   • Signed content is the literal string "{id}.{timestamp}.{rawBody}"
///   • HMAC-SHA256 with the dashboard signing-secret (after stripping "whsec_" + base64-decode)
///   • The header can contain multiple space-delimited signatures (key rotation window),
///     each prefixed with version like "v1,..." — we accept if ANY matches.
///
/// Suppression behavior mirrors the old SES controller:
///   • email.bounced + bounce.type == "Permanent" → write to SuppressedEmails
///   • email.complained → always suppress (recipient told their provider it's spam)
///   • Transient bounces, deliveries, opens, clicks → logged at debug, no state change
/// </summary>
[ApiController]
[Route("api/webhooks/resend")]
public class ResendNotificationsController : ControllerBase
{
    private readonly ISuppressionService _suppression;
    private readonly IConfiguration _config;
    private readonly ILogger<ResendNotificationsController> _logger;

    // Tolerance window for the timestamp on the signed message. Svix doesn't mandate a value
    // but recommends a few minutes — we use 5 to absorb clock skew + slow delivery without
    // accepting replays from hours ago.
    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromMinutes(5);

    public ResendNotificationsController(
        ISuppressionService suppression,
        IConfiguration config,
        ILogger<ResendNotificationsController> logger)
    {
        _suppression = suppression;
        _config = config;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        // Read the raw body — Svix signs the literal bytes, so any deserialize-then-reserialize
        // round-trip would invalidate the signature.
        string body;
        using (var reader = new StreamReader(Request.Body))
            body = await reader.ReadToEndAsync(ct);

        var svixId = Request.Headers["svix-id"].ToString();
        var svixTimestamp = Request.Headers["svix-timestamp"].ToString();
        var svixSignature = Request.Headers["svix-signature"].ToString();

        if (string.IsNullOrEmpty(svixId) || string.IsNullOrEmpty(svixTimestamp) || string.IsNullOrEmpty(svixSignature))
        {
            _logger.LogWarning("Resend webhook rejected: missing svix headers");
            return BadRequest("missing svix headers");
        }

        if (!VerifySignature(svixId, svixTimestamp, body, svixSignature))
        {
            _logger.LogWarning("Resend webhook rejected: signature invalid (id={Id})", svixId);
            return BadRequest("invalid signature");
        }

        // Parse the event envelope. We only care about a few event types — anything else is
        // logged at debug and ignored.
        try
        {
            using var doc = JsonDocument.Parse(body);
            await HandleEventAsync(doc.RootElement, body, ct);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Resend webhook body was not valid JSON");
            return BadRequest("malformed body");
        }

        return Ok();
    }

    private async Task HandleEventAsync(JsonElement root, string rawBody, CancellationToken ct)
    {
        var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (string.IsNullOrEmpty(type))
        {
            _logger.LogDebug("Resend event missing 'type' field");
            return;
        }

        if (!root.TryGetProperty("data", out var data))
        {
            _logger.LogDebug("Resend event missing 'data' field (type={Type})", type);
            return;
        }

        if (type == "email.bounced")
        {
            // data.bounce.type is "Permanent" or "Temporary". Only permanent bounces go on
            // the suppression list — transient (mailbox full, OOO, dns flake) can recover.
            string? bounceType = null;
            string? bounceSubType = null;
            if (data.TryGetProperty("bounce", out var bounce))
            {
                if (bounce.TryGetProperty("type", out var bt)) bounceType = bt.GetString();
                if (bounce.TryGetProperty("subType", out var bst)) bounceSubType = bst.GetString();
            }

            if (!string.Equals(bounceType, "Permanent", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "Ignoring non-permanent bounce from Resend (type={Type} subType={SubType})",
                    bounceType, bounceSubType);
                return;
            }

            foreach (var addr in ExtractRecipients(data))
            {
                await _suppression.SuppressAsync(addr, "bounce", bounceType, bounceSubType, rawBody, ct);
            }
        }
        else if (type == "email.complained")
        {
            foreach (var addr in ExtractRecipients(data))
            {
                await _suppression.SuppressAsync(addr, "complaint", null, null, rawBody, ct);
            }
        }
        else
        {
            // sent/delivered/opened/clicked/scheduled/etc — no state change here. Keep the
            // log line at debug so production logs aren't drowned by every successful send.
            _logger.LogDebug("Ignoring Resend event type={Type}", type);
        }
    }

    /// <summary>
    /// Pull addresses out of data.to. Resend models it as an array of strings ("to":["a@x.com"])
    /// but defensively handle the single-string case too in case a future event type differs.
    /// </summary>
    private static IEnumerable<string> ExtractRecipients(JsonElement data)
    {
        if (!data.TryGetProperty("to", out var to)) yield break;

        if (to.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in to.EnumerateArray())
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s)) yield return s!;
            }
        }
        else if (to.ValueKind == JsonValueKind.String)
        {
            var s = to.GetString();
            if (!string.IsNullOrWhiteSpace(s)) yield return s!;
        }
    }

    /// <summary>
    /// Manual Svix verification per https://docs.svix.com/receiving/verifying-payloads/how-manual.
    /// Steps:
    ///   1. Reject if timestamp is outside our tolerance window (replay protection).
    ///   2. Compute HMAC-SHA256 over "{svixId}.{svixTimestamp}.{body}" with the base64-decoded
    ///      signing secret.
    ///   3. Compare against each space-delimited signature in the header (after stripping the
    ///      version prefix like "v1,"). Constant-time compare to avoid timing attacks.
    /// </summary>
    private bool VerifySignature(string svixId, string svixTimestamp, string body, string signatureHeader)
    {
        var secret = _config["Email:ResendWebhookSecret"];
        if (string.IsNullOrEmpty(secret))
        {
            _logger.LogError("Resend webhook secret is not configured (Email:ResendWebhookSecret)");
            return false;
        }

        // Replay protection: reject anything more than 5 min off our clock.
        if (!long.TryParse(svixTimestamp, out var unix))
        {
            _logger.LogWarning("Resend webhook timestamp is not numeric: {Timestamp}", svixTimestamp);
            return false;
        }
        var msgTime = DateTimeOffset.FromUnixTimeSeconds(unix);
        var drift = (DateTimeOffset.UtcNow - msgTime).Duration();
        if (drift > TimestampTolerance)
        {
            _logger.LogWarning("Resend webhook timestamp outside tolerance: drift={Drift}", drift);
            return false;
        }

        // Secret format is "whsec_<base64-encoded-key>" — strip prefix, decode.
        if (!secret.StartsWith("whsec_", StringComparison.Ordinal))
        {
            _logger.LogError("Resend webhook secret must start with 'whsec_'");
            return false;
        }
        byte[] keyBytes;
        try { keyBytes = Convert.FromBase64String(secret["whsec_".Length..]); }
        catch (FormatException)
        {
            _logger.LogError("Resend webhook secret is not valid base64 after 'whsec_' prefix");
            return false;
        }

        var signedContent = $"{svixId}.{svixTimestamp}.{body}";
        byte[] expected;
        using (var hmac = new HMACSHA256(keyBytes))
            expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedContent));

        // Header looks like "v1,<base64sig> v1,<base64sig2>". A v2 prefix is reserved for
        // future schemes — we only honor v1 today.
        foreach (var entry in signatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var commaIdx = entry.IndexOf(',');
            if (commaIdx < 0) continue;
            var version = entry[..commaIdx];
            if (version != "v1") continue;
            var sigBase64 = entry[(commaIdx + 1)..];

            byte[] candidate;
            try { candidate = Convert.FromBase64String(sigBase64); }
            catch (FormatException) { continue; }

            if (CryptographicOperations.FixedTimeEquals(expected, candidate))
                return true;
        }

        return false;
    }
}

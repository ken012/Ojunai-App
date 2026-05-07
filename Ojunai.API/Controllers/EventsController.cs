using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ojunai.API.Data;
using Ojunai.API.Models;
using System.Text.Json;

namespace Ojunai.API.Controllers;

/// <summary>
/// Anonymous client telemetry sink. The dashboard's PWA install funnel posts
/// events here via navigator.sendBeacon. Endpoint is intentionally minimal:
/// validate-name → store → 204. Telemetry must never break the user flow, so
/// errors are logged and swallowed.
/// </summary>
[Route("api/events")]
[ApiController]
[AllowAnonymous]
public class EventsController : ControllerBase
{
    private static readonly HashSet<string> AllowedEvents = new()
    {
        "pwa_banner_shown",
        "pwa_banner_clicked",
        "pwa_install_accepted",
        "pwa_install_dismissed",
        "pwa_launch_standalone",
    };

    private const int MaxPayloadChars = 4000;

    private readonly AppDbContext _db;
    private readonly ILogger<EventsController> _logger;

    public EventsController(AppDbContext db, ILogger<EventsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Record([FromBody] JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) return NoContent();

        // Whitelist event names — silently drop anything we don't recognize so a
        // typo or rogue caller can't fill the table with junk.
        if (!body.TryGetProperty("name", out var nameElem) || nameElem.ValueKind != JsonValueKind.String)
            return NoContent();
        var name = nameElem.GetString();
        if (string.IsNullOrWhiteSpace(name) || !AllowedEvents.Contains(name))
            return NoContent();

        var payload = body.GetRawText();
        if (payload.Length > MaxPayloadChars) payload = payload.Substring(0, MaxPayloadChars);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString()
            ?? HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? "unknown";
        if (ip.Length > 50) ip = ip.Substring(0, 50);

        var ua = HttpContext.Request.Headers["User-Agent"].ToString();
        if (ua.Length > 500) ua = ua.Substring(0, 500);

        try
        {
            _db.MobileEvents.Add(new MobileEvent
            {
                Name = name,
                Payload = payload,
                IpAddress = ip,
                UserAgent = string.IsNullOrEmpty(ua) ? null : ua,
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist mobile event {Name}", name);
        }

        return NoContent();
    }
}

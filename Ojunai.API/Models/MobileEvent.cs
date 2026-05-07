namespace Ojunai.API.Models;

/// <summary>
/// Lightweight client-side telemetry: PWA install funnel events posted from the
/// dashboard via navigator.sendBeacon. Anonymous, no PII attached beyond IP and
/// user-agent. Used to measure install rate and standalone launch share.
/// </summary>
public class MobileEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Payload { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

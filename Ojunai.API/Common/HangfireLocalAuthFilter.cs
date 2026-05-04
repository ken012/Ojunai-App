using Hangfire.Dashboard;

namespace Ojunai.API.Common;

/// <summary>
/// Gates the Hangfire dashboard (/hangfire) so only local/trusted requests can access background job internals.
/// Hangfire dashboard exposes sensitive info (job failures, stack traces, queued items), so locking it down is essential.
///
/// Access allowed only when:
///   1. The request comes from loopback (localhost) — typical for on-VPS inspection via SSH tunnel
///   2. Remote and local IPs match (unlikely but defensive)
///
/// Null RemoteIpAddress is rejected (unlike the previous version that allowed it) to prevent accidental exposure
/// behind misconfigured proxies that strip connection info.
/// </summary>
public class HangfireLocalAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var connection = httpContext.Connection;

        // Reject if we can't verify the source — better to lock out than to accidentally expose.
        if (connection.RemoteIpAddress == null) return false;

        // Allow loopback (same machine) — for SSH tunnel access from the VPS.
        if (System.Net.IPAddress.IsLoopback(connection.RemoteIpAddress)) return true;

        // Allow same-host traffic (remote and local IPs match).
        if (connection.LocalIpAddress != null && connection.RemoteIpAddress.Equals(connection.LocalIpAddress))
            return true;

        return false;
    }
}

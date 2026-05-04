using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Collections.Concurrent;

namespace Ojunai.API.Common;

/// <summary>
/// Per-IP rate limit for authentication endpoints (login, register, password reset).
/// Blocks an IP after MaxAttempts in a rolling Window and returns HTTP 429.
/// Entries are periodically cleaned up to prevent unbounded memory growth.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class AuthRateLimitAttribute : Attribute, IAsyncActionFilter
{
    private static readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _attempts = new();
    private static DateTime _lastCleanup = DateTime.UtcNow;
    private static readonly object _cleanupLock = new();

    private const int MaxAttempts = 10;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(10);

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Extract client IP, falling back to X-Forwarded-For (for requests behind Nginx)
        var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString()
            ?? context.HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? "unknown";

        var now = DateTime.UtcNow;

        // Periodic cleanup of stale entries to prevent memory growth under bot attacks
        if (now - _lastCleanup > CleanupInterval)
        {
            lock (_cleanupLock)
            {
                if (now - _lastCleanup > CleanupInterval)
                {
                    var staleKeys = _attempts
                        .Where(kv => now - kv.Value.WindowStart > Window.Add(TimeSpan.FromMinutes(5)))
                        .Select(kv => kv.Key)
                        .ToList();
                    foreach (var key in staleKeys) _attempts.TryRemove(key, out _);
                    _lastCleanup = now;
                }
            }
        }

        var entry = _attempts.GetOrAdd(ip, _ => (0, now));

        // Reset window if it has expired for this IP
        if (now - entry.WindowStart > Window)
            entry = (0, now);

        entry.Count++;
        _attempts[ip] = entry;

        if (entry.Count > MaxAttempts)
        {
            context.Result = new ObjectResult(ApiResponse<object>.Fail("Too many attempts. Please try again in a few minutes."))
            {
                StatusCode = 429
            };
            return;
        }

        await next();
    }
}

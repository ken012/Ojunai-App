using System.Security.Claims;
using System.Text.RegularExpressions;

namespace Ojunai.API.Common;

/// <summary>
/// Opens a logging scope for every request — a correlation <c>RequestId</c> plus the authenticated
/// <c>BusinessId</c>/<c>UserId</c> when present — so a single inbound request can be followed across
/// every log line it produces (requires <c>Logging:Console:IncludeScopes</c>). At ~100x, journald
/// interleaves many concurrent webhooks and background jobs; without a shared id, reconstructing one
/// message's path (webhook → Claude → DB write → reply) is guesswork.
///
/// Honors an inbound <c>X-Request-Id</c> (e.g. set by nginx) so our logs correlate with the proxy;
/// otherwise falls back to the framework's <see cref="HttpContext.TraceIdentifier"/>. The id is echoed
/// back in the response header so a user reporting an issue can quote it.
///
/// Placed AFTER authentication so the claims are populated; it runs for anonymous requests too
/// (they just get a RequestId with no Business/User).
/// </summary>
public sealed class RequestContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestContextMiddleware> _logger;

    // Bound + sanitize an inbound id so a forged header can't inject newlines into logs or bloat them.
    private static readonly Regex SafeId = new(@"^[A-Za-z0-9._:\-]{1,64}$", RegexOptions.Compiled);

    public RequestContextMiddleware(RequestDelegate next, ILogger<RequestContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers["X-Request-Id"].ToString();
        var requestId = !string.IsNullOrEmpty(incoming) && SafeId.IsMatch(incoming)
            ? incoming
            : context.TraceIdentifier;

        // Set before the response starts so it's always present for the caller to quote.
        context.Response.Headers["X-Request-Id"] = requestId;

        var scope = new Dictionary<string, object>(3) { ["RequestId"] = requestId };
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var biz = context.User.FindFirst("businessId")?.Value;
            if (!string.IsNullOrEmpty(biz)) scope["BusinessId"] = biz;

            var uid = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? context.User.FindFirst("sub")?.Value;
            if (!string.IsNullOrEmpty(uid)) scope["UserId"] = uid;
        }

        using (_logger.BeginScope(scope))
        {
            await _next(context);
        }
    }
}

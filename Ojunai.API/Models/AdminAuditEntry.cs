namespace Ojunai.API.Models;

/// <summary>
/// One row per admin-endpoint hit. We never store the raw admin key — only a SHA-256 prefix
/// of it for correlation across multiple hits from the same operator. If someone gains read
/// access to this table they still can't reuse the prefix to authenticate (no rainbow-table
/// risk on a 32+ char random secret).
/// </summary>
public class AdminAuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>HTTP method + path, e.g. "GET /api/admin/metrics/overview".</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Caller IP address (X-Forwarded-For aware via standard HttpContext lookup).</summary>
    public string? Ip { get; set; }

    /// <summary>First 12 hex chars of SHA-256(adminKey). Stable per operator, not reversible.</summary>
    public string? KeyPrefix { get; set; }

    /// <summary>True if ValidateAdminKey returned null (success); false otherwise.</summary>
    public bool Success { get; set; }

    /// <summary>HTTP status code returned to the caller. Useful for spotting attack patterns
    /// (many 401s from one IP, etc.).</summary>
    public int StatusCode { get; set; }

    /// <summary>Query string sans the `key` parameter — kept so we can see what was queried
    /// without leaking the secret. Truncated to 500 chars.</summary>
    public string? QueryString { get; set; }
}

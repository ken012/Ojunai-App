using Ojunai.API.Models;

namespace Ojunai.API.Services.Interfaces;

public interface IAlertService
{
    /// <summary>
    /// Creates a new alert. If a non-null DedupeKey is provided and an unread row with the
    /// same key exists for this business in the last DedupeWindow, this is a no-op.
    /// </summary>
    Task<Alert?> CreateAsync(
        Guid businessId,
        Guid? userId,
        AlertType type,
        AlertSeverity severity,
        string title,
        string body,
        string? linkUrl = null,
        string? metadataJson = null,
        string? dedupeKey = null);

    /// <summary>
    /// Returns alerts visible to the requesting user — business-wide ones if they're
    /// Owner/Admin, plus their own user-scoped alerts. Newest first, capped by limit.
    /// </summary>
    Task<List<Alert>> ListAsync(Guid businessId, Guid userId, UserRole role, bool unreadOnly, int limit);

    Task<int> UnreadCountAsync(Guid businessId, Guid userId, UserRole role);

    Task MarkReadAsync(Guid businessId, Guid userId, UserRole role, Guid alertId);
    Task DismissAsync(Guid businessId, Guid userId, UserRole role, Guid alertId);
    Task MarkAllReadAsync(Guid businessId, Guid userId, UserRole role);

    // ── Convenience helpers used by handlers ──────────────────────────────────────

    /// <summary>
    /// Fires post-sale dashboard alerts: low stock, large sale, sales goal hit.
    /// Each respects its own business toggle and is dedup'd. Pass the sale's ID so the
    /// large-sale alert dedups per-sale instead of per-call (caller may invoke us
    /// multiple times for the same sale via different code paths).
    /// </summary>
    Task EmitPostSaleAlertsAsync(Guid businessId, decimal saleAmount, Guid? saleId = null);
}

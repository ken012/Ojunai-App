namespace Ojunai.API.Models;

/// <summary>
/// Append-only audit record of a user (or bot) action — create/update/delete across modules.
/// Distinct from the transactional activity feed (sales/expenses/stock/ledger): this captures
/// WHO did WHAT to WHICH entity, attributed + timestamped + channel. Surfaced in the activity
/// log under the "action" type. Never mutated after write.
/// </summary>
public class ActivityLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BusinessId { get; set; }

    /// <summary>The acting user, when known. Null for system/automated actions.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Actor name captured at write time — denormalized so the record stays truthful
    /// even if the user is later renamed or removed.</summary>
    public string ActorName { get; set; } = "System";

    /// <summary>Where the action came from: dashboard | whatsapp | telegram | messenger | system.</summary>
    public string ActorChannel { get; set; } = "system";

    /// <summary>Dotted action code, e.g. "product.deleted", "staff.added", "settings.updated".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Entity kind, e.g. "Product", "Contact", "Staff", "Business".</summary>
    public string EntityType { get; set; } = string.Empty;

    public Guid? EntityId { get; set; }

    /// <summary>Human label for the entity at write time, e.g. "Rice".</summary>
    public string? EntityName { get; set; }

    /// <summary>Human-readable one-liner, e.g. 'deleted product "Rice"' or 'price 5,000 → 5,500'.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Optional extra detail / before-after context.</summary>
    public string? Details { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

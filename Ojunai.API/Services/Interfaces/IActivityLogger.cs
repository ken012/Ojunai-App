namespace Ojunai.API.Services.Interfaces;

/// <summary>Who performed an action. UserId null = system/automated.</summary>
public readonly record struct ActivityActor(Guid? UserId, string Name, string Channel);

/// <summary>
/// Per-request/per-job override of the acting user. The dashboard path leaves this unset (the
/// logger reads the JWT), but bot webhooks and background jobs — which have no authenticated HTTP
/// user — call <see cref="Set"/> once after resolving the sender so every subsequent audit entry
/// in that scope is attributed to the right person + channel.
/// </summary>
public interface ICurrentActor
{
    ActivityActor? Override { get; }
    void Set(Guid? userId, string name, string channel);
}

/// <summary>
/// Records user/bot actions to the append-only <c>ActivityLogEntry</c> audit log. The entry is
/// STAGED on the current DbContext and committed atomically by the caller's next SaveChangesAsync
/// (so the audit row exists iff the action itself committed). Never throws — auditing must never
/// break the underlying action.
/// </summary>
public interface IActivityLogger
{
    /// <param name="actor">Omit to resolve from the current HTTP user (dashboard). Pass explicitly
    /// for bot/background callers that have no HTTP context.</param>
    Task LogAsync(
        Guid businessId,
        string action,
        string entityType,
        Guid? entityId,
        string? entityName,
        string summary,
        string? details = null,
        ActivityActor? actor = null);
}

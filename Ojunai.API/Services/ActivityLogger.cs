using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;

namespace Ojunai.API.Services;

/// <summary>Scoped holder for a bot/job-supplied actor override (see <see cref="ICurrentActor"/>).</summary>
public class CurrentActor : ICurrentActor
{
    public ActivityActor? Override { get; private set; }
    public void Set(Guid? userId, string name, string channel) => Override = new ActivityActor(userId, name, channel);
}

/// <inheritdoc />
public class ActivityLogger : IActivityLogger
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;
    private readonly ICurrentActor _current;
    private readonly ILogger<ActivityLogger> _logger;

    public ActivityLogger(AppDbContext db, IHttpContextAccessor http, ICurrentActor current, ILogger<ActivityLogger> logger)
    {
        _db = db;
        _http = http;
        _current = current;
        _logger = logger;
    }

    public async Task LogAsync(
        Guid businessId, string action, string entityType, Guid? entityId, string? entityName,
        string summary, string? details = null, ActivityActor? actor = null)
    {
        try
        {
            // Precedence: explicit arg → bot/job override → HTTP JWT → System.
            var a = actor ?? _current.Override ?? await ResolveHttpActorAsync();
            _db.ActivityLogEntries.Add(new ActivityLogEntry
            {
                BusinessId = businessId,
                UserId = a.UserId,
                ActorName = string.IsNullOrWhiteSpace(a.Name) ? "System" : Trunc(a.Name, 200),
                ActorChannel = string.IsNullOrWhiteSpace(a.Channel) ? "system" : a.Channel,
                Action = Trunc(action, 60),
                EntityType = Trunc(entityType, 40),
                EntityId = entityId,
                EntityName = entityName == null ? null : Trunc(entityName, 300),
                Summary = Trunc(summary, 500),
                Details = details,
                CreatedAtUtc = DateTime.UtcNow,
            });
            // NOTE: not saved here — the caller's SaveChangesAsync commits it atomically with the action.
        }
        catch (Exception ex)
        {
            // Best-effort — an audit failure must never break the underlying action.
            _logger.LogWarning(ex, "Failed to stage activity log entry ({Action} {EntityType})", action, entityType);
        }
    }

    /// <summary>Resolves the acting user from the current HTTP request's JWT, caching the display
    /// name per-request. Falls back to System when there's no authenticated HTTP context.</summary>
    private async Task<ActivityActor> ResolveHttpActorAsync()
    {
        var ctx = _http.HttpContext;
        if (ctx?.User?.Identity?.IsAuthenticated != true)
            return new ActivityActor(null, "System", "system");

        Guid uid;
        try { uid = ctx.User.GetUserId(); } catch { uid = Guid.Empty; }
        if (uid == Guid.Empty) return new ActivityActor(null, "System", "system");

        var name = ctx.Items["__oj_actor_name"] as string;
        if (name == null)
        {
            name = await _db.Users.AsNoTracking()
                .Where(u => u.Id == uid).Select(u => u.FullName).FirstOrDefaultAsync() ?? "User";
            ctx.Items["__oj_actor_name"] = name;
        }
        return new ActivityActor(uid, name, "dashboard");
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];
}

using Ojunai.API.Data;
using Ojunai.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Services;

/// <summary>
/// User→location access control (multi-location). Two related jobs:
///  1. <see cref="AccessibleLocationIdsAsync"/> — which locations a user may see/act on. Owners &amp; Admins
///     always get ALL active locations. Everyone else is a "restricted" role: with one or more
///     <see cref="UserLocation"/> assignments they get exactly those (active) locations; with NO assignments
///     they get the business's DEFAULT location only (the owner-chosen policy — a cashier can't wander the
///     whole company until they're explicitly assigned).
///  2. <see cref="ResolveEffectiveLocationAsync"/> — the ambient LocationScope for a request. Owners/Admins
///     pass through the requested X-Location-Id verbatim (null = "All locations"). Restricted users are
///     ALWAYS pinned to one of their accessible locations — a null or out-of-set request resolves to their
///     primary (default if accessible, else the first). This is what makes "restricted" leak-proof without
///     rewriting every business-wide query: their scope is never business-wide at a multi-location business.
///
/// COST: single-location businesses (the only kind today) and Owner/Admin callers pay ZERO or ONE tiny
/// indexed query — see the short-circuits below. Enforcement only bites for restricted roles at businesses
/// that actually have more than one active location.
/// </summary>
public class LocationAccessService
{
    private readonly AppDbContext _db;

    public LocationAccessService(AppDbContext db) => _db = db;

    private static bool IsAllAccess(UserRole role) => role is UserRole.Owner or UserRole.Admin;

    /// <summary>The active location ids this user may access, default-first. Never empty for a business that
    /// has at least one active location.</summary>
    public async Task<List<Guid>> AccessibleLocationIdsAsync(Guid businessId, Guid userId, UserRole role)
    {
        var activeLocs = await _db.Locations
            .Where(l => l.BusinessId == businessId && l.IsActive)
            .OrderByDescending(l => l.IsDefault) // default first
            .Select(l => new { l.Id, l.IsDefault })
            .ToListAsync();

        if (IsAllAccess(role) || activeLocs.Count <= 1)
            return activeLocs.Select(l => l.Id).ToList();

        var assigned = await _db.UserLocations
            .Where(ul => ul.UserId == userId)
            .Select(ul => ul.LocationId)
            .ToListAsync();

        var assignedActive = activeLocs.Where(l => assigned.Contains(l.Id)).Select(l => l.Id).ToList();
        if (assignedActive.Count > 0) return assignedActive;

        // No assignments → default location only (owner policy). activeLocs is default-first, so [0] is it.
        return new List<Guid> { activeLocs[0].Id };
    }

    /// <summary>The effective ambient location for a request given the caller's role and the requested
    /// X-Location-Id. Owner/Admin ⇒ the request as-is (null allowed = business-wide). At a SINGLE-location
    /// business ⇒ the request as-is too (there is nothing to leak, and the >1-active gate ignores the value —
    /// so single-location businesses stay byte-for-byte unchanged). Only a restricted role at a genuinely
    /// MULTI-location business is PINNED: to the requested location if it's in their accessible set, else to
    /// their primary (default-first). A restricted user is therefore never business-wide when it matters —
    /// including one assigned to exactly ONE of several branches, which the naive "accessible.Count <= 1"
    /// check would have wrongly let fall through to business-wide.</summary>
    public async Task<Guid?> ResolveEffectiveLocationAsync(Guid businessId, Guid userId, UserRole role, Guid? requested)
    {
        if (IsAllAccess(role)) return requested; // unrestricted; no query

        var activeLocs = await _db.Locations
            .Where(l => l.BusinessId == businessId && l.IsActive)
            .OrderByDescending(l => l.IsDefault) // default first
            .Select(l => new { l.Id })
            .ToListAsync();

        if (activeLocs.Count <= 1) return requested; // single-location business → today's behavior exactly

        // Multi-location + restricted role → resolve the user's accessible set and PIN to it.
        var assigned = await _db.UserLocations
            .Where(ul => ul.UserId == userId)
            .Select(ul => ul.LocationId)
            .ToListAsync();

        var accessible = activeLocs.Where(l => assigned.Contains(l.Id)).Select(l => l.Id).ToList();
        if (accessible.Count == 0) accessible.Add(activeLocs[0].Id); // no assignments → default only (default-first)

        if (requested is { } r && accessible.Contains(r)) return r;
        return accessible[0];
    }
}

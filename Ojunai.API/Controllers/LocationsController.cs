using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Business;
using Ojunai.API.Models;
using Ojunai.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ojunai.API.Controllers;

/// <summary>
/// Location management (multi-location Phase 2). Every business has exactly one default "Main" location
/// (created by the backfill / on registration). Creating additional locations requires the multi-location
/// entitlement (Scale+ or the add-on) and stays within the plan's quota. Per-location STOCK reads/writes
/// are wired in Phase 2b; this controller is the management layer. See docs/multi-location-spec.md.
/// </summary>
[Route("api/business/locations")]
public class LocationsController : OjunaiBaseController
{
    private readonly AppDbContext _db;
    private readonly PlanGuard _planGuard;
    private readonly IActivityLogger _activity;
    private readonly Services.LocationAccessService _access;

    public LocationsController(AppDbContext db, PlanGuard planGuard, IActivityLogger activity, Services.LocationAccessService access)
    {
        _db = db;
        _planGuard = planGuard;
        _activity = activity;
        _access = access;
    }

    /// <summary>The business's locations (default first). Owner/Admin see all (incl. inactive, for management);
    /// restricted staff see only the locations they're allowed to access, so branch names aren't disclosed.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<LocationDto>>>> List()
    {
        var locations = await _db.Locations
            .Where(l => l.BusinessId == BusinessId)
            .OrderByDescending(l => l.IsDefault).ThenBy(l => l.CreatedAtUtc)
            .Select(l => new LocationDto { Id = l.Id, Name = l.Name, Type = l.Type, IsDefault = l.IsDefault, IsActive = l.IsActive })
            .ToListAsync();

        if (User.GetRole() is not (UserRole.Owner or UserRole.Admin))
        {
            var accessible = (await _access.AccessibleLocationIdsAsync(BusinessId, UserId, User.GetRole())).ToHashSet();
            locations = locations.Where(l => accessible.Contains(l.Id)).ToList();
        }
        return Ok(ApiResponse<List<LocationDto>>.Ok(locations));
    }

    /// <summary>Create an additional location. Gated: requires the multi-location entitlement + quota
    /// headroom (the default location always exists, so this is always a 2nd+ location).</summary>
    [HttpPost]
    [RequirePermission(Permission.ManageSettings)]
    public async Task<ActionResult<ApiResponse<LocationDto>>> Create([FromBody] CreateLocationRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return BadRequest(ApiResponse<LocationDto>.Fail("Location name is required."));
        if (name.Length > 200)
            return BadRequest(ApiResponse<LocationDto>.Fail("Location name is too long (max 200 characters)."));
        var type = string.Equals(request.Type, "warehouse", StringComparison.OrdinalIgnoreCase) ? "warehouse" : "branch";

        if (!await _planGuard.CanUseMultiLocationAsync(BusinessId))
            return BadRequest(ApiResponse<LocationDto>.Fail(
                "Multiple locations are available on the Scale plan or with the Multi-location add-on. Upgrade at app.ojunai.com/settings"));

        // Soft cap (business limit, not a security boundary). This check-then-insert can, under simultaneous
        // POSTs, overshoot by the concurrency count — acceptable for a soft quota; the entitlement gate above
        // is the real boundary and isn't racy. The default location is included in the count.
        var activeCount = await _db.Locations.CountAsync(l => l.BusinessId == BusinessId && l.IsActive);
        var quota = await _planGuard.GetLocationQuotaAsync(BusinessId);
        if (activeCount >= quota)
            return BadRequest(ApiResponse<LocationDto>.Fail($"You've reached your plan's limit of {quota} active locations."));

        var loc = new Location
        {
            BusinessId = BusinessId,
            Name = name,
            Type = type,
            IsDefault = false,
            IsActive = true,
        };
        _db.Locations.Add(loc);
        await _activity.LogAsync(BusinessId, "location.created", "Location", loc.Id, name, $"created location '{name}'");
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<LocationDto>.Ok(
            new LocationDto { Id = loc.Id, Name = loc.Name, Type = loc.Type, IsDefault = loc.IsDefault, IsActive = loc.IsActive },
            "Location created."));
    }

    /// <summary>Rename or (de)activate a location. The default location can't be deactivated.</summary>
    [HttpPatch("{id:guid}")]
    [RequirePermission(Permission.ManageSettings)]
    public async Task<ActionResult<ApiResponse<LocationDto>>> Update(Guid id, [FromBody] UpdateLocationRequest request)
    {
        var loc = await _db.Locations.FirstOrDefaultAsync(l => l.Id == id && l.BusinessId == BusinessId);
        if (loc == null) return NotFound(ApiResponse<LocationDto>.Fail("Location not found."));

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var name = request.Name.Trim();
            loc.Name = name.Length > 200 ? name[..200] : name;
        }
        if (request.IsActive.HasValue)
        {
            if (loc.IsDefault && !request.IsActive.Value)
                return BadRequest(ApiResponse<LocationDto>.Fail("The default location can't be deactivated."));
            // Don't strand stock: a location still holding stock would keep that stock in the business-wide
            // roll-up while making it unsellable (you can't select a deactivated location). Require it to be
            // transferred to another branch first.
            if (!request.IsActive.Value && loc.IsActive)
            {
                var withStock = await _db.ProductLocationStocks
                    .CountAsync(x => x.LocationId == loc.Id && x.CurrentStock > 0);
                if (withStock > 0)
                    return BadRequest(ApiResponse<LocationDto>.Fail(
                        $"{loc.Name} still holds stock in {withStock} product{(withStock == 1 ? "" : "s")}. Transfer it to another branch first, then deactivate."));
            }
            // Reactivating a non-default location is subject to the SAME gate + quota as creating one —
            // otherwise a downgraded business could deactivate then reactivate to exceed its limit.
            if (request.IsActive.Value && !loc.IsActive && !loc.IsDefault)
            {
                if (!await _planGuard.CanUseMultiLocationAsync(BusinessId))
                    return BadRequest(ApiResponse<LocationDto>.Fail(
                        "Multiple locations are available on the Scale plan or with the Multi-location add-on."));
                var activeCount = await _db.Locations.CountAsync(l => l.BusinessId == BusinessId && l.IsActive);
                var quota = await _planGuard.GetLocationQuotaAsync(BusinessId);
                if (activeCount >= quota)
                    return BadRequest(ApiResponse<LocationDto>.Fail($"You've reached your plan's limit of {quota} active locations."));
            }
            loc.IsActive = request.IsActive.Value;
        }

        await _activity.LogAsync(BusinessId, "location.updated", "Location", loc.Id, loc.Name, $"updated location '{loc.Name}'");
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<LocationDto>.Ok(
            new LocationDto { Id = loc.Id, Name = loc.Name, Type = loc.Type, IsDefault = loc.IsDefault, IsActive = loc.IsActive },
            "Location updated."));
    }
}

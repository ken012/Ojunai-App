using Microsoft.EntityFrameworkCore;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Services;
using Xunit;

namespace Ojunai.Tests.Security;

/// <summary>
/// User→location access control (LocationAccessService). Owners/Admins are all-access; restricted roles
/// (Sales/Bookkeeper/Viewer) are limited to their assignments (or the DEFAULT location only if unassigned)
/// and are PINNED — never business-wide — at a multi-location business. Single-location businesses are
/// unaffected (resolver returns the raw request, which the >1-active gate ignores). EF InMemory.
/// </summary>
public class MultiLocationAccessTests
{
    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("mlaccess-" + Guid.NewGuid()).Options);

    private static LocationAccessService Access(AppDbContext db) => new(db);

    private static async Task<Business> AddBizAsync(AppDbContext db)
    {
        var biz = new Business { Name = "T", AccountNumber = "A" + Guid.NewGuid().ToString("N")[..9] };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync(); // mirror seeds the default "Main" location
        return biz;
    }

    private static async Task<User> AddUserAsync(AppDbContext db, Guid businessId, UserRole role)
    {
        var u = new User { BusinessId = businessId, FullName = "U", PhoneNumber = "p" + Guid.NewGuid().ToString("N")[..12], Role = role };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    private static async Task<Location> AddLocationAsync(AppDbContext db, Guid businessId, string name, bool active = true)
    {
        var l = new Location { BusinessId = businessId, Name = name, IsActive = active };
        db.Locations.Add(l);
        await db.SaveChangesAsync();
        return l;
    }

    private static async Task AssignAsync(AppDbContext db, Guid userId, params Guid[] locationIds)
    {
        foreach (var id in locationIds) db.UserLocations.Add(new UserLocation { UserId = userId, LocationId = id });
        await db.SaveChangesAsync();
    }

    // ── AccessibleLocationIds ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Owner_And_Admin_SeeAllActiveLocations()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        var b = await AddLocationAsync(db, biz.Id, "B");
        var owner = await AddUserAsync(db, biz.Id, UserRole.Owner);
        var admin = await AddUserAsync(db, biz.Id, UserRole.Admin);

        var expected = new[] { biz.DefaultLocationId!.Value, b.Id };
        Assert.Equal(expected.OrderBy(x => x), (await Access(db).AccessibleLocationIdsAsync(biz.Id, owner.Id, UserRole.Owner)).OrderBy(x => x));
        Assert.Equal(expected.OrderBy(x => x), (await Access(db).AccessibleLocationIdsAsync(biz.Id, admin.Id, UserRole.Admin)).OrderBy(x => x));
    }

    [Fact]
    public async Task RestrictedUnassigned_MultiLocation_GetsDefaultOnly()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        await AddLocationAsync(db, biz.Id, "B");
        var sales = await AddUserAsync(db, biz.Id, UserRole.Sales);

        var acc = await Access(db).AccessibleLocationIdsAsync(biz.Id, sales.Id, UserRole.Sales);
        Assert.Equal(new[] { biz.DefaultLocationId!.Value }, acc);
    }

    [Fact]
    public async Task RestrictedAssigned_GetsExactlyTheirActiveAssignments()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        var b = await AddLocationAsync(db, biz.Id, "B");
        var c = await AddLocationAsync(db, biz.Id, "C");
        var inactive = await AddLocationAsync(db, biz.Id, "D", active: false);
        var sales = await AddUserAsync(db, biz.Id, UserRole.Sales);
        await AssignAsync(db, sales.Id, b.Id, inactive.Id); // assigned to B + an INACTIVE one

        var acc = await Access(db).AccessibleLocationIdsAsync(biz.Id, sales.Id, UserRole.Sales);
        Assert.Equal(new[] { b.Id }, acc); // only the ACTIVE assignment; inactive dropped; C not assigned
    }

    // ── ResolveEffectiveLocation (the ambient scope + leak-proofing) ─────────────────────────────────────
    [Fact]
    public async Task Resolve_Owner_PassesThroughRequest_IncludingNullAll()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        var b = await AddLocationAsync(db, biz.Id, "B");
        var owner = await AddUserAsync(db, biz.Id, UserRole.Owner);

        Assert.Null(await Access(db).ResolveEffectiveLocationAsync(biz.Id, owner.Id, UserRole.Owner, null)); // "All"
        Assert.Equal(b.Id, await Access(db).ResolveEffectiveLocationAsync(biz.Id, owner.Id, UserRole.Owner, b.Id));
    }

    [Fact]
    public async Task Resolve_SingleLocationBusiness_ReturnsRequestUnchanged()
    {
        // Byte-for-byte: at a single-location business the resolver must not invent a scope.
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        var sales = await AddUserAsync(db, biz.Id, UserRole.Sales);

        Assert.Null(await Access(db).ResolveEffectiveLocationAsync(biz.Id, sales.Id, UserRole.Sales, null));
    }

    [Fact]
    public async Task Resolve_RestrictedMultiLocation_HonoursInSetRequest_ElsePinsToPrimary()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        var b = await AddLocationAsync(db, biz.Id, "B");
        var c = await AddLocationAsync(db, biz.Id, "C");
        var sales = await AddUserAsync(db, biz.Id, UserRole.Sales);
        await AssignAsync(db, sales.Id, b.Id, c.Id);

        // In-set request honoured.
        Assert.Equal(b.Id, await Access(db).ResolveEffectiveLocationAsync(biz.Id, sales.Id, UserRole.Sales, b.Id));
        // Null ("All") → pinned to their primary (an assigned location, never business-wide).
        var pinnedForNull = await Access(db).ResolveEffectiveLocationAsync(biz.Id, sales.Id, UserRole.Sales, null);
        Assert.Contains(pinnedForNull!.Value, new[] { b.Id, c.Id });
        // Foreign request (the default, which they are NOT assigned to) → pinned to an assigned location.
        var pinnedForForeign = await Access(db).ResolveEffectiveLocationAsync(biz.Id, sales.Id, UserRole.Sales, biz.DefaultLocationId!.Value);
        Assert.Contains(pinnedForForeign!.Value, new[] { b.Id, c.Id });
    }

    [Fact]
    public async Task Resolve_RestrictedAssignedToOneOfMany_AlwaysPinned_NeverBusinessWide()
    {
        // Regression for the "accessible.Count <= 1" trap: a staffer assigned to exactly ONE of several
        // branches must be pinned to it — a null/foreign request must NOT fall through to business-wide.
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        var b = await AddLocationAsync(db, biz.Id, "B");
        await AddLocationAsync(db, biz.Id, "C");
        var sales = await AddUserAsync(db, biz.Id, UserRole.Sales);
        await AssignAsync(db, sales.Id, b.Id); // exactly one of three

        Assert.Equal(b.Id, await Access(db).ResolveEffectiveLocationAsync(biz.Id, sales.Id, UserRole.Sales, null));
        Assert.Equal(b.Id, await Access(db).ResolveEffectiveLocationAsync(biz.Id, sales.Id, UserRole.Sales, biz.DefaultLocationId!.Value));
        Assert.Equal(b.Id, await Access(db).ResolveEffectiveLocationAsync(biz.Id, sales.Id, UserRole.Sales, b.Id));
    }

    [Fact]
    public async Task Resolve_RestrictedUnassignedMultiLocation_PinnedToDefault()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        await AddLocationAsync(db, biz.Id, "B");
        var sales = await AddUserAsync(db, biz.Id, UserRole.Sales);

        Assert.Equal(biz.DefaultLocationId!.Value,
            await Access(db).ResolveEffectiveLocationAsync(biz.Id, sales.Id, UserRole.Sales, null));
        // Even if they request a branch they aren't assigned to, they're pinned back to the default.
        var branchB = await db.Locations.FirstAsync(l => l.Name == "B");
        Assert.Equal(biz.DefaultLocationId!.Value,
            await Access(db).ResolveEffectiveLocationAsync(biz.Id, sales.Id, UserRole.Sales, branchB.Id));
    }
}

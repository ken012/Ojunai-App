using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Services;
using Ojunai.API.Services.Interfaces;
using Xunit;

namespace Ojunai.Tests.Security;

/// <summary>
/// Variants ARE products (a VariantGroup only groups them), so their per-location stock already lives in
/// ProductLocationStock. These tests lock that VariantGroupService OVERLAYS the selected branch's stock onto
/// the style's TotalStock / low-stock count / per-variant stock — and that a single-location business (or the
/// "All branches" view) is unchanged (business-wide CurrentStock). EF InMemory + the dual-write mirror.
/// </summary>
public class MultiLocationVariantReadTests
{
    private sealed class NoopActivityLogger : IActivityLogger
    {
        public Task LogAsync(Guid businessId, string action, string entityType, Guid? entityId,
            string? entityName, string summary, string? details = null, ActivityActor? actor = null)
            => Task.CompletedTask;
    }

    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("mlvariant-" + Guid.NewGuid())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static VariantGroupService NewService(AppDbContext db) =>
        new(db, new NoopActivityLogger(), new LocationStockService(db));

    private static async Task<Business> AddBizAsync(AppDbContext db)
    {
        var biz = new Business { Name = "T", AccountNumber = "A" + Guid.NewGuid().ToString("N")[..9] };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync();
        return biz;
    }

    // Builds a style whose variants are created while SINGLE-location (mirror seeds each default-location PLS =
    // its `def` stock), THEN adds Branch B and routes each variant's `b` stock there via the mirror.
    private static async Task<(Business biz, VariantGroup group, Location branchB, List<Product> variants)> SetupAsync(
        AppDbContext db, params (string name, decimal def, decimal b, decimal low)[] specs)
    {
        var biz = await AddBizAsync(db);
        var group = new VariantGroup { BusinessId = biz.Id, Name = "Tee", Axes = "[\"Size\"]" };
        db.VariantGroups.Add(group);
        await db.SaveChangesAsync();

        var variants = new List<Product>();
        foreach (var s in specs)
        {
            var p = new Product { BusinessId = biz.Id, Name = s.name, CurrentStock = s.def, IsActive = true,
                VariantGroupId = group.Id, LowStockThreshold = s.low };
            db.Products.Add(p);
            variants.Add(p);
        }
        await db.SaveChangesAsync(); // single-location → mirror seeds default PLS = def for each

        var branchB = new Location { BusinessId = biz.Id, Name = "B", IsActive = true };
        db.Locations.Add(branchB);
        await db.SaveChangesAsync(); // now multi-location

        try
        {
            LocationScope.Current = branchB.Id;
            for (var i = 0; i < variants.Count; i++)
                if (specs[i].b != 0) variants[i].CurrentStock += specs[i].b; // mirror routes the delta to Branch B
            await db.SaveChangesAsync();
        }
        finally { LocationScope.Current = null; }

        return (biz, group, branchB, variants);
    }

    [Fact]
    public async Task Get_BranchSelected_OverlaysBranchStock()
    {
        using var db = NewContext();
        var (biz, group, branchB, variants) = await SetupAsync(db,
            ("Tee — S", 10, 3, 0),   // 10 at default + 3 at B  → 13 total
            ("Tee — L", 5, 0, 0));   // 5 at default + 0 at B   → 5 total
        var small = variants[0]; var large = variants[1];

        try
        {
            LocationScope.Current = branchB.Id;
            var dto = await NewService(db).GetAsync(biz.Id, group.Id);
            Assert.Equal(3m, dto.Variants.Single(v => v.ProductId == small.Id).CurrentStock);
            Assert.Equal(0m, dto.Variants.Single(v => v.ProductId == large.Id).CurrentStock);
            Assert.Equal(3m, dto.TotalStock); // branch B total, not 18
        }
        finally { LocationScope.Current = null; }

        // No branch selected → business-wide roll-up.
        var wide = await NewService(db).GetAsync(biz.Id, group.Id);
        Assert.Equal(13m, wide.Variants.Single(v => v.ProductId == small.Id).CurrentStock);
        Assert.Equal(18m, wide.TotalStock);
    }

    [Fact]
    public async Task List_BranchSelected_TotalAndLowStockUseBranch()
    {
        using var db = NewContext();
        // Small: 10 at default + 1 at B, low threshold 2 → LOW at B (1<=2) but NOT business-wide (11>2).
        var (biz, _, branchB, _) = await SetupAsync(db, ("Tee — S", 10, 1, 2));

        try
        {
            LocationScope.Current = branchB.Id;
            var g = (await NewService(db).ListAsync(biz.Id)).Single();
            Assert.Equal(1m, g.TotalStock);
            Assert.Equal(1, g.LowStockCount);
        }
        finally { LocationScope.Current = null; }

        var wide = (await NewService(db).ListAsync(biz.Id)).Single();
        Assert.Equal(11m, wide.TotalStock);
        Assert.Equal(0, wide.LowStockCount);
    }

    [Fact]
    public async Task Get_SingleLocation_Unchanged()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        var group = new VariantGroup { BusinessId = biz.Id, Name = "Tee", Axes = "[\"Size\"]" };
        db.VariantGroups.Add(group);
        await db.SaveChangesAsync();
        var only = new Product { BusinessId = biz.Id, Name = "Tee — S", CurrentStock = 7, IsActive = true, VariantGroupId = group.Id };
        db.Products.Add(only);
        await db.SaveChangesAsync();

        // Only the auto default location exists → even a stale scope resolves to "no branch" (>1-active gate).
        try
        {
            LocationScope.Current = biz.DefaultLocationId!.Value;
            var dto = await NewService(db).GetAsync(biz.Id, group.Id);
            Assert.Equal(7m, dto.Variants.Single().CurrentStock);
            Assert.Equal(7m, dto.TotalStock);
        }
        finally { LocationScope.Current = null; }
    }
}

using Microsoft.EntityFrameworkCore;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Ojunai.API.Services;
using Ojunai.API.Services.Interfaces;
using Xunit;

namespace Ojunai.Tests.Security;

/// <summary>
/// Tests for the multi-location Phase 1 dual-write (AppDbContext.SaveChanges override): it seeds a default
/// "Main" location for every new business and best-effort-mirrors Product.CurrentStock into that location's
/// ProductLocationStock row. Product.CurrentStock stays authoritative; these tests assert the mirror keeps
/// the per-location rows in sync so the Phase 2 read-cutover is race-free. Uses the EF InMemory provider —
/// no external DB — which still exercises the override + ChangeTracker logic.
/// </summary>
public class MultiLocationDualWriteTests
{
    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("mloc-" + Guid.NewGuid())
            .Options);

    private static async Task<Business> AddBusinessAsync(AppDbContext db, string acct)
    {
        var biz = new Business { Name = "Test", AccountNumber = acct, NextReceiptNumber = 7, ReceiptPrefix = "TS" };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync();
        return biz;
    }

    [Fact]
    public async Task NewBusiness_GetsExactlyOneDefaultLocation()
    {
        using var db = NewContext();
        var biz = await AddBusinessAsync(db, "0000000001");

        var loc = Assert.Single(await db.Locations.Where(l => l.BusinessId == biz.Id).ToListAsync());
        Assert.True(loc.IsDefault);
        Assert.True(loc.IsActive);
        Assert.Equal("Main", loc.Name);
        Assert.Equal(7, loc.NextReceiptNumber);          // receipt counter seeded from the business
        Assert.Equal("TS", loc.ReceiptPrefix);
        Assert.Equal(loc.Id, biz.DefaultLocationId);     // business points at its default
    }

    [Fact]
    public async Task NewProduct_GetsLocationStockMirroringCurrentStock()
    {
        using var db = NewContext();
        var biz = await AddBusinessAsync(db, "0000000002");

        var p = new Product { BusinessId = biz.Id, Name = "Widget", CurrentStock = 50 };
        db.Products.Add(p);
        await db.SaveChangesAsync();

        var pls = Assert.Single(await db.ProductLocationStocks.Where(x => x.ProductId == p.Id).ToListAsync());
        Assert.Equal(biz.DefaultLocationId, pls.LocationId);
        Assert.Equal(biz.Id, pls.BusinessId);
        Assert.Equal(50, pls.CurrentStock);
    }

    [Fact]
    public async Task StockDecrement_MirrorsToLocationStock()
    {
        using var db = NewContext();
        var biz = await AddBusinessAsync(db, "0000000003");
        var p = new Product { BusinessId = biz.Id, Name = "W", CurrentStock = 50 };
        db.Products.Add(p);
        await db.SaveChangesAsync();

        p.CurrentStock -= 20; // simulate a sale
        await db.SaveChangesAsync();

        var pls = await db.ProductLocationStocks.SingleAsync(x => x.ProductId == p.Id);
        Assert.Equal(30, pls.CurrentStock);
    }

    [Fact]
    public async Task NonStockChange_LeavesLocationStockInSync_NoDuplicateRow()
    {
        using var db = NewContext();
        var biz = await AddBusinessAsync(db, "0000000004");
        var p = new Product { BusinessId = biz.Id, Name = "W", CurrentStock = 50 };
        db.Products.Add(p);
        await db.SaveChangesAsync();

        p.Name = "Renamed"; // no stock change
        await db.SaveChangesAsync();

        var rows = await db.ProductLocationStocks.Where(x => x.ProductId == p.Id).ToListAsync();
        Assert.Single(rows);            // no duplicate PLS row
        Assert.Equal(50, rows[0].CurrentStock);
    }

    [Fact]
    public async Task Invariant_LocationStock_TracksCurrentStock_AcrossManyMutations()
    {
        using var db = NewContext();
        var biz = await AddBusinessAsync(db, "0000000005");
        var p = new Product { BusinessId = biz.Id, Name = "W", CurrentStock = 100 };
        db.Products.Add(p);
        await db.SaveChangesAsync();

        p.CurrentStock += 25; await db.SaveChangesAsync(); // stock-in
        p.CurrentStock -= 40; await db.SaveChangesAsync(); // sale
        p.CurrentStock = 12;  await db.SaveChangesAsync(); // absolute adjustment

        var pls = await db.ProductLocationStocks.SingleAsync(x => x.ProductId == p.Id);
        Assert.Equal(12, pls.CurrentStock);
        Assert.Equal(p.CurrentStock, pls.CurrentStock); // the invariant Phase 2 relies on
    }

    // ── Phase 2b: once a business has >1 active location, stock changes route the DELTA to the request's
    // resolved location (X-Location-Id / LocationScope), leaving other locations untouched, and the
    // SUM(per-location) == Product.CurrentStock roll-up holds for relative ops. ─────────────────────────

    private static async Task<Location> AddLocationAsync(AppDbContext db, Guid businessId, string name)
    {
        var loc = new Location { BusinessId = businessId, Name = name, IsDefault = false, IsActive = true };
        db.Locations.Add(loc);
        await db.SaveChangesAsync();
        return loc;
    }

    [Fact]
    public async Task MultiLocation_StockChange_RoutesDeltaToResolvedLocation_RollupHolds()
    {
        using var db = NewContext();
        var biz = await AddBusinessAsync(db, "0000000010");
        var p = new Product { BusinessId = biz.Id, Name = "W", CurrentStock = 50 };
        db.Products.Add(p);
        await db.SaveChangesAsync();                 // single-location: PLS(default) = 50
        var defaultLoc = biz.DefaultLocationId!.Value;

        var branchB = await AddLocationAsync(db, biz.Id, "Branch B"); // now multi-location (2 active)

        try
        {
            LocationScope.Current = branchB.Id;      // stock-in +30 AT branch B
            p.CurrentStock += 30;                    // service sets the business-wide roll-up 50 -> 80
            await db.SaveChangesAsync();
        }
        finally { LocationScope.Current = null; }

        var plsB = await db.ProductLocationStocks.SingleAsync(x => x.ProductId == p.Id && x.LocationId == branchB.Id);
        var plsDefault = await db.ProductLocationStocks.SingleAsync(x => x.ProductId == p.Id && x.LocationId == defaultLoc);
        Assert.Equal(30, plsB.CurrentStock);         // the +30 delta landed at B
        Assert.Equal(50, plsDefault.CurrentStock);   // the default location is untouched
        Assert.Equal(p.CurrentStock, plsB.CurrentStock + plsDefault.CurrentStock); // roll-up: 80 == 30 + 50
    }

    [Fact]
    public async Task MultiLocation_NoLocationHeader_RoutesToDefault()
    {
        using var db = NewContext();
        var biz = await AddBusinessAsync(db, "0000000012");
        var p = new Product { BusinessId = biz.Id, Name = "W", CurrentStock = 40 };
        db.Products.Add(p);
        await db.SaveChangesAsync();
        var defaultLoc = biz.DefaultLocationId!.Value;
        await AddLocationAsync(db, biz.Id, "Branch B"); // multi-location, but no LocationScope set

        LocationScope.Current = null;                // "All locations" / no header → default
        p.CurrentStock -= 10;                        // 40 -> 30
        await db.SaveChangesAsync();

        var plsDefault = await db.ProductLocationStocks.SingleAsync(x => x.ProductId == p.Id && x.LocationId == defaultLoc);
        Assert.Equal(30, plsDefault.CurrentStock);   // delta landed on the default location
    }

    [Fact]
    public async Task MultiLocation_ForeignLocationId_FallsBackToDefault()
    {
        using var db = NewContext();
        var biz = await AddBusinessAsync(db, "0000000013");
        var p = new Product { BusinessId = biz.Id, Name = "W", CurrentStock = 40 };
        db.Products.Add(p);
        await db.SaveChangesAsync();
        var defaultLoc = biz.DefaultLocationId!.Value;
        await AddLocationAsync(db, biz.Id, "Branch B");

        try
        {
            LocationScope.Current = Guid.NewGuid();   // an id that is NOT one of this business's locations
            p.CurrentStock -= 5;                      // 40 -> 35
            await db.SaveChangesAsync();
        }
        finally { LocationScope.Current = null; }

        var plsDefault = await db.ProductLocationStocks.SingleAsync(x => x.ProductId == p.Id && x.LocationId == defaultLoc);
        Assert.Equal(35, plsDefault.CurrentStock);   // invalid/foreign id → default, not the foreign location
    }

    [Fact]
    public async Task MultiLocation_OversellAtLocation_ClampsToZero_DoesNotThrow()
    {
        using var db = NewContext();
        var biz = await AddBusinessAsync(db, "0000000014");
        var p = new Product { BusinessId = biz.Id, Name = "W", CurrentStock = 50 };
        db.Products.Add(p);
        await db.SaveChangesAsync();
        var branchB = await AddLocationAsync(db, biz.Id, "Branch B"); // B has 0 stock

        try
        {
            LocationScope.Current = branchB.Id;
            p.CurrentStock -= 10;                     // sell 10 "at B" which has 0 → delta -10
            await db.SaveChangesAsync();              // must NOT throw (clamped, no negative)
        }
        finally { LocationScope.Current = null; }

        var plsB = await db.ProductLocationStocks.FirstOrDefaultAsync(x => x.ProductId == p.Id && x.LocationId == branchB.Id);
        Assert.Equal(0m, plsB?.CurrentStock ?? 0m);  // clamped to 0, never negative
    }

    // ── Phase 2b: per-location READ overlay — the products list shows the selected location's stock. ──────

    private sealed class NoopActivityLogger : IActivityLogger
    {
        public Task LogAsync(Guid businessId, string action, string entityType, Guid? entityId,
            string? entityName, string summary, string? details = null, ActivityActor? actor = null)
            => Task.CompletedTask;
    }

    [Fact]
    public async Task PerLocationRead_Overlay_ShowsSelectedLocationStock_ElseBusinessWide()
    {
        using var db = NewContext();
        var svc = new ProductService(db, new NoopActivityLogger());
        var biz = await AddBusinessAsync(db, "0000000020");
        var p = new Product { BusinessId = biz.Id, Name = "Widget", CurrentStock = 50, IsActive = true };
        db.Products.Add(p);
        await db.SaveChangesAsync();
        var defaultLoc = biz.DefaultLocationId!.Value;
        var branchB = await AddLocationAsync(db, biz.Id, "Branch B");

        try { LocationScope.Current = branchB.Id; p.CurrentStock += 30; await db.SaveChangesAsync(); }
        finally { LocationScope.Current = null; }
        // Now: business-wide 80 = default 50 + B 30.

        async Task<decimal> StockView(Guid? loc)
        {
            try
            {
                LocationScope.Current = loc;
                var view = await svc.GetAllAsync(biz.Id, 1, 100, null);
                return view.Items.Single(i => i.Id == p.Id).CurrentStock;
            }
            finally { LocationScope.Current = null; }
        }

        Assert.Equal(80m, await StockView(null));            // "All locations" → business-wide roll-up
        Assert.Equal(30m, await StockView(branchB.Id));      // Branch B → B's stock
        Assert.Equal(50m, await StockView(defaultLoc));      // Default → default's stock
        Assert.Equal(80m, await StockView(Guid.NewGuid()));  // foreign id → business-wide (not 0)
    }
}

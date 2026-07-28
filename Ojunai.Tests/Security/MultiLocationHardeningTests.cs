using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Inventory;
using Ojunai.API.DTOs.Sales;
using Ojunai.API.Models;
using Ojunai.API.Services;
using Xunit;

namespace Ojunai.Tests.Security;

/// <summary>
/// Phase 2 hardening: the write services (InventoryService et al.) become per-location aware ONLY when a
/// specific location is selected. These tests assert single-location behaviour is unchanged AND multi-
/// location does absolute-set / availability against the selected location's stock. EF InMemory.
/// </summary>
public class MultiLocationHardeningTests
{
    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("mlhard-" + Guid.NewGuid())
            // InventoryService/StocktakeService now wrap their writes in a (Serializable) transaction via DbRetry;
            // the InMemory provider ignores transactions, so silence that benign warning.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static InventoryService Inv(AppDbContext db) => new(db, new LocationStockService(db));

    // SalesService/StocktakeService open a DB transaction; InMemory ignores transactions, so silence that
    // warning for tests that exercise them.
    private static AppDbContext NewTxContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("mlhardtx-" + Guid.NewGuid())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static async Task<Business> AddBizAsync(AppDbContext db)
    {
        var biz = new Business { Name = "T", AccountNumber = "A" + Guid.NewGuid().ToString("N")[..9] };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync();
        return biz;
    }

    // Multi-location fixture: default location has `defaultStock`, Branch B has `bStock` (put there via the
    // dual-write mirror), product total = defaultStock + bStock.
    private static async Task<(Business biz, Product p, Guid defaultLoc, Location branchB)> MultiAsync(
        AppDbContext db, decimal defaultStock, decimal bStock)
    {
        var biz = await AddBizAsync(db);
        var p = new Product { BusinessId = biz.Id, Name = "W", CurrentStock = defaultStock, IsActive = true };
        db.Products.Add(p);
        await db.SaveChangesAsync();
        var branchB = new Location { BusinessId = biz.Id, Name = "B", IsActive = true };
        db.Locations.Add(branchB);
        await db.SaveChangesAsync();
        if (bStock != 0)
        {
            try { LocationScope.Current = branchB.Id; p.CurrentStock += bStock; await db.SaveChangesAsync(); }
            finally { LocationScope.Current = null; }
        }
        return (biz, p, biz.DefaultLocationId!.Value, branchB);
    }

    [Fact]
    public async Task Adjust_SingleLocation_SetsProductTotal_Unchanged()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        var p = new Product { BusinessId = biz.Id, Name = "W", CurrentStock = 50, IsActive = true };
        db.Products.Add(p);
        await db.SaveChangesAsync();

        await Inv(db).AdjustAsync(biz.Id, new AdjustmentRequest { ProductId = p.Id, NewQuantity = 30 });

        Assert.Equal(30m, (await db.Products.FindAsync(p.Id))!.CurrentStock);
    }

    [Fact]
    public async Task Adjust_MultiLocation_SetsSelectedLocationStock_RollupHolds()
    {
        using var db = NewContext();
        var (biz, p, defaultLoc, branchB) = await MultiAsync(db, 50, 30); // default 50, B 30, total 80

        try { LocationScope.Current = branchB.Id; await Inv(db).AdjustAsync(biz.Id, new AdjustmentRequest { ProductId = p.Id, NewQuantity = 100 }); }
        finally { LocationScope.Current = null; }

        var plsB = await db.ProductLocationStocks.SingleAsync(x => x.ProductId == p.Id && x.LocationId == branchB.Id);
        var plsDef = await db.ProductLocationStocks.SingleAsync(x => x.ProductId == p.Id && x.LocationId == defaultLoc);
        Assert.Equal(100m, plsB.CurrentStock);   // B set to 100 (not a delta)
        Assert.Equal(50m, plsDef.CurrentStock);  // default untouched
        Assert.Equal(150m, (await db.Products.FindAsync(p.Id))!.CurrentStock); // roll-up 50 + 100
    }

    [Fact]
    public async Task StockOut_SingleLocation_ChecksBusinessWide_Unchanged()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        var p = new Product { BusinessId = biz.Id, Name = "W", CurrentStock = 50, IsActive = true };
        db.Products.Add(p);
        await db.SaveChangesAsync();

        await Inv(db).StockOutAsync(biz.Id, new StockOutRequest { ProductId = p.Id, Quantity = 20 }); // 20 <= 50
        Assert.Equal(30m, (await db.Products.FindAsync(p.Id))!.CurrentStock);
    }

    [Fact]
    public async Task StockOut_MultiLocation_OversellAtLocation_Throws_EvenIfBusinessTotalIsEnough()
    {
        using var db = NewContext();
        var (biz, p, _, branchB) = await MultiAsync(db, 50, 10); // B has 10, business total 60

        try
        {
            LocationScope.Current = branchB.Id;
            // 20 > B's 10 (though 20 <= business total 60) → must reject at the location.
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Inv(db).StockOutAsync(biz.Id, new StockOutRequest { ProductId = p.Id, Quantity = 20 }));
        }
        finally { LocationScope.Current = null; }
    }

    [Fact]
    public async Task StockOut_MultiLocation_WithinLocationStock_Succeeds_RoutesToLocation()
    {
        using var db = NewContext();
        var (biz, p, defaultLoc, branchB) = await MultiAsync(db, 50, 30); // B has 30, total 80

        try { LocationScope.Current = branchB.Id; await Inv(db).StockOutAsync(biz.Id, new StockOutRequest { ProductId = p.Id, Quantity = 12 }); }
        finally { LocationScope.Current = null; }

        var plsB = await db.ProductLocationStocks.SingleAsync(x => x.ProductId == p.Id && x.LocationId == branchB.Id);
        var plsDef = await db.ProductLocationStocks.SingleAsync(x => x.ProductId == p.Id && x.LocationId == defaultLoc);
        Assert.Equal(18m, plsB.CurrentStock);   // 30 - 12
        Assert.Equal(50m, plsDef.CurrentStock); // untouched
        Assert.Equal(68m, (await db.Products.FindAsync(p.Id))!.CurrentStock); // roll-up 80 - 12
    }

    [Fact]
    public async Task Adjust_OneActiveLocation_WithStaleHeader_BehavesSingleLocation_NoServiceMirrorDivergence()
    {
        // Regression for the service↔mirror gating divergence: a business that deactivated a branch back to
        // ONE active location, with a stale X-Location-Id still pointing at the (active) default. The service
        // must take the SAME single-location branch the mirror takes (activeCount == 1), so "set stock to 100"
        // sets the product total to 100 — NOT the corrupted 130 the mismatched gating produced.
        using var db = NewContext();
        var (biz, p, defaultLoc, branchB) = await MultiAsync(db, 50, 30); // default 50, B 30, total 80
        branchB.IsActive = false;
        await db.SaveChangesAsync(); // back to 1 active location

        try { LocationScope.Current = defaultLoc; await Inv(db).AdjustAsync(biz.Id, new AdjustmentRequest { ProductId = p.Id, NewQuantity = 100 }); }
        finally { LocationScope.Current = null; }

        var product = (await db.Products.FindAsync(p.Id))!;
        var plsDef = await db.ProductLocationStocks.SingleAsync(x => x.ProductId == p.Id && x.LocationId == defaultLoc);
        Assert.Equal(100m, product.CurrentStock); // single-location "set to 100" — not 130
        Assert.Equal(100m, plsDef.CurrentStock);  // active default PLS mirrors the total (service+mirror agreed)
    }

    [Fact]
    public async Task Sale_MultiLocation_OversellAtLocation_Throws_EvenIfBusinessTotalIsEnough()
    {
        using var db = NewTxContext();
        var (biz, p, _, branchB) = await MultiAsync(db, 50, 10); // B has 10, business total 60
        var sales = new SalesService(db, new LocationStockService(db));

        try
        {
            LocationScope.Current = branchB.Id;
            var req = new CreateSaleRequest { Items = { new SaleItemRequest { ProductId = p.Id, Quantity = 20, UnitPrice = 5 } } };
            await Assert.ThrowsAsync<InvalidOperationException>(() => sales.CreateAsync(biz.Id, req));
        }
        finally { LocationScope.Current = null; }
    }

    [Fact]
    public async Task Sale_MultiLocation_WithinLocationStock_Succeeds_RoutesToLocation()
    {
        using var db = NewTxContext();
        var (biz, p, defaultLoc, branchB) = await MultiAsync(db, 50, 30); // B has 30, total 80
        var sales = new SalesService(db, new LocationStockService(db));

        try
        {
            LocationScope.Current = branchB.Id;
            var req = new CreateSaleRequest { Items = { new SaleItemRequest { ProductId = p.Id, Quantity = 12, UnitPrice = 5 } } };
            await sales.CreateAsync(biz.Id, req);
        }
        finally { LocationScope.Current = null; }

        var plsB = await db.ProductLocationStocks.SingleAsync(x => x.ProductId == p.Id && x.LocationId == branchB.Id);
        var plsDef = await db.ProductLocationStocks.SingleAsync(x => x.ProductId == p.Id && x.LocationId == defaultLoc);
        Assert.Equal(18m, plsB.CurrentStock);   // 30 - 12 routed to B
        Assert.Equal(50m, plsDef.CurrentStock); // untouched
        Assert.Equal(68m, (await db.Products.FindAsync(p.Id))!.CurrentStock); // roll-up 80 - 12
    }
}

using Microsoft.EntityFrameworkCore;
using Ojunai.API.Data;
using Ojunai.API.Models;
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
}

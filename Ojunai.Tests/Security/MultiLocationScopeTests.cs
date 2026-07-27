using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Expenses;
using Ojunai.API.DTOs.Sales;
using Ojunai.API.Models;
using Ojunai.API.Services;
using Ojunai.API.Services.Interfaces;
using Xunit;

namespace Ojunai.Tests.Security;

/// <summary>
/// Per-location READ scoping + transaction ATTRIBUTION (the "fully functional" layer on top of the Phase 2
/// hardening): inventory low-stock/stats bucket by the selected location's stock; sales/expenses are stamped
/// with the selected LocationId on create and filtered by it on read. Every test also asserts the
/// single-location path is byte-for-byte unchanged (no selection ⇒ business-wide, null LocationId). EF InMemory.
/// </summary>
public class MultiLocationScopeTests
{
    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("mlscope-" + Guid.NewGuid()).Options);

    // Sales open a DB transaction; InMemory ignores transactions, so silence that warning.
    private static AppDbContext NewTxContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("mlscopetx-" + Guid.NewGuid())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class NoopActivityLogger : IActivityLogger
    {
        public Task LogAsync(Guid businessId, string action, string entityType, Guid? entityId,
            string? entityName, string summary, string? details = null, ActivityActor? actor = null)
            => Task.CompletedTask;
    }

    private static ProductService Products(AppDbContext db) => new(db, new NoopActivityLogger(), new LocationStockService(db));
    private static SalesService Sales(AppDbContext db) => new(db, new LocationStockService(db));
    private static ExpenseService Expenses(AppDbContext db) => new(db, new LocationStockService(db));

    private static async Task<Business> AddBizAsync(AppDbContext db)
    {
        var biz = new Business { Name = "T", AccountNumber = "A" + Guid.NewGuid().ToString("N")[..9] };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync();
        return biz;
    }

    // default location has `defaultStock`, Branch B has `bStock` (routed via the dual-write mirror).
    private static async Task<(Business biz, Product p, Guid defaultLoc, Location branchB)> MultiAsync(
        AppDbContext db, decimal defaultStock, decimal bStock, decimal threshold = 0)
    {
        var biz = await AddBizAsync(db);
        var p = new Product { BusinessId = biz.Id, Name = "W", CurrentStock = defaultStock, LowStockThreshold = threshold, IsActive = true };
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

    // ── Inventory: low-stock buckets by the SELECTED location's stock ────────────────────────────────────
    [Fact]
    public async Task LowStock_MultiLocation_JudgedByLocationStock_NotBusinessWide()
    {
        using var db = NewContext();
        // total 100, threshold 5; only 2 at Branch B → low at B, but NOT low business-wide (100 > 5).
        var (biz, p, defaultLoc, branchB) = await MultiAsync(db, 98, 2, threshold: 5);

        // Business-wide (no selection): 100 > 5 → not returned.
        Assert.Empty(await Products(db).GetLowStockAsync(biz.Id));

        // At Branch B: 2 <= 5 → returned, showing the location's 2.
        try { LocationScope.Current = branchB.Id; var low = await Products(db).GetLowStockAsync(biz.Id);
            var row = Assert.Single(low);
            Assert.Equal(p.Id, row.Id);
            Assert.Equal(2m, row.CurrentStock);
            Assert.True(row.IsLowStock);
        }
        finally { LocationScope.Current = null; }

        // At the default location: 98 > 5 → not low there.
        try { LocationScope.Current = defaultLoc; Assert.Empty(await Products(db).GetLowStockAsync(biz.Id)); }
        finally { LocationScope.Current = null; }
    }

    [Fact]
    public async Task LowStock_SingleLocation_Unchanged()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        db.Products.Add(new Product { BusinessId = biz.Id, Name = "Low", CurrentStock = 1, LowStockThreshold = 5, IsActive = true });
        db.Products.Add(new Product { BusinessId = biz.Id, Name = "Ok", CurrentStock = 50, LowStockThreshold = 5, IsActive = true });
        await db.SaveChangesAsync();

        var low = await Products(db).GetLowStockAsync(biz.Id);
        Assert.Equal("Low", Assert.Single(low).Name);
    }

    // ── Inventory: stock stats bucket by the SELECTED location's stock ───────────────────────────────────
    [Fact]
    public async Task StockStats_MultiLocation_BucketsByLocationStock()
    {
        using var db = NewContext();
        var (biz, p, _, branchB) = await MultiAsync(db, 98, 2, threshold: 5); // B has 2 (<=5 → Low), total 100

        var wide = await Products(db).GetStockStatsAsync(biz.Id, null, null);
        Assert.Equal(1, wide.Total);
        Assert.Equal(1, wide.Sufficient); // 100 > 5
        Assert.Equal(0, wide.Low);

        try { LocationScope.Current = branchB.Id; var loc = await Products(db).GetStockStatsAsync(biz.Id, null, null);
            Assert.Equal(1, loc.Total);
            Assert.Equal(1, loc.Low);        // 2 is within (0, 5]
            Assert.Equal(0, loc.Sufficient);
            Assert.Equal(0, loc.OutOfStock);
        }
        finally { LocationScope.Current = null; }
    }

    // ── Sales: stamp LocationId on create, filter on read ────────────────────────────────────────────────
    [Fact]
    public async Task Sale_MultiLocation_StampsLocationId_AndListFiltersByLocation()
    {
        using var db = NewTxContext();
        var (biz, p, defaultLoc, branchB) = await MultiAsync(db, 50, 30); // B has 30

        Guid saleId;
        try
        {
            LocationScope.Current = branchB.Id;
            var req = new CreateSaleRequest { Items = { new SaleItemRequest { ProductId = p.Id, Quantity = 5, UnitPrice = 10 } } };
            var sale = await Sales(db).CreateAsync(biz.Id, req);
            saleId = sale.Id;
        }
        finally { LocationScope.Current = null; }

        Assert.Equal(branchB.Id, (await db.Sales.FindAsync(saleId))!.LocationId);

        // Selecting Branch B lists it; selecting the default (no sales there) does not.
        try { LocationScope.Current = branchB.Id; var atB = await Sales(db).GetAllAsync(biz.Id, 1, 100, null, null); Assert.Contains(atB.Items, s => s.Id == saleId); }
        finally { LocationScope.Current = null; }
        try { LocationScope.Current = defaultLoc; var atDef = await Sales(db).GetAllAsync(biz.Id, 1, 100, null, null); Assert.DoesNotContain(atDef.Items, s => s.Id == saleId); }
        finally { LocationScope.Current = null; }
    }

    [Fact]
    public async Task Sale_SingleLocation_LocationIdNull_ListUnfiltered()
    {
        using var db = NewTxContext();
        var biz = await AddBizAsync(db);
        var p = new Product { BusinessId = biz.Id, Name = "W", CurrentStock = 50, IsActive = true };
        db.Products.Add(p);
        await db.SaveChangesAsync();

        var req = new CreateSaleRequest { Items = { new SaleItemRequest { ProductId = p.Id, Quantity = 5, UnitPrice = 10 } } };
        var sale = await Sales(db).CreateAsync(biz.Id, req);

        Assert.Null((await db.Sales.FindAsync(sale.Id))!.LocationId); // single-location ⇒ unstamped
        var list = await Sales(db).GetAllAsync(biz.Id, 1, 100, null, null);
        Assert.Contains(list.Items, s => s.Id == sale.Id);           // no selection ⇒ business-wide list
    }

    // ── Expenses: stamp LocationId on create, filter on read ─────────────────────────────────────────────
    [Fact]
    public async Task Expense_MultiLocation_StampsLocationId_AndListFiltersByLocation()
    {
        using var db = NewContext();
        var (biz, _, defaultLoc, branchB) = await MultiAsync(db, 10, 5);

        Guid expId;
        try
        {
            LocationScope.Current = branchB.Id;
            var exp = await Expenses(db).CreateAsync(biz.Id, new CreateExpenseRequest { Category = "Rent", Amount = 100 });
            expId = exp.Id;
        }
        finally { LocationScope.Current = null; }

        Assert.Equal(branchB.Id, (await db.Expenses.FindAsync(expId))!.LocationId);

        try { LocationScope.Current = branchB.Id; var atB = await Expenses(db).GetAllAsync(biz.Id, 1, 100, null, null); Assert.Contains(atB.Items, e => e.Id == expId); }
        finally { LocationScope.Current = null; }
        try { LocationScope.Current = defaultLoc; var atDef = await Expenses(db).GetAllAsync(biz.Id, 1, 100, null, null); Assert.DoesNotContain(atDef.Items, e => e.Id == expId); }
        finally { LocationScope.Current = null; }
    }

    [Fact]
    public async Task Expense_SingleLocation_LocationIdNull_ListUnfiltered()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        var exp = await Expenses(db).CreateAsync(biz.Id, new CreateExpenseRequest { Category = "Rent", Amount = 100 });

        Assert.Null((await db.Expenses.FindAsync(exp.Id))!.LocationId);
        var list = await Expenses(db).GetAllAsync(biz.Id, 1, 100, null, null);
        Assert.Contains(list.Items, e => e.Id == exp.Id);
    }
}

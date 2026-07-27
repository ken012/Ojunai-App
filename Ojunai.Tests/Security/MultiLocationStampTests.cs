using Microsoft.EntityFrameworkCore;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.Models;
using Xunit;

namespace Ojunai.Tests.Security;

/// <summary>
/// Central location-attribution stamp (AppDbContext.StampLocationScopedEntitiesAsync). ILocationScoped rows
/// being inserted get their LocationId set from the ambient scope — but ONLY at a multi-location business with
/// a valid selected location. Single-location, "All", and explicit stamps are left null / untouched. EF InMemory.
/// </summary>
public class MultiLocationStampTests
{
    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("mlstamp-" + Guid.NewGuid()).Options);

    private static async Task<Business> AddBizAsync(AppDbContext db)
    {
        var biz = new Business { Name = "T", AccountNumber = "A" + Guid.NewGuid().ToString("N")[..9] };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync(); // mirror seeds the default location
        return biz;
    }

    private static async Task<Location> AddActiveLocationAsync(AppDbContext db, Guid businessId, string name)
    {
        var l = new Location { BusinessId = businessId, Name = name, IsActive = true };
        db.Locations.Add(l);
        await db.SaveChangesAsync();
        return l;
    }

    private static InventoryTransaction NewTxn(Guid businessId) => new()
    {
        BusinessId = businessId, ProductId = Guid.NewGuid(), Type = InventoryTransactionType.StockOut, Quantity = 1,
    };

    [Fact]
    public async Task MultiLocation_WithSelectedLocation_StampsInsertedRow()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        var branchB = await AddActiveLocationAsync(db, biz.Id, "B"); // now 2 active locations

        var txn = NewTxn(biz.Id);
        db.InventoryTransactions.Add(txn);
        try { LocationScope.Current = branchB.Id; await db.SaveChangesAsync(); }
        finally { LocationScope.Current = null; }

        Assert.Equal(branchB.Id, (await db.InventoryTransactions.FindAsync(txn.Id))!.LocationId);
    }

    [Fact]
    public async Task SingleLocation_LeavesLocationIdNull_EvenWithScopeSet()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db); // one active location (default)

        var txn = NewTxn(biz.Id);
        db.InventoryTransactions.Add(txn);
        // Stale header pointing at the (only) default — must NOT stamp, since the business isn't multi-location.
        try { LocationScope.Current = biz.DefaultLocationId; await db.SaveChangesAsync(); }
        finally { LocationScope.Current = null; }

        Assert.Null((await db.InventoryTransactions.FindAsync(txn.Id))!.LocationId);
    }

    [Fact]
    public async Task MultiLocation_AllLocations_NoScope_LeavesLocationIdNull()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        await AddActiveLocationAsync(db, biz.Id, "B"); // multi-location

        var txn = NewTxn(biz.Id);
        db.InventoryTransactions.Add(txn);
        await db.SaveChangesAsync(); // scope null = "All locations"

        Assert.Null((await db.InventoryTransactions.FindAsync(txn.Id))!.LocationId);
    }

    [Fact]
    public async Task ForeignSelectedLocation_IsIgnored_LeavesNull()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        await AddActiveLocationAsync(db, biz.Id, "B"); // multi-location

        var txn = NewTxn(biz.Id);
        db.InventoryTransactions.Add(txn);
        try { LocationScope.Current = Guid.NewGuid(); await db.SaveChangesAsync(); } // not a location of this biz
        finally { LocationScope.Current = null; }

        Assert.Null((await db.InventoryTransactions.FindAsync(txn.Id))!.LocationId);
    }

    [Fact]
    public async Task ExplicitLocationId_IsPreserved_NotOverwritten()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        var branchB = await AddActiveLocationAsync(db, biz.Id, "B");
        var branchC = await AddActiveLocationAsync(db, biz.Id, "C");

        var txn = NewTxn(biz.Id);
        txn.LocationId = branchC.Id; // service stamped it explicitly
        db.InventoryTransactions.Add(txn);
        try { LocationScope.Current = branchB.Id; await db.SaveChangesAsync(); } // ambient differs
        finally { LocationScope.Current = null; }

        Assert.Equal(branchC.Id, (await db.InventoryTransactions.FindAsync(txn.Id))!.LocationId); // not clobbered
    }
}

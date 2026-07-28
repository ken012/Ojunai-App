using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ojunai.API.Common;
using Ojunai.API.Data;
using Ojunai.API.DTOs.Inventory;
using Ojunai.API.Models;
using Ojunai.API.Services;
using Ojunai.API.Services.Interfaces;
using Xunit;

namespace Ojunai.Tests.Security;

/// <summary>
/// Stock transfers between branches (multi-location Phase 3). A transfer moves stock from one location's
/// ProductLocationStock to another's and leaves Product.CurrentStock (the roll-up) UNCHANGED, so
/// SUM(per-location) == Product.CurrentStock holds. Gated on multi-location entitlement. EF InMemory.
/// </summary>
public class MultiLocationTransferTests
{
    private sealed class NoopActivityLogger : IActivityLogger
    {
        public Task LogAsync(Guid businessId, string action, string entityType, Guid? entityId,
            string? entityName, string summary, string? details = null, ActivityActor? actor = null)
            => Task.CompletedTask;
    }

    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("mlxfer-" + Guid.NewGuid())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static StockTransferService Transfers(AppDbContext db) => new(db, new PlanGuard(db, new NoopActivityLogger()));

    private static async Task<Business> AddBizAsync(AppDbContext db, string plan = "scale")
    {
        var biz = new Business { Name = "T", AccountNumber = "A" + Guid.NewGuid().ToString("N")[..9], Plan = plan };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync(); // mirror seeds the default location
        return biz;
    }

    private static async Task<Location> AddLocAsync(AppDbContext db, Guid bizId, string name)
    {
        var l = new Location { BusinessId = bizId, Name = name, IsActive = true };
        db.Locations.Add(l);
        await db.SaveChangesAsync();
        return l;
    }

    // Product with `total` stock routed to location `at` via the dual-write mirror (business is multi-location).
    private static async Task<Product> AddProductWithStockAtAsync(AppDbContext db, Guid bizId, Guid at, decimal total, string name = "Widget")
    {
        var p = new Product { BusinessId = bizId, Name = name, Unit = "bag", CurrentStock = 0, IsActive = true };
        db.Products.Add(p);
        await db.SaveChangesAsync();
        try { LocationScope.Current = at; p.CurrentStock += total; await db.SaveChangesAsync(); }
        finally { LocationScope.Current = null; }
        return p;
    }

    private static async Task<decimal> PlsAsync(AppDbContext db, Guid productId, Guid locId) =>
        (await db.ProductLocationStocks.FirstOrDefaultAsync(x => x.ProductId == productId && x.LocationId == locId))?.CurrentStock ?? 0m;

    [Fact]
    public async Task Transfer_MovesStock_And_ProductTotalUnchanged()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        var main = biz.DefaultLocationId!.Value;
        var b = await AddLocAsync(db, biz.Id, "B");
        var p = await AddProductWithStockAtAsync(db, biz.Id, main, 50);

        var dto = await Transfers(db).TransferAsync(biz.Id, new CreateStockTransferRequest
        { ProductId = p.Id, FromLocationId = main, ToLocationId = b.Id, Quantity = 20 });

        Assert.Equal(30m, await PlsAsync(db, p.Id, main));  // source down
        Assert.Equal(20m, await PlsAsync(db, p.Id, b.Id));  // dest up (row created)
        Assert.Equal(50m, (await db.Products.FindAsync(p.Id))!.CurrentStock); // roll-up UNCHANGED
        Assert.Equal(20m, dto.Quantity);
        Assert.Equal("B", dto.ToLocationName);

        // Invariant: SUM(per-location) == Product.CurrentStock
        var sum = await db.ProductLocationStocks.Where(x => x.ProductId == p.Id).SumAsync(x => x.CurrentStock);
        Assert.Equal(50m, sum);
    }

    [Fact]
    public async Task Transfer_EmitsTransferOutAndTransferInMovementRows()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        var main = biz.DefaultLocationId!.Value;
        var b = await AddLocAsync(db, biz.Id, "B");
        var p = await AddProductWithStockAtAsync(db, biz.Id, main, 50);

        await Transfers(db).TransferAsync(biz.Id, new CreateStockTransferRequest
        { ProductId = p.Id, FromLocationId = main, ToLocationId = b.Id, Quantity = 15 });

        var outTxn = await db.InventoryTransactions.SingleAsync(t => t.Type == InventoryTransactionType.TransferOut);
        var inTxn = await db.InventoryTransactions.SingleAsync(t => t.Type == InventoryTransactionType.TransferIn);
        Assert.Equal(main, outTxn.LocationId);   // out leg stamped with the source
        Assert.Equal(b.Id, inTxn.LocationId);     // in leg stamped with the destination
        Assert.Equal(15m, outTxn.Quantity);
        Assert.Single(await db.StockTransfers.Where(t => t.BusinessId == biz.Id).ToListAsync());
    }

    [Fact]
    public async Task Transfer_CreatesDestinationRow_WhenMissing()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        var main = biz.DefaultLocationId!.Value;
        var b = await AddLocAsync(db, biz.Id, "B");
        var p = await AddProductWithStockAtAsync(db, biz.Id, main, 40);
        Assert.Equal(0m, await PlsAsync(db, p.Id, b.Id)); // no dest row yet

        await Transfers(db).TransferAsync(biz.Id, new CreateStockTransferRequest
        { ProductId = p.Id, FromLocationId = main, ToLocationId = b.Id, Quantity = 40 });

        Assert.Equal(0m, await PlsAsync(db, p.Id, main));
        Assert.Equal(40m, await PlsAsync(db, p.Id, b.Id));
    }

    [Fact]
    public async Task Transfer_InsufficientSourceStock_Throws_NothingMoved()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        var main = biz.DefaultLocationId!.Value;
        var b = await AddLocAsync(db, biz.Id, "B");
        var p = await AddProductWithStockAtAsync(db, biz.Id, main, 10);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Transfers(db).TransferAsync(biz.Id,
            new CreateStockTransferRequest { ProductId = p.Id, FromLocationId = main, ToLocationId = b.Id, Quantity = 25 }));

        Assert.Equal(10m, await PlsAsync(db, p.Id, main)); // untouched
        Assert.Empty(await db.StockTransfers.ToListAsync());
    }

    [Fact]
    public async Task Transfer_SameSourceAndDestination_Throws()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        var main = biz.DefaultLocationId!.Value;
        await AddLocAsync(db, biz.Id, "B");
        var p = await AddProductWithStockAtAsync(db, biz.Id, main, 50);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Transfers(db).TransferAsync(biz.Id,
            new CreateStockTransferRequest { ProductId = p.Id, FromLocationId = main, ToLocationId = main, Quantity = 5 }));
    }

    [Fact]
    public async Task Transfer_NotEntitled_Throws()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db, plan: "starter"); // no multi-location entitlement
        var main = biz.DefaultLocationId!.Value;
        var b = await AddLocAsync(db, biz.Id, "B");
        var p = await AddProductWithStockAtAsync(db, biz.Id, main, 50);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Transfers(db).TransferAsync(biz.Id,
            new CreateStockTransferRequest { ProductId = p.Id, FromLocationId = main, ToLocationId = b.Id, Quantity = 5 }));
    }

    [Fact]
    public async Task History_ReturnsTransfers_WithResolvedNames()
    {
        using var db = NewContext();
        var biz = await AddBizAsync(db);
        var main = biz.DefaultLocationId!.Value;
        var b = await AddLocAsync(db, biz.Id, "Ikeja");
        var p = await AddProductWithStockAtAsync(db, biz.Id, main, 50, name: "Rice");

        await Transfers(db).TransferAsync(biz.Id, new CreateStockTransferRequest
        { ProductId = p.Id, FromLocationId = main, ToLocationId = b.Id, Quantity = 12 });

        var page = await Transfers(db).GetAllAsync(biz.Id, 1, 20);
        var row = Assert.Single(page.Items);
        Assert.Equal("Rice", row.ProductName);
        Assert.Equal("Ikeja", row.ToLocationName);
        Assert.Equal(12m, row.Quantity);
    }
}
